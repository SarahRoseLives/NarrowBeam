using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace NarrowBeam;

/// <summary>
/// AM Video Demodulator ported from rtl_tv.
/// Processes I/Q samples to recover NTSC video.
/// </summary>
internal sealed class AmVideoDemodulator
{
    private const int FrameWidth = 540;
    private const int FrameHeight = 480;
    
    // NTSC timing
    private const double FrameRate = 30_000.0 / 1_001.0;
    private const double LinesPerFrame = 525;
    private const double HSyncDurationUs = 4.7;
    private const double FrontPorchUs = 1.5;
    private const double ActiveVideoUs = 52.6;

    private readonly double _sampleRate;
    private readonly double _initialSamplesPerLine;
    
    private double _samplesPerLine;
    private double _hSyncErrorAccumulator;
    
    private int _hSyncPulseWidth;
    private int _syncSearchWindow;
    private int _lineStartActiveVideo;
    private int _lineEndActiveVideo;

    private readonly byte[] _frameBuffer;
    private readonly byte[] _displayBuffer;
    private readonly object _frameLock = new();

    // State
    private int _x, _y;
    private int _pixelCounter;
    private double _smoothedMax = 128.0;
    private double _smoothedMin = 0.0;
    
    private enum VSyncState { Search, InSync }
    private VSyncState _vSyncState = VSyncState.Search;
    private int _vSyncSerrationCounter;

    public AmVideoDemodulator(double sampleRate)
    {
        _sampleRate = sampleRate;
        double lineDuration = 1.0 / (FrameRate * LinesPerFrame);
        _initialSamplesPerLine = lineDuration * sampleRate;
        _samplesPerLine = _initialSamplesPerLine;

        _hSyncPulseWidth = (int)(HSyncDurationUs * 1e-6 * sampleRate);
        _syncSearchWindow = (int)(_samplesPerLine * 0.20);

        double activeStartUs = HSyncDurationUs + FrontPorchUs;
        _lineStartActiveVideo = (int)(activeStartUs * 1e-6 * sampleRate);
        _lineEndActiveVideo = _lineStartActiveVideo + (int)(ActiveVideoUs * 1e-6 * sampleRate);

        _frameBuffer = new byte[FrameWidth * FrameHeight * 3];
        _displayBuffer = new byte[FrameWidth * FrameHeight * 3];
    }

    public unsafe void ProcessIq(byte* iqData, int length)
    {
        // length is total bytes, so length/2 samples
        int samples = length / 2;
        
        // Define levels based on smoothed AGC
        double syncTipLevel = _smoothedMax;
        double peakWhiteLevel = _smoothedMin;
        double syncThreshold = syncTipLevel * 0.75;
        double blackLevel = syncTipLevel * 0.65;
        double levelCoeff = 255.0 / (blackLevel - peakWhiteLevel + 1e-6);

        // PI Loop constants
        const double Kp = 0.002;
        const double Ki = 0.0001;

        for (int i = 0; i < samples; i++)
        {
            // AM Demod (magnitude)
            // RTL-SDR is unsigned 8-bit, 127 center
            double iSample = (double)iqData[i * 2] - 127.5;
            double qSample = (double)iqData[i * 2 + 1] - 127.5;
            double mag = Math.Sqrt(iSample * iSample + qSample * qSample);

            // Fast AGC
            if (mag > _smoothedMax) _smoothedMax = _smoothedMax * 0.95 + mag * 0.05;
            else _smoothedMax = _smoothedMax * 0.999 + mag * 0.001; // Decay slower

            if (mag < _smoothedMin) _smoothedMin = _smoothedMin * 0.95 + mag * 0.05;
            else _smoothedMin = _smoothedMin * 0.999 + mag * 0.001; // Decay slower

            // --- Sync Detection ---
            if (_x < _syncSearchWindow)
            {
                if (mag >= syncThreshold)
                {
                    _pixelCounter++;
                }
                else
                {
                    if (_pixelCounter > _hSyncPulseWidth / 2) // Found pulse
                    {
                        bool isLongPulse = _pixelCounter > _hSyncPulseWidth * 2;

                        if (_vSyncState == VSyncState.Search)
                        {
                            if (_y > (FrameHeight - 20) && isLongPulse)
                            {
                                _vSyncState = VSyncState.InSync;
                                _vSyncSerrationCounter = 1;
                            }
                            else
                            {
                                // H-Sync PLL
                                double error = (double)_x - _samplesPerLine;
                                _hSyncErrorAccumulator += error * Ki;
                                double correction = (error * Kp) + _hSyncErrorAccumulator;
                                
                                // Adjust line length
                                _samplesPerLine += correction;

                                // Clamp
                                if (_samplesPerLine < _initialSamplesPerLine * 0.95)
                                    _samplesPerLine = _initialSamplesPerLine * 0.95;
                                if (_samplesPerLine > _initialSamplesPerLine * 1.05)
                                    _samplesPerLine = _initialSamplesPerLine * 1.05;

                                // New Line
                                _y++;
                                _x = 0;
                            }
                        }
                        else // InSync
                        {
                            if (isLongPulse && _vSyncSerrationCounter < 6)
                            {
                                _vSyncSerrationCounter++;
                            }
                            else
                            {
                                if (_vSyncSerrationCounter >= 3)
                                {
                                    // V-Sync Confirmed
                                    _y = 0;
                                    _x = 0;
                                    _samplesPerLine = _initialSamplesPerLine;
                                    _hSyncErrorAccumulator = 0.0;
                                }
                                _vSyncState = VSyncState.Search;
                                _vSyncSerrationCounter = 0;
                            }
                        }
                        _pixelCounter = 0;
                        continue;
                    }
                    _pixelCounter = 0;
                }
            }

            // --- Video Drawing ---
            if (_y >= 0 && _y < FrameHeight && _x >= _lineStartActiveVideo && _x < _lineEndActiveVideo)
            {
                double samplesInActive = (double)(_lineEndActiveVideo - _lineStartActiveVideo);
                double relativeSample = (double)(_x - _lineStartActiveVideo);
                int pixelX = (int)(relativeSample / samplesInActive * FrameWidth);

                if (pixelX >= 0 && pixelX < FrameWidth)
                {
                    double brightness = (blackLevel - mag) * levelCoeff;
                    byte val = (byte)Math.Clamp(brightness, 0, 255);

                    int idx = (_y * FrameWidth + pixelX) * 3;
                    _frameBuffer[idx] = val;     // B
                    _frameBuffer[idx + 1] = val; // G
                    _frameBuffer[idx + 2] = val; // R
                }
            }

            _x++;

            // Flywheel
            if (_x >= (int)_samplesPerLine)
            {
                _x = 0;
                _y++;
            }

            if (_y >= FrameHeight)
            {
                _y = 0;
                lock (_frameLock)
                {
                    Array.Copy(_frameBuffer, _displayBuffer, _displayBuffer.Length);
                }
            }
        }
    }

    public Bitmap GetFrame()
    {
        var bmp = new Bitmap(FrameWidth, FrameHeight, PixelFormat.Format24bppRgb);
        var rect = new Rectangle(0, 0, FrameWidth, FrameHeight);
        var data = bmp.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);

        lock (_frameLock)
        {
            Marshal.Copy(_displayBuffer, 0, data.Scan0, _displayBuffer.Length);
        }
        
        bmp.UnlockBits(data);
        return bmp;
    }
}
