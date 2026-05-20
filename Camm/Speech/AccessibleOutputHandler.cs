using DavyKager;

namespace Camm.Speech;

// Routes per-line log output to Tolk, with markup sanitization and
// interrupt-policy management. Per-mod knowledge (which lines to
// pick up, how to clean their markup) comes from the manifest's
// IScreenReaderMarkerProtocol + IMessageSanitizer.
//
// Interrupt policy: a line marked NOINTERRUPT pauses the rolling
// interrupt window. Subsequent lines within nonInterruptTime stick
// to non-interrupt mode so that a multi-part announcement isn't
// chopped off by the next routine line. Window expires after 3
// seconds of speech idle.
//
// Identical-text dedupe: an earlier v0.5.4/v0.5.5 attempt held the
// latest pending interrupt-mode text for a window and replayed it
// after 150ms. That broke normal flow — pressing Down arrow then
// Alt+V quickly caused the verbosity toggle's "Verbose off" to be
// stomped by the next arrow announce in the pending slot, so the
// user heard the second arrow but not the toggle confirmation.
//
// What works correctly across all observed cases: let Tolk's natural
// last-write-wins behavior handle rapid different-text interrupts
// (Tolk speaks partial-first + full-second, user hears the final
// state which matches reality). The only thing worth filtering is
// IDENTICAL text arriving twice within a short window — that's
// observable as accidental re-announce on the same item, and we
// drop the duplicate.
//
// This is intentionally simple: no timer, no pending slot, no
// deferral. Just "if the same text was spoken N ms ago, drop the
// duplicate." Different text always reaches Tolk immediately.
public sealed class AccessibleOutputHandler
{
    private DateTime _lastNonInterruptableMessage = DateTime.MinValue;
    private static readonly TimeSpan NonInterruptTime = TimeSpan.FromSeconds(3);

    private readonly object _dedupeLock = new();
    private string? _lastSpokenText;
    private DateTime _lastSpokenAt = DateTime.MinValue;
    private static readonly TimeSpan IdenticalDedupeWindow = TimeSpan.FromMilliseconds(250);

    public AccessibleOutputHandler()
    {
        Tolk.TrySAPI(true);
        Tolk.Load();
    }

    public void Speak(string text, bool interrupt = true)
    {
        SpeakInternal(text, interrupt);
    }

    // Single chokepoint for Tolk.Output calls so the dedupe filter
    // applies uniformly to every speech path (Speak() direct callers
    // AND OutputMessage's per-line dispatch).
    private void SpeakInternal(string text, bool interrupt)
    {
        if (interrupt)
        {
            lock (_dedupeLock)
            {
                var now = DateTime.UtcNow;
                if (text == _lastSpokenText
                    && now - _lastSpokenAt < IdenticalDedupeWindow)
                {
                    // Same line within the window — drop the duplicate.
                    return;
                }
                _lastSpokenText = text;
                _lastSpokenAt = now;
            }
        }
        Tolk.Output(text, interrupt);
    }

    public void OutputMessage(string message)
    {
        var protocol = CammHost.Manifest.MarkerProtocol;
        var sanitizer = CammHost.Manifest.Sanitizer;
        // Installer-only manifests leave these null. LogTailSpeaker
        // won't be started in that case, but a guard here means a
        // misconfigured manifest fails loud-and-early instead of
        // throwing deep inside the speech pipeline.
        if (protocol is null || sanitizer is null) return;

        var lines = message.Split('\n');
        foreach (var line in lines)
        {
            if (!protocol.ContainsMarker(line)) continue;

            var options = protocol.ParseOptions(line);
            bool interrupt = !options.NoInterrupt;

            if (!interrupt)
            {
                _lastNonInterruptableMessage = DateTime.UtcNow;
            }
            else
            {
                interrupt = DateTime.UtcNow >= _lastNonInterruptableMessage.Add(NonInterruptTime);
            }

            var sanitized = sanitizer.Sanitize(line);
            Logger.Info($"OutputMessage forwarding to Tolk (interrupt={interrupt}): '{sanitized}'");
            SpeakInternal(sanitized, interrupt);
        }
    }
}
