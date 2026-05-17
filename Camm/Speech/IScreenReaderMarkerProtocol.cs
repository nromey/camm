namespace Camm.Speech;

// Per-mod protocol for identifying speech-bound log lines and
// extracting their options. CAMM's log-tail watcher reads every line
// the game writes; the marker protocol decides which lines are for
// the screen reader (vs ignored as debug-only).
//
// Civ VI Access uses prefix "#SCREENREADER" with bracket-delimited
// options ([NOINTERRUPT,FOO,BAR]); a different game's accessibility
// mod would use a different convention. Consumer supplies an
// implementation via CammModManifest.MarkerProtocol.
public interface IScreenReaderMarkerProtocol
{
    // Heading characters the consumer mod-side prepends to speech
    // lines. CAMM doesn't use this string directly (it's already
    // encoded into ContainsMarker / ParseOptions semantics); exposed
    // for diagnostic logging.
    string MarkerPrefix { get; }

    // Does this raw log line contain a speech marker? Performance-
    // critical: called for every line in the tailed log file.
    bool ContainsMarker(string line);

    // Parse any options embedded in the marker (e.g. NOINTERRUPT).
    // Returns default SpeechOptions for lines with no options or
    // unrecognized values.
    SpeechOptions ParseOptions(string line);
}
