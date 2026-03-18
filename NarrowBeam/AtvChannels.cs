using System.Collections.Generic;

namespace NarrowBeam;

internal record AtvChannel(string Name, decimal FrequencyMhz);

internal static class AtvChannels
{
    public static IReadOnlyList<AtvChannel> All { get; } = new List<AtvChannel>
    {
        // 70cm Band (420-450 MHz) - Corresponding to CATV channels
        new("70cm - Cable 57 (421.25)", 421.25M),
        new("70cm - Cable 58 (427.25)", 427.25M), // Most common simplex
        new("70cm - Cable 59 (433.25)", 433.25M), // Avoid 432.1 SSB calling
        new("70cm - Cable 60 (439.25)", 439.25M), // Common repeater input

        // 33cm Band (902-928 MHz)
        new("33cm - 910.25", 910.25M),
        new("33cm - 923.25", 923.25M),

        // 23cm Band (1240-1300 MHz)
        new("23cm - 1241.25", 1241.25M),
        new("23cm - 1253.25", 1253.25M),
        new("23cm - 1265.25", 1265.25M),
        new("23cm - 1277.25", 1277.25M),
        new("23cm - 1289.25", 1289.25M),
    };
}
