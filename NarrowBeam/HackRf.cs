using System;
using System.Runtime.InteropServices;

namespace NarrowBeam;

/// <summary>
/// P/Invoke bindings for libhackrf (hackrf.dll on Windows).
/// Requires the HackRF Windows driver package to be installed and
/// hackrf.dll / libusb-1.0.dll to be on the PATH or in the output directory.
/// </summary>
internal static class HackRf
{
    private const string Lib = "hackrf";

    public const int Success = 0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate int TransferCallback(ref Transfer transfer);

    [StructLayout(LayoutKind.Sequential)]
    public struct Transfer
    {
        public IntPtr Device;
        public IntPtr Buffer;
        public int BufferLength;
        public int ValidLength;
        public IntPtr RxCtx;
        public IntPtr TxCtx;
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_init();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_exit();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_open(out IntPtr device);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_open_by_serial(string desiredSerialNumber, out IntPtr device);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr hackrf_device_list();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_device_list_open(IntPtr list, int idx, out IntPtr device);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern void hackrf_device_list_free(IntPtr list);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_close(IntPtr device);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_set_freq(IntPtr device, ulong freqHz);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_set_sample_rate(IntPtr device, double samplesPerSec);

    /// <summary>TX VGA (IF) gain: 0–47 dB in 1 dB steps.</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_set_txvga_gain(IntPtr device, uint value);

    /// <summary>Enable/disable the on-board amplifier (~14 dB).</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_set_amp_enable(IntPtr device, byte value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_start_tx(IntPtr device, TransferCallback callback, IntPtr txCtx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_stop_tx(IntPtr device);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_start_rx(IntPtr device, TransferCallback callback, IntPtr rxCtx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_stop_rx(IntPtr device);

    /// <summary>LNA gain: 0–40 dB in 8 dB steps.</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_set_lna_gain(IntPtr device, uint value);

    /// <summary>VGA (baseband) gain: 0–62 dB in 2 dB steps.</summary>
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_set_vga_gain(IntPtr device, uint value);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int hackrf_is_streaming(IntPtr device);

    public static void Check(int result, string operation)
    {
        if (result != Success)
            throw new InvalidOperationException($"HackRF error {result} during: {operation}");
    }
}
