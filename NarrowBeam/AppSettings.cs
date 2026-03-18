using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace NarrowBeam;

internal sealed class AppSettings
{
    private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "narrowbeam.ini");

    public string? LastDevice { get; set; }
    public string? LastPreset { get; set; }
    public decimal FrequencyMhz { get; set; } = 427.250M;
    public double BandwidthMhz { get; set; } = 2.0;
    public int GainDb { get; set; } = 30;
    public bool AmpEnabled { get; set; }
    public string? Callsign { get; set; }
    public bool UseTestPattern { get; set; }

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        if (!File.Exists(ConfigPath))
            return settings;

        foreach (string rawLine in File.ReadAllLines(ConfigPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            int separatorIndex = line.IndexOf('=');
            if (separatorIndex <= 0)
                continue;

            string key = line[..separatorIndex].Trim();
            string value = line[(separatorIndex + 1)..].Trim();

            switch (key)
            {
                case "last_device":
                    settings.LastDevice = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "last_preset":
                    settings.LastPreset = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "frequency_mhz":
                    if (decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal frequency))
                        settings.FrequencyMhz = frequency;
                    break;
                case "bandwidth_mhz":
                    if (double.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out double bandwidth))
                        settings.BandwidthMhz = bandwidth;
                    break;
                case "gain_db":
                    if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int gain))
                        settings.GainDb = gain;
                    break;
                case "amp_enabled":
                    if (bool.TryParse(value, out bool ampEnabled))
                        settings.AmpEnabled = ampEnabled;
                    break;
                case "callsign":
                    settings.Callsign = string.IsNullOrWhiteSpace(value) ? null : value;
                    break;
                case "use_test_pattern":
                    if (bool.TryParse(value, out bool useTestPattern))
                        settings.UseTestPattern = useTestPattern;
                    break;
            }
        }

        return settings;
    }

    public void Save()
    {
        var lines = new List<string>
        {
            $"last_device={LastDevice ?? string.Empty}",
            $"last_preset={LastPreset ?? string.Empty}",
            $"frequency_mhz={FrequencyMhz.ToString(CultureInfo.InvariantCulture)}",
            $"bandwidth_mhz={BandwidthMhz.ToString(CultureInfo.InvariantCulture)}",
            $"gain_db={GainDb.ToString(CultureInfo.InvariantCulture)}",
            $"amp_enabled={AmpEnabled.ToString().ToLowerInvariant()}",
            $"callsign={Callsign ?? string.Empty}",
            $"use_test_pattern={UseTestPattern.ToString().ToLowerInvariant()}",
        };

        File.WriteAllLines(ConfigPath, lines);
    }
}
