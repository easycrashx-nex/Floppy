#nullable enable
using System;
using System.Collections.Generic;

namespace Floppy.Dungeons2;

internal sealed record SupportedBuild(string SteamBuild, string Hash, ulong Objects, ulong Names);

internal static class SupportedBuilds
{
    internal static readonly IReadOnlyList<SupportedBuild> All = new SupportedBuild[]
    {
        new("25041023", "7C83AFBF0AD34A40B853CDB25A22FFFB605D08E2A1E2D431974D7C7C1EE0BA54", 0xBEA8BF0, 0xBDC5040),
        new("25647713", "231147BD0C655A4AE73F90873675D42917F2BFB3A9EE164FC64F217D6D6BD4EF", 0xBF35A70, 0xBE51EC0)
    };

    internal static SupportedBuild? Find(string hash)
    {
        foreach (var build in All)
            if (string.Equals(hash, build.Hash, StringComparison.OrdinalIgnoreCase)) return build;
        return null;
    }
}
