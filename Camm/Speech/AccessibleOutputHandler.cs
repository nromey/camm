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
public sealed class AccessibleOutputHandler
{
    private DateTime _lastNonInterruptableMessage = DateTime.MinValue;
    private static readonly TimeSpan NonInterruptTime = TimeSpan.FromSeconds(3);

    public AccessibleOutputHandler()
    {
        Tolk.TrySAPI(true);
        Tolk.Load();
    }

    public void Speak(string text, bool interrupt = true)
    {
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
            var ok = Tolk.Output(sanitized, interrupt);
            Logger.Info($"  Tolk.Output returned {ok}");
        }
    }
}
