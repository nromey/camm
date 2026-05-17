using static System.Console;

namespace Camm.Speech;

// Console-side output companion to AccessibleOutputHandler. Some
// status lines (log-watcher diagnostics, "Waiting for log file...")
// don't go through Tolk — they're for the launcher's console log
// only, where a sighted dev might be watching.
public sealed class TextOutputHandler
{
    public void OutputLine(string message) => WriteLine(message);
    public void OutputErrorLine(string message) => Error.WriteLine(message);
}
