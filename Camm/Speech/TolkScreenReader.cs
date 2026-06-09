using DavyKager;

namespace Camm.Speech;

// IScreenReader over the vendored DavyKager.Tolk binding. This is the
// default backend and the cross-mod convention. The native Tolk.dll +
// sidecars are extracted next to the exe (or to a temp dir) by
// TolkBootstrap before any of these calls run.
//
// Initialize() never throws — Tolk.Load() is safe even with no screen
// reader present (it falls back to SAPI or simply produces no audible
// output). It returns Tolk.IsLoaded() purely as a signal for the factory's
// diagnostics; Tolk is always usable as the last-resort backend.
public sealed class TolkScreenReader : IScreenReader
{
    public string BackendName => "Tolk";

    public string? DetectedReader
    {
        get { try { return Tolk.DetectScreenReader(); } catch { return null; } }
    }

    public bool IsSpeaking
    {
        get { try { return Tolk.IsSpeaking(); } catch { return false; } }
    }

    public bool Initialize()
    {
        Tolk.TrySAPI(true);
        Tolk.Load();
        return Tolk.IsLoaded();
    }

    public void Speak(string text, bool interrupt) => Tolk.Output(text, interrupt);

    public void Stop()
    {
        try { Tolk.Silence(); } catch { /* best effort */ }
    }

    public void Dispose()
    {
        try { Tolk.Unload(); } catch { /* best effort */ }
    }
}
