using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;

namespace NarrowBeam;

/// <summary>
/// Simple AM Video Demodulator.
/// Reverted to the direct sample-processing logic that was working previously.
/// </summary>
internal sealed class AmVideoDemodulator
{
    private const int FrameWidth = 640;
    private const int FrameHeight = 480;
    
    // NTSC timing (approximate for robust locking)
    private readonly double _nominalSamplesPerLine;

    private readonly double _sampleRate;
    // PLL period — starts at nominal NTSC line duration, adapts to actual signal
    private double _samplesPerLine;
    
    // State
    private double _smoothedMax = 128.0; // Tracks Sync Tip (Max RF)
    private double _smoothedMin = 0.0;   // Tracks White (Min RF)
    
    private double _x; // Horizontal position (0.0 to _samplesPerLine)
    private int _y;    // Vertical line
    
    private int _pixelCounter; // For pulse width measurement
    private int _vSyncIntegrator;
    // Set true once we've seen enough V-sync broad pulses; cleared on the first
    // normal H-sync afterwards — that edge is the stable frame boundary.
    private bool _vsyncPending = false;

    private readonly byte[] _frameBuffer;
    private readonly byte[] _displayBuffer;
    private readonly object _frameLock = new();
    private readonly Bitmap _bitmap;
    private bool _inSyncPulse = false;

    public AmVideoDemodulator(double sampleRate)
    {
        _sampleRate = sampleRate;
        _nominalSamplesPerLine = sampleRate / 15734.264; // NTSC line rate
        _samplesPerLine = _nominalSamplesPerLine;
        
        _frameBuffer = new byte[FrameWidth * FrameHeight * 3];
        _displayBuffer = new byte[FrameWidth * FrameHeight * 3];
        _bitmap = new Bitmap(FrameWidth, FrameHeight, PixelFormat.Format24bppRgb);
    }

    // DC Blocking State
    private double _dcI = 0;
    private double _dcQ = 0;
    private const double DcAlpha = 0.0001;

    // Post-demodulation low-pass filter (single-stage IIR, ~1 MHz @ 2.4 MSPS).
    // Light smoothing to reduce shot noise without blurring horizontal detail.
    // Two-stage was ~383 kHz — too tight, causing horizontal blur.
    private double _lpf1;

    // ── Tunable parameters (adjustable from UI sliders) ──────────────────────
    /// <summary>LPF smoothing factor: 0.5 (smooth/noisy) → 1.0 (sharp/grainy).</summary>
    public double LpfAlpha { get; set; } = 0.85;
    /// <summary>Sync detection threshold as fraction of envelope (0.05–0.40).</summary>
    public double SyncThreshold { get; set; } = 0.20;
    /// <summary>Active video start as fraction of line period (back-porch end).</summary>
    public double ActiveStart { get; set; } = 0.26;
    /// <summary>Active video width as fraction of line period.</summary>
    public double ActiveWidth { get; set; } = 0.74;
    /// <summary>Black level offset as fraction of envelope range.</summary>
    public double BlackLevelOffset { get; set; } = 0.28;

    // Tracks the last horizontal pixel drawn on the current line for gap-filling.
    private int _lastPx = -1;

    private void PublishFrame()
    {
        lock (_frameLock)
        {
            Array.Copy(_frameBuffer, _displayBuffer, _displayBuffer.Length);
        }
    }

    private void BeginNewFrame(bool publishCurrentFrame)
    {
        if (publishCurrentFrame)
        {
            PublishFrame();
        }

        Array.Clear(_frameBuffer, 0, _frameBuffer.Length);
        _x = 0;
        _y = 0;
        _lastPx = -1;
    }

