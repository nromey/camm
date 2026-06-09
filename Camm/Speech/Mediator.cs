namespace Camm.Speech;

// Trivial composer that LogTailSpeaker writes through, fanning each
// inbound chunk of game-log text out to (a) the AccessibleOutputHandler
// for screen-reader routing and (b) the TextOutputHandler for console
// diagnostics. The split exists because some lines are user-facing
// speech (game messages) while others are launcher-status text
// (waiting for log file, file truncation events) that shouldn't
// route through Tolk.
public sealed class Mediator
{
    private readonly AccessibleOutputHandler _accessibleOutput;
    private readonly TextOutputHandler _textOutput;

    // Optional secondary sink for raw game-log content. Receives the
    // SAME multi-line chunk that drives speech, independent of the
    // IScreenReaderMarkerProtocol routing. Civ VI Access registers a
    // WebView2 report bridge here — the mod emits #SHOWREPORT marker
    // lines that the observer accumulates and renders in a window —
    // without CAMM needing to know anything about reports. Null = no
    // secondary channel (speech only).
    private readonly Action<string>? _lineObserver;

    public Mediator(AccessibleOutputHandler accessibleOutput, TextOutputHandler textOutput,
                    Action<string>? lineObserver = null)
    {
        _accessibleOutput = accessibleOutput;
        _textOutput = textOutput;
        _lineObserver = lineObserver;
    }

    public void Output(string message)
    {
        _accessibleOutput.OutputMessage(message);
        // A misbehaving observer must never break the speech path —
        // speech is the primary, load-bearing channel.
        if (_lineObserver is not null)
        {
            try { _lineObserver(message); }
            catch (Exception ex) { Logger.Exception("Mediator line observer threw", ex); }
        }
    }

    public void OutputText(string message) => _textOutput.OutputLine(message);
    public void OutputTextError(string message) => _textOutput.OutputErrorLine(message);
}
