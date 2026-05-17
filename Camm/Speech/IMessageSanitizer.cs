namespace Camm.Speech;

// Per-mod text sanitizer for log-tail messages headed to Tolk. Game
// mod-side text typically contains in-engine markup ([ICON_FOOD],
// [COLOR:Red], [NEWLINE]) that should be stripped or transformed
// before speech. Each game has its own markup vocabulary, so the
// CAMM core can't know the rules — consumer supplies an
// implementation via CammModManifest.Sanitizer.
//
// Implementations should be pure functions (no side effects, no
// mutable state) and tolerate arbitrary input including empty
// strings, strings with no markup, and malformed markup.
public interface IMessageSanitizer
{
    string Sanitize(string raw);
}
