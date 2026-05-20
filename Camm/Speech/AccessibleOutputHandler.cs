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
// Rapid-interrupt coalescing: Tolk's Output(text, interrupt=true)
// is last-write-wins — a second interrupt call within tens of
// milliseconds of the first silences whatever was just queued
// before any audible part of it plays. This was observed as
// "sticky Alt+V" — toggling verbosity twice in quick succession
// produced silence. The coalesce window holds the most recent
// interrupt-mode text for MinInterruptInterval after firing one,
// and plays whichever message was the latest pending when the
// window expires. Single utterances pass through immediately;
// rapid bursts collapse to first-played + last-pending.
public sealed class AccessibleOutputHandler
{
    private DateTime _lastNonInterruptableMessage = DateTime.MinValue;
    private static readonly TimeSpan NonInterruptTime = TimeSpan.FromSeconds(3);

    private readonly object _coalesceLock = new();
    private DateTime _interruptCooldownUntil = DateTime.MinValue;
    private string? _pendingInterruptText;
    // Fully-qualified: Camm.csproj has UseWindowsForms=true (for the
    // install wizard), so plain `Timer` is ambiguous against
    // System.Windows.Forms.Timer. We want the threading variant.
    private readonly System.Threading.Timer _coalesceTimer;
    private static readonly TimeSpan MinInterruptInterval = TimeSpan.FromMilliseconds(150);

    public AccessibleOutputHandler()
    {
        Tolk.TrySAPI(true);
        Tolk.Load();
        _coalesceTimer = new System.Threading.Timer(
            CoalesceTimerCallback, null,
            System.Threading.Timeout.Infinite,
            System.Threading.Timeout.Infinite);
    }

    public void Speak(string text, bool interrupt = true)
    {
        SpeakWithCoalesce(text, interrupt);
    }

    // Single chokepoint for Tolk.Output calls so the coalesce window
    // applies uniformly to every speech path (Speak() direct callers
    // AND OutputMessage's per-line dispatch).
    private void SpeakWithCoalesce(string text, bool interrupt)
    {
        if (!interrupt)
        {
            // Non-interrupt speaks queue naturally in Tolk's pipeline
            // and don't trigger the swallow, so they bypass coalescing.
            Tolk.Output(text, false);
            return;
        }
        lock (_coalesceLock)
        {
            var now = DateTime.UtcNow;
            if (now >= _interruptCooldownUntil)
            {
                // Cooldown expired (or never started). Fire immediately.
                Tolk.Output(text, true);
                _interruptCooldownUntil = now + MinInterruptInterval;
                _pendingInterruptText = null;
            }
            else
            {
                // Within cooldown — defer. The latest text wins; if
                // multiple interrupts pile up, only the most recent
                // plays when the window expires.
                _pendingInterruptText = text;
                var remainingMs = (int)Math.Max(1, (_interruptCooldownUntil - now).TotalMilliseconds);
                _coalesceTimer.Change(remainingMs, System.Threading.Timeout.Infinite);
            }
        }
    }

    private void CoalesceTimerCallback(object? state)
    {
        string? toSpeak;
        lock (_coalesceLock)
        {
            toSpeak = _pendingInterruptText;
            _pendingInterruptText = null;
            if (toSpeak != null)
            {
                _interruptCooldownUntil = DateTime.UtcNow + MinInterruptInterval;
            }
        }
        if (toSpeak != null)
        {
            Tolk.Output(toSpeak, true);
        }
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
            SpeakWithCoalesce(sanitized, interrupt);
        }
    }
}
