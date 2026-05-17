using System.Reflection;

namespace Camm;

// Minimal three-part semver: Major.Minor.Patch with optional leading "v"
// and optional "-prerelease" tail (which we ignore for ordering — channel
// filtering decides whether prereleases count, not version-string suffix
// games). Suits the pre-1.0 release cadence of CAMM-built mods; full
// semver-2.0 ordering can land later without changing callers.
public readonly record struct SemVer(int Major, int Minor, int Patch) : IComparable<SemVer>
{
    public static bool TryParse(string? s, out SemVer version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(s)) return false;

        var trimmed = s.Trim();
        if (trimmed.StartsWith('v') || trimmed.StartsWith('V'))
        {
            trimmed = trimmed.Substring(1);
        }

        // Drop any -prerelease / +build suffix.
        var dash = trimmed.IndexOfAny(new[] { '-', '+' });
        if (dash > 0) trimmed = trimmed.Substring(0, dash);

        var parts = trimmed.Split('.');
        if (parts.Length < 2 || parts.Length > 3) return false;

        if (!int.TryParse(parts[0], out var major)) return false;
        if (!int.TryParse(parts[1], out var minor)) return false;
        int patch = 0;
        if (parts.Length == 3 && !int.TryParse(parts[2], out patch)) return false;

        version = new SemVer(major, minor, patch);
        return true;
    }

    public int CompareTo(SemVer other)
    {
        var c = Major.CompareTo(other.Major); if (c != 0) return c;
        c = Minor.CompareTo(other.Minor); if (c != 0) return c;
        return Patch.CompareTo(other.Patch);
    }

    public override string ToString() => $"{Major}.{Minor}.{Patch}";

    // Read the consuming exe's assembly version. Library code MUST read
    // Assembly.GetEntryAssembly() (the launcher process's main exe),
    // NOT typeof(SemVer).Assembly — that would return Camm.dll's version,
    // which is the framework version, not the mod's version. AOT-safe:
    // assembly metadata is preserved by the AOT compiler.
    public static SemVer Current()
    {
        var asm = Assembly.GetEntryAssembly();
        var v = asm?.GetName().Version;
        if (v is null) return new SemVer(0, 0, 0);
        return new SemVer(v.Major, v.Minor, v.Build < 0 ? 0 : v.Build);
    }
}
