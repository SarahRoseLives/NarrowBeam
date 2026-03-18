using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace NarrowBeam;

/// <summary>
/// Generates an NTSC composite video frame buffer in IRE units.
/// Thread-safe: use LockRaw/UnlockRaw around writes to RawFrameBuffer,
/// and LockFrame/UnlockFrame around calls to GenerateFullFrame / FrameBuffer reads.
/// </summary>
internal sealed class NtscSignal
{
    private const double FrameRate = 30_000.0 / 1_001.0;
    private const double Fsc = 3_579_545.4545;
    private const int LinesPerFrame = 525;

    private const double LevelSync = -40.0;
    private const double LevelBlanking = 0.0;
    private const double LevelBlack = 7.5;
    private const double LevelWhite = 100.0;
    private const double BurstAmp = 20.0;

    private static readonly (byte R, byte G, byte B)[] BarRgb =
    {
        (192, 192, 192),
        (192, 192,   0),
        (  0, 192, 192),
        (  0, 192,   0),
        (192,   0, 192),
        (192,   0,   0),
        (  0,   0, 192),
    };

    public const int FrameWidth = 540;
    public const int FrameHeight = 480;

    private readonly int _lineSamples;
    private readonly int _hSyncSamples;
    private readonly int _vSyncPulseSamples;
    private readonly int _eqPulseSamples;
    private readonly int _burstStartSamples;
    private readonly int _burstEndSamples;
    private readonly int _activeStartSamples;
    private readonly int _activeSamples;
    private readonly double _phaseInc;

    private readonly byte[] _rawFrame;
    private volatile double[] _frontBuffer;
    private double[] _backBuffer;
    private readonly LowPassFilter? _lpf;

    private readonly ReaderWriterLockSlim _rawLock = new();
    private readonly ReaderWriterLockSlim _frameLock = new();

    public byte[] RawFrameBuffer => _rawFrame;
    public double[] FrameBuffer => _frontBuffer;

    public NtscSignal(double sampleRate, double bandwidthHz = 0)
    {
        double lineDuration = 1.0 / (FrameRate * LinesPerFrame);
        _lineSamples = (int)(lineDuration * sampleRate);
        _hSyncSamples = (int)(4.7e-6 * sampleRate);
        _vSyncPulseSamples = (int)(27.1e-6 * sampleRate);
        _eqPulseSamples = (int)(2.3e-6 * sampleRate);
        _burstStartSamples = (int)(5.6e-6 * sampleRate);
        _burstEndSamples = _burstStartSamples + (int)(2.5e-6 * sampleRate);
        _activeStartSamples = (int)(10.7e-6 * sampleRate);
        _activeSamples = (int)(52.6e-6 * sampleRate);
        _phaseInc = 2.0 * Math.PI * Fsc / sampleRate;

        _rawFrame = new byte[FrameWidth * FrameHeight * 3];
        _frontBuffer = new double[_lineSamples * LinesPerFrame];
        _backBuffer = new double[_lineSamples * LinesPerFrame];

        if (bandwidthHz > 0)
            _lpf = new LowPassFilter(bandwidthHz / 2.0, sampleRate);
    }

    public static double IreToAmplitude(double ire) =>
        ((ire - 100.0) / -140.0) * (1.0 - 0.125) + 0.125;

    public void LockRaw() => _rawLock.EnterWriteLock();
    public void UnlockRaw() => _rawLock.ExitWriteLock();
    public void RLockRaw() => _rawLock.EnterReadLock();
    public void RUnlockRaw() => _rawLock.ExitReadLock();
    public void LockFrame() => _frameLock.EnterWriteLock();
    public void UnlockFrame() => _frameLock.ExitWriteLock();
    public void RLockFrame() => _frameLock.EnterReadLock();
    public void RUnlockFrame() => _frameLock.ExitReadLock();

    public void FillColorBars()
    {
        int barWidth = FrameWidth / 7;
        for (int y = 0; y < FrameHeight; y++)
        for (int x = 0; x < FrameWidth; x++)
        {
            int b = Math.Min(x / barWidth, 6);
            int i = (y * FrameWidth + x) * 3;
            _rawFrame[i] = BarRgb[b].R;
            _rawFrame[i + 1] = BarRgb[b].G;
            _rawFrame[i + 2] = BarRgb[b].B;
        }
    }

