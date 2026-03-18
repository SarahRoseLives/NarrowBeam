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

    public WebcamCapture(NtscSignal ntsc, string? deviceName, string? callsign, Action<string>? log = null)
    {
        _ntsc = ntsc;
        _log = log;

        string resolvedDevice = string.IsNullOrWhiteSpace(deviceName)
            ? VideoDeviceDiscovery.GetDefaultVideoDevice()
            : deviceName;

        string vfArg = $"setrange=full,scale={NtscSignal.FrameWidth}:{NtscSignal.FrameHeight},fps=30000/1001";
        if (!string.IsNullOrWhiteSpace(callsign))
        {
            // Escape special characters for drawtext
            string escapedCallsign = callsign.Replace(":", "\\:").Replace("'", "\\'");
            // Use Arial font (standard on Windows)
            string fontPath = "C\\:/Windows/Fonts/arial.ttf";
            vfArg += $",drawbox=x=0:y=ih-40:w=iw:h=40:color=black@0.6:t=fill,drawtext=fontfile='{fontPath}':text='{escapedCallsign}':x=10:y=h-35:fontcolor=white:fontsize=32:borderw=2:bordercolor=black";
        }

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
        try
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
                try
                {
                    Array.Copy(frameBuf, _ntsc.RawFrameBuffer, frameSize);
                }
                finally
                {
                    _ntsc.UnlockRaw();
                }

                _ntsc.GenerateFullFrame();
            }
        }
        catch (Exception ex)
        {
            _running = false;
            _log?.Invoke($"Webcam capture failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        _running = false;

        if (!_ffmpeg.HasExited)
            _ffmpeg.Kill(entireProcessTree: true);

        if (_readerThread.IsAlive)
            _readerThread.Join(TimeSpan.FromSeconds(2));

        _ffmpeg.Dispose();
    }
}
