using System;

namespace NarrowBeam;

/// <summary>
/// 4th-order Butterworth Low-Pass IIR Filter.
/// Much faster than FIR for real-time video processing.
/// Bandwidth limit: 2 * cutoffHz (due to AM double sideband).
/// </summary>
internal sealed class LowPassFilter
{
    // Biquad sections
    private readonly Biquad _section1;
    private readonly Biquad _section2;

    public LowPassFilter(double cutoffHz, double sampleRate)
    {
        // 4th order Butterworth = 2 cascaded Biquads (2nd order sections)
        // Q values for 4th order Butterworth:
        // Q1 = 0.54119610
        // Q2 = 1.3065630

        _section1 = new Biquad(cutoffHz, sampleRate, 0.54119610);
        _section2 = new Biquad(cutoffHz, sampleRate, 1.3065630);
    }

    public void Apply(double[] buffer, int offset, int count)
    {
        // Apply cascaded sections in-place
        _section1.Process(buffer, offset, count);
        _section2.Process(buffer, offset, count);
    }

    private class Biquad
    {
        private double _z1, _z2;
        private readonly double _a0, _a1, _a2, _b1, _b2;

        public Biquad(double fc, double fs, double q)
        {
            // Digital Biquad Design (Audio EQ Cookbook / RBJ)
            double w0 = 2.0 * Math.PI * fc / fs;
            double alpha = Math.Sin(w0) / (2.0 * q);
            double cosW0 = Math.Cos(w0);

            double b0_raw = (1.0 - cosW0) / 2.0;
            double b1_raw = 1.0 - cosW0;
            double b2_raw = (1.0 - cosW0) / 2.0;
            double a0_raw = 1.0 + alpha;
            double a1_raw = -2.0 * cosW0;
            double a2_raw = 1.0 - alpha;

            _a0 = b0_raw / a0_raw;
            _a1 = b1_raw / a0_raw;
            _a2 = b2_raw / a0_raw;
            _b1 = a1_raw / a0_raw;
            _b2 = a2_raw / a0_raw;
        }

        public void Process(double[] buffer, int offset, int count)
        {
            for (int i = offset; i < offset + count; i++)
            {
                double input = buffer[i];
                double output = _a0 * input + _a1 * _z1 + _a2 * _z2 - _b1 * _z1 - _b2 * _z2;
                
                // Re-implementing properly with explicit state:
                // Let's use Direct Form II (Canonical):
                // w[n] = x[n] - a1*w[n-1] - a2*w[n-2]
                // y[n] = b0*w[n] + b1*w[n-1] + b2*w[n-2]
                
                // My coefficients above (RBJ) use a1/a2 for denominator (y), but usually defined as 1 + a1*z^-1...
                // The formula used for _b1, _b2 calculation (a1_raw/a0_raw) implies standard difference equation:
                // y[n] = (b0/a0)*x[n] + (b1/a0)*x[n-1] + (b2/a0)*x[n-2] - (a1/a0)*y[n-1] - (a2/a0)*y[n-2]
                
                // So using Direct Form I:
                // y[n] = _a0*x[n] + _a1*x[n-1] + _a2*x[n-2] - _b1*y[n-1] - _b2*y[n-2]
                // State variables needed: x[n-1], x[n-2], y[n-1], y[n-2]
                // This requires 4 state variables per section.
                
                // Direct Form II is more efficient (2 state variables):
                // w[n] = x[n] - _b1*w[n-1] - _b2*w[n-2]
                // y[n] = _a0*w[n] + _a1*w[n-1] + _a2*w[n-2]
                
                double w = input - _b1 * _z1 - _b2 * _z2;
                output = _a0 * w + _a1 * _z1 + _a2 * _z2;
                
                _z2 = _z1;
                _z1 = w;

                buffer[i] = output;
            }
        }
    }
}