    public Bitmap GetPreviewBitmap()
    {
        var bitmap = new Bitmap(FrameWidth, FrameHeight, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, FrameWidth, FrameHeight);
        BitmapData bitmapData = bitmap.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        int rowBytes = FrameWidth * 3;
        byte[] rowBuffer = new byte[rowBytes];

        RLockRaw();
        try
        {
            for (int y = 0; y < FrameHeight; y++)
            {
                int srcOffset = y * rowBytes;
                for (int x = 0; x < FrameWidth; x++)
                {
                    int src = srcOffset + (x * 3);
                    int dst = x * 3;
                    rowBuffer[dst] = _rawFrame[src + 2];
                    rowBuffer[dst + 1] = _rawFrame[src + 1];
                    rowBuffer[dst + 2] = _rawFrame[src];
                }

                IntPtr rowPtr = IntPtr.Add(bitmapData.Scan0, y * bitmapData.Stride);
                Marshal.Copy(rowBuffer, 0, rowPtr, rowBytes);
            }
        }
        finally
        {
            RUnlockRaw();
            bitmap.UnlockBits(bitmapData);
        }

        return bitmap;
    }

    public void GenerateFullFrame()
    {
        double subPhase = 0.0;

        for (int line = 1; line <= LinesPerFrame; line++)
        {
            double[] lineBuffer = GenerateLumaLine(line);
            bool isVbi = (line >= 1 && line <= 21) || (line >= 264 && line <= 284);

            if (!isVbi)
            {
                for (int s = 0; s < _lineSamples; s++)
                {
                    if (s >= _burstStartSamples && s < _burstEndSamples)
                    {
                        lineBuffer[s] += BurstAmp * Math.Sin(subPhase + Math.PI);
                    }
                    else if (s >= _activeStartSamples && s < _activeStartSamples + _activeSamples)
                    {
                        GetPixelYiq(line, s, out _, out double iVal, out double qVal);
                        lineBuffer[s] += iVal * Math.Cos(subPhase) + qVal * Math.Sin(subPhase);
                    }

                    subPhase += _phaseInc;
                }
            }
            else
            {
                subPhase += _phaseInc * _lineSamples;
            }

            int offset = (line - 1) * _lineSamples;
            Array.Copy(lineBuffer, 0, _backBuffer, offset, _lineSamples);
        }

        _lpf?.Apply(_backBuffer, 0, _backBuffer.Length);

        _frameLock.EnterWriteLock();
        try
        {
            double[] temp = _frontBuffer;
            _frontBuffer = _backBuffer;
            _backBuffer = temp;
        }
        finally
        {
            _frameLock.ExitWriteLock();
        }
    }

    private double[] GenerateLumaLine(int line)
    {
        var buf = new double[_lineSamples];
        Array.Fill(buf, LevelBlanking);

        int lineInField = line > LinesPerFrame / 2 ? line - LinesPerFrame / 2 : line;
        bool isVbi = lineInField <= 21;
        int halfLine = _lineSamples / 2;

        if ((lineInField >= 1 && lineInField <= 3) || (lineInField >= 7 && lineInField <= 9))
        {
            for (int s = 0; s < _eqPulseSamples; s++)
            {
                buf[s] = LevelSync;
                buf[halfLine + s] = LevelSync;
            }
            return buf;
        }

        if (lineInField >= 4 && lineInField <= 6)
        {
            for (int s = 0; s < _vSyncPulseSamples; s++)
            {
                buf[s] = LevelSync;
                buf[halfLine + s] = LevelSync;
            }
            return buf;
        }

        for (int s = 0; s < _hSyncSamples; s++)
            buf[s] = LevelSync;

        if (!isVbi)
        {
            for (int s = 0; s < _activeSamples; s++)
            {
                GetPixelYiq(line, _activeStartSamples + s, out double y, out _, out _);
                buf[_activeStartSamples + s] = y;
            }
        }

        return buf;
    }

    private void GetPixelYiq(int line, int sampleInLine, out double y, out double iVal, out double qVal)
    {
        int videoLine;
        if (line >= 22 && line <= 263)
            videoLine = (line - 22) * 2;
        else if (line >= 285 && line <= 525)
            videoLine = (line - 285) * 2 + 1;
        else
        {
            y = LevelBlack;
            iVal = 0;
            qVal = 0;
            return;
        }

        int sampleInActive = sampleInLine - _activeStartSamples;
        int pixelX = (int)((double)sampleInActive / _activeSamples * FrameWidth);

        if (videoLine < 0 || videoLine >= FrameHeight || pixelX < 0 || pixelX >= FrameWidth)
        {
            y = LevelBlack;
            iVal = 0;
            qVal = 0;
            return;
        }

        int idx = (videoLine * FrameWidth + pixelX) * 3;
        double r = _rawFrame[idx];
        double g = _rawFrame[idx + 1];
        double b = _rawFrame[idx + 2];

        double yRaw = 0.299 * r + 0.587 * g + 0.114 * b;
        double iRaw = 0.596 * r - 0.274 * g - 0.322 * b;
        double qRaw = 0.211 * r - 0.523 * g + 0.312 * b;

        y = LevelBlack + yRaw / 255.0 * (LevelWhite - LevelBlack);
        iVal = iRaw / 255.0 * (LevelWhite - LevelBlack);
        qVal = qRaw / 255.0 * (LevelWhite - LevelBlack);
    }
}
