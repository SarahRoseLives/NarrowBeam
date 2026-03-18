using System;
using System.Runtime.InteropServices;

namespace NarrowBeam;

internal static class RtlSdr
{
    private const string Lib = "rtlsdr";

    public const int Success = 0;

    // rtl_sdr_read_async_cb_t
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ReadAsyncCallback(IntPtr buf, uint len, IntPtr ctx);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int rtlsdr_open(out IntPtr dev, uint index);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int rtlsdr_close(IntPtr dev);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int rtlsdr_set_center_freq(IntPtr dev, uint freq);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int rtlsdr_set_sample_rate(IntPtr dev, uint rate);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int rtlsdr_set_tuner_gain_mode(IntPtr dev, int manual);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int rtlsdr_set_tuner_gain(IntPtr dev, int gain);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int rtlsdr_reset_buffer(IntPtr dev);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int rtlsdr_read_async(IntPtr dev, ReadAsyncCallback cb, IntPtr ctx, uint buf_num, uint buf_len);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int rtlsdr_cancel_async(IntPtr dev);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint rtlsdr_get_device_count();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern IntPtr rtlsdr_get_device_name(uint index);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    public static extern int rtlsdr_get_tuner_gains(IntPtr dev, [Out] int[] gains);

    public static void Check(int result, string operation)
    {
        if (result != Success)
            throw new InvalidOperationException($"RTL-SDR error {result} during: {operation}");
    }
}