    public unsafe void ProcessIq(byte* iqData, int length)
    {
        int samples = length / 2;
        
        // AGC Constants – slower decay so the envelope doesn't drop between bursts
        const double AgcAttack = 0.001;
        const double AgcDecay  = 0.000005;

        // PLL horizontal sync: _x is a phase accumulator.  On each H-sync rising
        // edge the PLL steers _x toward 0 (phase) and _samplesPerLine toward the
        // actual line period (frequency).  Pulse-width classification still happens
        // on the falling edge so equalizing/V-sync pulses don't perturb the PLL.

        for (int i = 0; i < samples; i++)
        {
            // ── 1. IQ → magnitude ───────────────────────────────────────────
            double iSample = iqData[i * 2]     - 127.5;
            double qSample = iqData[i * 2 + 1] - 127.5;

            // DC blocker (removes hardware LO leakage at 0 Hz)
            _dcI = _dcI * (1.0 - DcAlpha) + iSample * DcAlpha;
            _dcQ = _dcQ * (1.0 - DcAlpha) + qSample * DcAlpha;
            double iClean = iSample - _dcI;
            double qClean = qSample - _dcQ;

            // Fast magnitude approximation
            double absI = iClean > 0 ? iClean : -iClean;
            double absQ = qClean > 0 ? qClean : -qClean;
            double mag = (absI > absQ ? absI : absQ) + 0.4 * (absI > absQ ? absQ : absI);

            // ── 2. Post-demod low-pass filter ────────────────────────────────
            // Single-stage IIR. LpfAlpha is tunable (0.5=smooth → 1.0=sharp).
            // Sync detection uses raw magnitude so edges stay sharp.
            _lpf1 = _lpf1 + LpfAlpha * (mag - _lpf1);
            double filteredMag = _lpf1;

            // ── 3. AGC (tracks on raw magnitude to catch sync tips accurately) ─
            if (mag > _smoothedMax)
                _smoothedMax = _smoothedMax * (1.0 - AgcAttack) + mag * AgcAttack;
            else
                _smoothedMax = _smoothedMax * (1.0 - AgcDecay)  + mag * AgcDecay;

            if (mag < _smoothedMin)
                _smoothedMin = _smoothedMin * (1.0 - AgcAttack) + mag * AgcAttack;
            else
                _smoothedMin = _smoothedMin * (1.0 - AgcDecay)  + mag * AgcDecay;

            double range = _smoothedMax - _smoothedMin;
            if (range < 1.0) range = 1.0;

            // ── 4. Sync detection (on raw mag — needs sharp edges) ───────────
            // NTSC negative modulation: sync tips are at MAX RF level.
            bool isSyncSample = mag > (_smoothedMax - range * SyncThreshold);

            if (isSyncSample && !_inSyncPulse)
            {
                // ── Rising edge of sync ──────────────────────────────────────
                _inSyncPulse = true;
                _pixelCounter = 0;
                _lastPx = -1;

                // H-sync PLL: steer phase and frequency toward lock.
                // _x should be ~0 at each rising edge when locked.
                // Kp corrects phase immediately; Kf drifts the period slowly.
                const double Kp = 0.4;
                const double Kf = 0.0005;
                double phaseError = _x;
                _x        -= Kp * phaseError;
                _samplesPerLine -= Kf * phaseError;

                // Clamp period to ±15% of nominal to prevent runaway
                double lo = _nominalSamplesPerLine * 0.85;
                double hi = _nominalSamplesPerLine * 1.15;
                if (_samplesPerLine < lo) _samplesPerLine = lo;
                if (_samplesPerLine > hi) _samplesPerLine = hi;
            }
            else if (!isSyncSample && _inSyncPulse)
            {
                // ── Falling edge – classify by pulse width ───────────────────
                _inSyncPulse = false;

                // At 2.4 MSPS:
                //   Equalizing ≈ 2.3 µs  →  ~5.5 samples  (ignored — < hMin)
                //   H-sync     ≈ 4.7 µs  → ~11.3 samples
                //   V-sync     ≈ 27.1 µs → ~65   samples
                //
                // hMin rejects equalizing pulses that would otherwise count as H-sync
                // and vertically shift the raster by ~12 lines per frame.
                double hMin = _samplesPerLine * 0.05;   // > 5 %  → not equalizing
                double hMax = _samplesPerLine * 0.20;   // < 20 % → H-sync
                double vMin = _samplesPerLine * 0.35;   // > 35 % → V-sync broad

                if (_pixelCounter > hMin && _pixelCounter <= hMax)
                {
                    if (_vsyncPending && _y > 400)
                    {
                        // Full-frame V-sync (field 1 boundary) — reset display frame.
                        BeginNewFrame(_y > 30);
                        _vsyncPending = false;
                        _vSyncIntegrator = 0;
                        _y++;
                    }
                    else
                    {
                        // Normal H-sync, or mid-frame field-2 V-sync (ignored as frame boundary).
                        if (_vsyncPending)
                        {
                            // Field-2 V-sync: reset detector, keep counting lines.
                            _vsyncPending = false;
                            _vSyncIntegrator = 0;
                        }
                        _y++;
                        if (_vSyncIntegrator > 0) _vSyncIntegrator--;
                    }
                }
                else if (_pixelCounter >= vMin)
                {
                    // V-sync broad pulse — accumulate until we're confident
                    _vSyncIntegrator++;
                    if (_vSyncIntegrator >= 3)
                        _vsyncPending = true;
                }
                // Equalizing pulses (< hMin) and anything in-between fall through.
            }

            if (_inSyncPulse) _pixelCounter++;

            // ── 5. Draw pixel (with gap-filling) ─────────────────────────────
            if (!_inSyncPulse && _y >= 0 && _y < FrameHeight)
            {
                double activeStart = _samplesPerLine * ActiveStart;
                double activeWidth = _samplesPerLine * ActiveWidth;

                if (_x >= activeStart)
                {
                    int px = (int)((_x - activeStart) / activeWidth * FrameWidth);

                    if (px >= 0 && px < FrameWidth)
                    {
                        // NTSC levels (negative modulation)
                        double blackLevel = _smoothedMax - range * BlackLevelOffset;
                        double whiteLevel = _smoothedMin + range * 0.05;
                        double videoRange = blackLevel - whiteLevel;
                        if (videoRange < 1.0) videoRange = 1.0;

                        // Use filtered magnitude for pixel brightness
                        double brightness = (blackLevel - filteredMag) / videoRange;
                        if (brightness < 0.0) brightness = 0.0;
                        if (brightness > 1.0) brightness = 1.0;

                        byte b = (byte)(brightness * 255.0);

                        // Fill every pixel from _lastPx+1 to px so the raster has
                        // no dark gaps between the sparse IQ sample positions.
                        int fillFrom = (_lastPx < 0) ? px : _lastPx + 1;
                        int rowBase  = _y * FrameWidth;
                        for (int p = fillFrom; p <= px && p < FrameWidth; p++)
                        {
                            int idx = (rowBase + p) * 3;
                            _frameBuffer[idx]     = b;
                            _frameBuffer[idx + 1] = b;
                            _frameBuffer[idx + 2] = b;
                        }

                        _lastPx = px;
                    }
                }
            }

            // ── 6. Advance horizontal counter (PLL phase accumulator) ────────
            _x++;

            // Horizontal flywheel: fires only if no H-sync arrives within one
            // full line period. With PLL locked this should never trigger.
            if (_x > _samplesPerLine * 1.1)
            {
                _x = 0;
                _y++;
                _lastPx = -1;
            }

            // Vertical flywheel: safety net ONLY — should not fire during normal operation.
            // A full NTSC frame is 525 lines; V-sync triggers BeginNewFrame at _y ≈ 509.
            // Set ceiling well above that so V-sync always fires first.
            if (_y >= 540)
                BeginNewFrame(true);
        }
    }

    public Bitmap GetFrame()
    {
        // Return a copy of the bitmap to avoid threading issues in UI
        lock (_bitmap)
        {
            BitmapData bits = _bitmap.LockBits(new Rectangle(0, 0, FrameWidth, FrameHeight), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            
            lock (_frameLock)
            {
                Marshal.Copy(_displayBuffer, 0, bits.Scan0, _displayBuffer.Length);
            }
            
            _bitmap.UnlockBits(bits);
            return new Bitmap(_bitmap);
        }
    }
}
