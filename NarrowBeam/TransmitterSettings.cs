namespace NarrowBeam;

internal sealed class TransmitterSettings
{
    public double FrequencyMhz { get; init; } = 427.25;
    public double BandwidthMhz { get; init; } = 2.0;
    public int GainDb { get; init; } = 30;
    public bool AmpEnabled { get; init; }
    public string? Callsign { get; init; }
    public bool UseTestPattern { get; init; }
    public string? DeviceName { get; init; }
}
