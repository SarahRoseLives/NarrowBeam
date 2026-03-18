using System;
using System.Runtime.InteropServices;

namespace NarrowBeam;

internal sealed class TransmitterSession : IDisposable
{
    private const double SampleRate = 8_000_000.0;

    private readonly Action<string> _log;
    private IntPtr _device;
    private bool _hackRfInitialised;
    private bool _streaming;
    private NtscSignal? _ntsc;
    private WebcamCapture? _webcam;
    private HackRf.TransferCallback? _txCallback;
    private int _sampleCounter;

    public bool IsRunning => _streaming;
    public NtscSignal? NtscSignal => _ntsc;

    public TransmitterSession(Action<string> log)
    {
        _log = log;
    }

    public void Start(TransmitterSettings settings)
    {
        if (_streaming)
            throw new InvalidOperationException("The transmitter is already running.");

        _ntsc = new NtscSignal(SampleRate, settings.BandwidthMhz * 1_000_000.0);
        _ntsc.FillColorBars();
        _ntsc.GenerateFullFrame();

        if (!settings.UseTestPattern)
            _webcam = new WebcamCapture(_ntsc, settings.DeviceName, settings.Callsign, _log);

        HackRf.Check(HackRf.hackrf_init(), "hackrf_init");
        _hackRfInitialised = true;

        HackRf.Check(HackRf.hackrf_open(out _device), "hackrf_open");
        HackRf.Check(HackRf.hackrf_set_sample_rate(_device, SampleRate), "set_sample_rate");
        HackRf.Check(HackRf.hackrf_set_freq(_device, (ulong)(settings.FrequencyMhz * 1_000_000.0)), "set_freq");
        HackRf.Check(HackRf.hackrf_set_txvga_gain(_device, (uint)settings.GainDb), "set_txvga_gain");
        HackRf.Check(HackRf.hackrf_set_amp_enable(_device, settings.AmpEnabled ? (byte)1 : (byte)0), "set_amp_enable");

        _txCallback = TransferCallback;
        HackRf.Check(HackRf.hackrf_start_tx(_device, _txCallback, IntPtr.Zero), "start_tx");
        _streaming = true;

        _log($"Transmitting at {settings.FrequencyMhz:F3} MHz, bandwidth {settings.BandwidthMhz:F1} MHz.");
    }

    public void Stop()
    {
        if (_streaming && _device != IntPtr.Zero)
        {
            int stopResult = HackRf.hackrf_stop_tx(_device);
            if (stopResult != HackRf.Success)
                throw new InvalidOperationException($"HackRF error {stopResult} during: stop_tx");

            _streaming = false;
        }

        _webcam?.Dispose();
        _webcam = null;

        if (_device != IntPtr.Zero)
        {
            HackRf.Check(HackRf.hackrf_close(_device), "hackrf_close");
            _device = IntPtr.Zero;
        }

        if (_hackRfInitialised)
        {
            HackRf.Check(HackRf.hackrf_exit(), "hackrf_exit");
            _hackRfInitialised = false;
        }

        _log("Transmitter stopped.");
    }

    public void Dispose()
    {
        if (_streaming || _device != IntPtr.Zero || _hackRfInitialised || _webcam is not null)
            Stop();
    }

    private int TransferCallback(ref HackRf.Transfer transfer)
    {
        if (_ntsc is null)
            return -1;

        int samplesToWrite = transfer.BufferLength / 2;
        var output = new byte[transfer.BufferLength];

        _ntsc.RLockFrame();
        try
        {
            double[] frameBuffer = _ntsc.FrameBuffer;
            for (int i = 0; i < samplesToWrite; i++)
            {
                double ire = frameBuffer[_sampleCounter];
                double amplitude = NtscSignal.IreToAmplitude(ire);
                output[i * 2] = (byte)(sbyte)(amplitude * 127.0);
                output[i * 2 + 1] = 0;

                _sampleCounter++;
                if (_sampleCounter >= frameBuffer.Length)
                    _sampleCounter = 0;
            }
        }
        finally
        {
            _ntsc.RUnlockFrame();
        }

        Marshal.Copy(output, 0, transfer.Buffer, transfer.BufferLength);
        return 0;
    }
}
