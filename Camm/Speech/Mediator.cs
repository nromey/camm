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

    public Mediator(AccessibleOutputHandler accessibleOutput, TextOutputHandler textOutput)
    {
        _accessibleOutput = accessibleOutput;
        _textOutput = textOutput;
    }

    public void Output(string message) => _accessibleOutput.OutputMessage(message);
    public void OutputText(string message) => _textOutput.OutputLine(message);
    public void OutputTextError(string message) => _textOutput.OutputErrorLine(message);
}
