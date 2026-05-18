using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Camm.Localization;

// Locale catalog + lookup. Translatable text in CAMM lives in
// `lang/<culture>.json` files as flat dictionaries
// ("Wizard.Welcome.Heading": "Install __DISPLAY_NAME__").
//
// CAMM ships `lang/en.json` embedded in `Camm.dll` as the always-
// available baseline. At startup, Strings looks for a loose
// `lang/<culture>.json` next to the consuming exe and overlays any
// keys it finds onto the baseline (so a translator can drop a new
// locale next to the .exe without rebuilding).
//
// Fallback chain on lookup:
//   1. Loose <culture>.json (CurrentUICulture.Name, e.g. "de-DE.json")
//   2. Loose <language>.json (parent culture, e.g. "de.json")
//   3. Embedded en.json
//   4. Return the key itself + log a warning. Missing-string bugs
//      surface to the user but never crash the launcher.
//
// Substitution tokens in values get replaced with manifest fields at
// Get() time:
//   __DISPLAY_NAME__          → CammHost.Manifest.DisplayName
//   __TARGET_GAME__           → CammHost.Manifest.TargetGameDisplayName
//   __TARGET_LAUNCHER__       → CammHost.Manifest.TargetGameLauncherName
//   __PUBLISHER__             → CammHost.Manifest.Publisher
//   __INSTALL_DIR__           → Installer.DefaultInstallDir
//   __LOCAL_APP_DATA_FOLDER__ → CammHost.Manifest.LocalAppDataFolderName
//   __LAUNCHER_EXE__          → CammHost.Manifest.LauncherExeName
//   __VERSION__               → SemVer.Current() (the consuming exe's
//                               assembly version)
//
// AOT-clean via source-generated JsonSerializerContext below. No
// reflection on user types.
public static class Strings
{
    private static readonly object _lock = new();
    private static Dictionary<string, string>? _embedded;
    private static Dictionary<string, string>? _loose;
    private static bool _loaded;

    public static string Get(string key)
    {
        EnsureLoaded();

        // Mode-aware variant lookup. When the manifest is in
        // installer-only mode, prefer "<key>.InstallerOnly" over the
        // base key so adopters get installer-appropriate copy (no
        // mention of IFEO redirect, Steam launch interception, etc.).
        // The variant is optional — if absent in the catalog we fall
        // through to the base key, so adopters with shared copy across
        // both modes don't need to duplicate every key.
        bool installerOnly = false;
        try { installerOnly = CammHost.Manifest.IsInstallerOnly; }
        catch { /* not initialized — unit tests, etc. */ }

        if (installerOnly)
        {
            var variantKey = key + ".InstallerOnly";
            if (_loose is not null && _loose.TryGetValue(variantKey, out var looseVar))
            {
                return Substitute(looseVar);
            }
            if (_embedded is not null && _embedded.TryGetValue(variantKey, out var embVar))
            {
                return Substitute(embVar);
            }
        }

        if (_loose is not null && _loose.TryGetValue(key, out var loose))
        {
            return Substitute(loose);
        }
        if (_embedded is not null && _embedded.TryGetValue(key, out var emb))
        {
            return Substitute(emb);
        }
        Logger.Warn($"Missing locale key: {key}");
        return key;
    }

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            _embedded = LoadEmbedded();
            _loose = LoadLoose();
            _loaded = true;
        }
    }

    private static Dictionary<string, string>? LoadEmbedded()
    {
        var asm = typeof(Strings).Assembly;
        using var stream = asm.GetManifestResourceStream("lang/en.json")
            ?? asm.GetManifestResourceStream("lang\\en.json");
        if (stream is null)
        {
            Logger.Warn("Embedded lang/en.json not found in Camm.dll");
            return null;
        }
        try
        {
            return JsonSerializer.Deserialize(stream, LocaleJsonContext.Default.DictionaryStringString);
        }
        catch (Exception ex)
        {
            Logger.Exception("Failed to parse embedded lang/en.json", ex);
            return null;
        }
    }

    private static Dictionary<string, string>? LoadLoose()
    {
        var langDir = Path.Combine(AppContext.BaseDirectory, "lang");
        if (!Directory.Exists(langDir)) return null;

        var culture = CultureInfo.CurrentUICulture;
        // Try <culture>.json (e.g. de-DE.json), then <language>.json
        // (e.g. de.json), then nothing — we want overrides to be
        // intentional, not accidentally picked up from a translation
        // for the wrong language family.
        foreach (var candidate in new[] { culture.Name, culture.TwoLetterISOLanguageName })
        {
            var path = Path.Combine(langDir, candidate + ".json");
            if (File.Exists(path))
            {
                try
                {
                    using var fs = File.OpenRead(path);
                    var dict = JsonSerializer.Deserialize(fs, LocaleJsonContext.Default.DictionaryStringString);
                    if (dict is not null)
                    {
                        Logger.Info($"Loaded locale override from {path} ({dict.Count} keys)");
                        return dict;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Exception($"Failed to load locale override {path}", ex);
                }
            }
        }
        return null;
    }

    private static string Substitute(string template)
    {
        // Read manifest lazily — Strings.Get could be called before
        // CammHost.Initialize in unit-test scenarios. Skip
        // substitution when no manifest is set.
        CammModManifest? m = null;
        try { m = CammHost.Manifest; } catch { /* not initialized */ }
        if (m is null) return template;

        var installDir = "";
        try { installDir = Installer.DefaultInstallDir; }
        catch { /* OperatingSystem may not be windows in unit tests */ }

        return template
            .Replace("__DISPLAY_NAME__", m.DisplayName)
            .Replace("__TARGET_GAME__", m.TargetGameDisplayName)
            .Replace("__TARGET_LAUNCHER__", m.TargetGameLauncherName)
            .Replace("__PUBLISHER__", m.Publisher)
            .Replace("__INSTALL_DIR__", installDir)
            .Replace("__LOCAL_APP_DATA_FOLDER__", m.LocalAppDataFolderName)
            .Replace("__LAUNCHER_EXE__", m.LauncherExeName)
            .Replace("__VERSION__", SemVer.Current().ToString());
    }
}

// Source-gen JSON context for AOT-clean Dictionary<string,string>
// deserialization. CAMM uses System.Text.Json's source generator
// pattern throughout (see also GitHubReleasesClient's
// ReleasesJsonContext) so AOT publishing doesn't trip over
// reflection-based serialization.
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class LocaleJsonContext : JsonSerializerContext { }
