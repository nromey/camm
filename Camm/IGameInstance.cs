namespace Camm;

// Per-game hooks consumed by CammHost.RunAsync's main launch flow.
// CAMM owns the orchestration (apply pending update, route on args,
// spawn the game, wait for it, tail its log); the consumer supplies
// the game-specific paths and the user-facing speech strings here.
//
// A consuming mod implements this interface and supplies an instance
// via CammModManifest.GameInstance.
public interface IGameInstance
{
    // Absolute path to the game's main exe, when CAMM is launching the
    // game itself (not via transparent invocation). Return null if the
    // game can't be located — CAMM will surface an error to the user
    // rather than spawn a non-existent process. For transparent
    // invocations (IFEO redirect), CAMM uses the path Windows passed
    // in args[0] and never calls this method.
    string? FindGameExe();

    // Absolute path to the game's log file CAMM should tail for
    // speech-bound lines. CAMM polls this file for new bytes, splits
    // on newlines, and routes each line through CammModManifest
    // .MarkerProtocol + .Sanitizer + Tolk.
    string GetLogFilePath();

    // The line CAMM speaks just before launching the game. Mods that
    // distinguish first-launch (e.g. Civ VI's EULA-detect) versus
    // subsequent-launch greeting return different text from the same
    // method; CAMM doesn't know or care about the distinction.
    string GetLaunchAnnouncement();

    // The line CAMM speaks after the game process exits. Used for
    // "Sid Meier's Civilization VI closed." and equivalents.
    string GetClosedAnnouncement();
}
