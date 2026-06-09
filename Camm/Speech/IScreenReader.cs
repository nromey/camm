namespace Camm.Speech;

// Backend-neutral screen-reader output surface. CAMM routes all speech
// through this interface so the backend (Tolk or Prism) can be chosen
// without the speech pipeline above it knowing which is in use.
//
// The dedupe + sticky-NOINTERRUPT policy lives in AccessibleOutputHandler,
// ABOVE this interface, so it applies uniformly to every backend.
//
// Implementations: TolkScreenReader (default, DavyKager.Tolk binding),
// PrismScreenReader (ethindp/prism via our P/Invoke layer). Selection +
// fallback happens in ScreenReaderFactory.
public interface IScreenReader : IDisposable
{
    // Bring the backend up. Returns true if a usable speech channel is
    // available; false means the caller should fall back to another
    // backend (ScreenReaderFactory falls Prism -> Tolk).
    bool Initialize();

    // Speak text. interrupt=true cuts off current speech (last-write-wins
    // on both Tolk and Prism); false lets the backend queue/append.
    void Speak(string text, bool interrupt);

    // Stop current speech immediately. Best-effort; no-op if the backend
    // can't interrupt.
    void Stop();

    // True when speech is currently in progress. Best-effort — backends
    // that can't report this return false.
    bool IsSpeaking { get; }

    // Stable backend identity for diagnostics ("Tolk" / "Prism").
    string BackendName { get; }

    // Human-readable name of the detected screen reader / TTS target
    // (e.g. "NVDA", "SAPI"), for diagnostics. Null when none detected.
    string? DetectedReader { get; }
}
