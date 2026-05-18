using System.Text.Json.Serialization;

namespace Camm;

// External-mod dependency declaration. Adopters list these on
// CammModManifest.Dependencies to require Harmony / BepInEx /
// MelonLoader / IPA / similar bootstrap layers their mod runs
// inside. CAMM checks each dependency at install time (sentinel
// check at InstallPath/SentinelFileName); if missing, CAMM prompts
// the user, fetches the latest release from GitHub, and extracts
// it to InstallPath.
//
// v0.4.0 fetch source is GitHub Releases only. Non-GitHub sources
// (Steam Workshop, direct URL, bundled-with-adopter zip) require
// CammModManifest.PreInstallHook + adopter-written logic.
//
// Dependencies are install-time only — CAMM does not auto-update
// them after first install. Dependencies also survive adopter-mod
// uninstall (they're shared resources another mod may need).
public sealed record ModDependency(
    // Stable identifier. Persisted as the manifest filename
    // (`%LocalAppData%\<adopter>\dep-<Name>.json`) and used as the
    // dictionary key in log messages. Use the dependency's
    // canonical ID — `brrainz.harmony`, `BepInEx`, etc.
    string Name,

    // User-facing name. Shown in the install consent prompt,
    // "Downloading ..." status, error messages.
    string DisplayName,

    // GitHub releases source.
    string GitHubReleasesOwner,
    string GitHubReleasesRepo,

    // Asset filename pattern. `{0}` substitutes the release's tag
    // name with any leading `v` stripped, matching CAMM's own
    // LauncherAssetNamePattern convention. Adopter writes
    // "Harmony-{0}.zip" or "BepInEx_x64_{0}.zip" etc.
    string AssetNamePattern,

    // Directory the dependency extracts into. Called at runtime —
    // safe to use Environment.GetFolderPath. Must return an
    // absolute path.
    Func<string> InstallPath,

    // Relative path inside InstallPath that proves the dependency
    // is installed. Sub-paths like "About/About.xml" are honored.
    // CAMM skips the fetch entirely if this file exists.
    string SentinelFileName)
{
    // When the zip wraps its content in a top-level directory
    // (the common GitHub-release shape — "HarmonyRimWorld-2.3.3/..."),
    // strip the prefix during extraction so the dependency lands
    // directly under InstallPath.
    //
    // Values:
    //   - Literal string (e.g. "HarmonyRimWorld-2.3.3") → strip
    //     that exact prefix from every entry path.
    //   - "*" → strip whatever the first directory of the first
    //     non-directory entry happens to be. Handles the version-
    //     name-in-folder case where the prefix changes per release.
    //   - null (default) → extract as-is, preserving any top-level
    //     zip folders.
    //
    // Bare-DLL dependencies (AssetNamePattern ending in `.dll`)
    // ignore this — there's no archive to strip.
    public string? ZipRootStripPrefix { get; init; }
}

// Persisted to %LocalAppData%\<adopter>\dep-<Name>.json so CAMM
// remembers what version of which dependency it installed.
// v0.4.0 doesn't use this for uninstall (dependencies survive
// adopter-uninstall by design), but the data is available for
// future versions (managed dependency updates, --dependency-status
// diagnostics).
public sealed class DependencyInstallManifest
{
    [JsonPropertyName("dependencyName")]
    public string DependencyName { get; set; } = "";

    [JsonPropertyName("installedVersion")]
    public string InstalledVersion { get; set; } = "";

    [JsonPropertyName("installPath")]
    public string InstallPath { get; set; } = "";

    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = new();

    [JsonPropertyName("sourceAssetUrl")]
    public string SourceAssetUrl { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("cammVersion")]
    public string CammVersion { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DependencyInstallManifest))]
internal partial class DependencyInstallManifestJsonContext : JsonSerializerContext { }
