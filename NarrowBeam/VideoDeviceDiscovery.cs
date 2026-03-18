using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace NarrowBeam;

internal static class VideoDeviceDiscovery
{
    public static IReadOnlyList<string> GetVideoDevices()
    {
        var devices = new List<string>();
        string ffmpegPath = ResolveFfmpegExecutable();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = "-hide_banner -list_devices true -f dshow -i dummy",
                UseShellExecute = false,
                RedirectStandardError = true,
                CreateNoWindow = true,
            },
        };

        process.Start();
        string stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        bool inVideoSection = false;
        foreach (string rawLine in stderr.Split('\n'))
        {
            string line = rawLine.Trim();

            if (line.Contains("Alternative name", StringComparison.OrdinalIgnoreCase))
                continue;

            // ffmpeg >= 6.0: "[dshow @ 0x...] "Name" (video)" / "(audio)"
            if (line.Contains("(video)", StringComparison.OrdinalIgnoreCase))
            {
                Match m = Regex.Match(line, "\"([^\"]+)\"");
                if (m.Success)
                    devices.Add(m.Groups[1].Value);
                continue;
            }

            if (line.Contains("(audio)", StringComparison.OrdinalIgnoreCase))
                continue;

            // ffmpeg < 6.0: section-header style output
            if (line.Contains("DirectShow video devices", StringComparison.OrdinalIgnoreCase))
            {
                inVideoSection = true;
                continue;
            }

            if (line.Contains("DirectShow audio devices", StringComparison.OrdinalIgnoreCase))
                break;

            if (!inVideoSection)
                continue;

            Match match = Regex.Match(line, "\"([^\"]+)\"");
            if (match.Success)
                devices.Add(match.Groups[1].Value);
        }

        return devices;
    }

    public static string GetDefaultVideoDevice()
    {
        IReadOnlyList<string> devices = GetVideoDevices();
        if (devices.Count == 0)
            throw new InvalidOperationException("No DirectShow video devices were found.");

        return devices[0];
    }

    public static string ResolveFfmpegExecutable()
    {
        string localPath = Path.Combine(AppContext.BaseDirectory, "ffmpeg.exe");
        if (File.Exists(localPath))
            return localPath;

        string repoRootPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ffmpeg.exe");
        string fullRepoRootPath = Path.GetFullPath(repoRootPath);
        if (File.Exists(fullRepoRootPath))
            return fullRepoRootPath;

        return "ffmpeg";
    }
}
