using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace NarrowBeam;

internal sealed class WebcamCapture : IDisposable
{
    private readonly NtscSignal _ntsc;
    private readonly Process _ffmpeg;
    private readonly Thread _readerThread;
    private readonly Action<string>? _log;
    private volatile bool _running = true;

    public WebcamCapture(NtscSignal ntsc, string? deviceName, Action<string>? log = null)
    {
        _ntsc = ntsc;
        _log = log;

        string resolvedDevice = string.IsNullOrWhiteSpace(deviceName)
            ? VideoDeviceDiscovery.GetDefaultVideoDevice()
            : deviceName;

        string vfArg = $"setrange=full,scale={NtscSignal.FrameWidth}:{NtscSignal.FrameHeight},fps=30000/1001";
        string arguments = string.Join(" ",
            "-hide_banner -loglevel warning",
            "-f dshow",
            "-rtbufsize 256M",
            "-framerate 30000/1001",
            $"-i \"video={resolvedDevice}\"",
            "-fflags nobuffer -flags low_delay",
            "-probesize 32 -analyzeduration 0",
            "-threads 1",
            "-f rawvideo -pix_fmt rgb24",
            $"-vf \"{vfArg}\"",
            "-");

        _ffmpeg = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = VideoDeviceDiscovery.ResolveFfmpegExecutable(),
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        _ffmpeg.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data))
                _log?.Invoke($"[ffmpeg] {e.Data}");
        };

        _ffmpeg.Start();
        _ffmpeg.BeginErrorReadLine();
        _log?.Invoke($"FFmpeg started for device: {resolvedDevice}");

        _readerThread = new Thread(ReadLoop)
        {
            IsBackground = true,
            Name = "FFmpegReader",
        };
        _readerThread.Start();
    }

    private void ReadLoop()
    {
        int frameSize = NtscSignal.FrameWidth * NtscSignal.FrameHeight * 3;
        var frameBuf = new byte[frameSize];
        Stream stdout = _ffmpeg.StandardOutput.BaseStream;

        while (_running)
        {
            int bytesRead = 0;
            while (bytesRead < frameSize)
            {
                int read = stdout.Read(frameBuf, bytesRead, frameSize - bytesRead);
                if (read == 0)
                {
                    _running = false;
                    _log?.Invoke("FFmpeg stream ended.");
                    return;
                }

                bytesRead += read;
            }

            _ntsc.LockRaw();
            Array.Copy(frameBuf, _ntsc.RawFrameBuffer, frameSize);
            _ntsc.UnlockRaw();

            _ntsc.LockFrame();
            _ntsc.GenerateFullFrame();
            _ntsc.UnlockFrame();
        }
    }

    public void Dispose()
    {
        _running = false;

        if (!_ffmpeg.HasExited)
            _ffmpeg.Kill(entireProcessTree: true);

        _ffmpeg.Dispose();
    }
}
