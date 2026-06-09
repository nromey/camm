namespace Camm.Speech;

// Picks and initializes the IScreenReader backend.
//
// Selection precedence (highest first):
//   1. CAMM_SCREEN_READER_BACKEND env var ("tolk" / "prism", case-
//      insensitive) — a dev/testing override that flips the backend at
//      launch with no rebuild. This is how we A/B Tolk vs Prism before a
//      real in-game picker exists.
//   2. The manifest default (CammModManifest.ScreenReaderBackend).
//
// Prism never hard-fails the launcher: if the selected backend is Prism
// but it can't initialize (prism.dll missing, no available backend, init
// error), the factory disposes it and falls back to Tolk. Tolk is always
// usable as the last resort (its Load() is safe even with no reader).
internal static class ScreenReaderFactory
{
    private const string EnvVar = "CAMM_SCREEN_READER_BACKEND";

    public static IScreenReader Create(ScreenReaderBackend manifestDefault)
    {
        var selected = ResolveSelection(manifestDefault);

        if (selected == ScreenReaderBackend.Prism)
        {
            var prism = new PrismScreenReader();
            if (prism.Initialize())
            {
                Logger.Info($"Screen-reader backend: Prism (reader: {prism.DetectedReader ?? "unknown"}).");
                return prism;
            }
            Logger.Warn("Prism backend requested but unavailable; falling back to Tolk.");
            prism.Dispose();
        }

        var tolk = new TolkScreenReader();
        var loaded = tolk.Initialize();
        Logger.Info($"Screen-reader backend: Tolk (reader: {tolk.DetectedReader ?? "none detected"}, loaded={loaded}).");
        return tolk;
    }

    private static ScreenReaderBackend ResolveSelection(ScreenReaderBackend manifestDefault)
    {
        var raw = Environment.GetEnvironmentVariable(EnvVar);
        if (!string.IsNullOrWhiteSpace(raw))
        {
            if (raw.Trim().Equals("prism", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"{EnvVar}=prism overrides manifest default ({manifestDefault}).");
                return ScreenReaderBackend.Prism;
            }
            if (raw.Trim().Equals("tolk", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Info($"{EnvVar}=tolk overrides manifest default ({manifestDefault}).");
                return ScreenReaderBackend.Tolk;
            }
            Logger.Warn($"{EnvVar}='{raw}' not recognized (expected tolk|prism); using manifest default {manifestDefault}.");
        }
        return manifestDefault;
    }
}
