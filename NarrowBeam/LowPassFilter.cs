using System;

namespace NarrowBeam;

/// <summary>
/// FIR low-pass filter with a Blackman window.
/// Used to limit the composite video bandwidth before AM modulation so the
/// transmitted RF signal fits inside a given channel width.
///
/// RF bandwidth = 2 × cutoff frequency (because AM produces symmetric sidebands).
/// e.g. cutoffHz = 1 MHz  →  2 MHz RF bandwidth (fits an RTL-SDR at 2.4 MSPS).
/// </summary>
internal sealed class LowPassFilter
{
    private readonly double[] _taps;
    private readonly double[] _history;
    private int _histIdx;

    public int Delay => (_taps.Length - 1) / 2;

    public LowPassFilter(int numTaps, double cutoffHz, double sampleRate)
    {
        _taps    = BuildTaps(numTaps, cutoffHz, sampleRate);
        _history = new double[numTaps];
        _histIdx = 0;
    }

    /// <summary>
    /// Filter an array of samples in-place using the FIR taps.
    /// Operates as a causal filter with an internal delay line.
    /// </summary>
    public void Apply(double[] buffer, int offset, int count)
    {
        for (int n = offset; n < offset + count; n++)
        {
            _history[_histIdx] = buffer[n];

            double acc = 0.0;
            for (int k = 0; k < _taps.Length; k++)
            {
                int idx = (_histIdx - k + _taps.Length) % _taps.Length;
                acc += _taps[k] * _history[idx];
            }

            buffer[n] = acc;
            _histIdx  = (_histIdx + 1) % _taps.Length;
        }
    }

    // ── Filter design ─────────────────────────────────────────────────────────
    private static double[] BuildTaps(int numTaps, double cutoffHz, double sampleRate)
    {
        var taps = new double[numTaps];
        double normalizedCutoff = cutoffHz / sampleRate;
        double M = numTaps - 1;
        double sum = 0.0;

        for (int i = 0; i < numTaps; i++)
        {
            // Blackman window
            double window = 0.42
                - 0.50 * Math.Cos(2 * Math.PI * i / M)
                + 0.08 * Math.Cos(4 * Math.PI * i / M);

            // Windowed sinc
            double sinc;
            if (i == (int)(M / 2))
                sinc = 2 * Math.PI * normalizedCutoff;
            else
                sinc = Math.Sin(2 * Math.PI * normalizedCutoff * (i - M / 2)) / (i - M / 2);

            taps[i] = sinc * window;
            sum    += taps[i];
        }

        // Normalise to unity gain at DC
        for (int i = 0; i < numTaps; i++)
            taps[i] /= sum;

        return taps;
    }
}
