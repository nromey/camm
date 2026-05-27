namespace Camm;

// Per-mod configuration object — the consumer constructs one of these
// at startup and passes it to CammHost.RunAsync (or
// CammHost.Initialize for direct-use scenarios). Every CAMM module
// reads from it via CammHost.Manifest at call time.
//
// CAMM has four operating modes that the manifest's optional fields
// select:
//
//   * **Launcher mode with log-tail** — full chameleon launcher:
//     install wizard + auto-update + IFEO transparent-launch + Tolk
//     speech routing of in-game log-tail messages + lifecycle wait.
//     The consumer fills in every launcher field (GameInstance,
//     IfeoTargetExeNames, GameProcessNames, Sanitizer, MarkerProtocol).
//     Civ VI Access uses this.
//
//   * **Launcher mode without log-tail** — install + update + IFEO
//     redirect + game spawn + lifecycle wait, but no log-tail speech
//     bridge. For mods that handle speech in-process (Civ V Access
//     pattern: a Lua proxy DLL exposes Tolk as a global inside the
//     game's scripting context, so there's no log channel to tail).
//     Set GameInstance + IfeoTargetExeNames + GameProcessNames; leave
//     Sanitizer and MarkerProtocol null.
//
//   * **Installer-only mode** — install wizard + Apps & Features
//     registration. Updates apply when the user re-runs the installer
//     exe (no IFEO redirect). Used by mods whose runtime lives inside
//     the game's process (Harmony DLL, BepInEx plugin, Wwise mod) where
//     there's no launcher relationship and the adopter doesn't want one.
//     Leave every launcher-mode field null.
//
//   * **Installer-only mode with update-on-launch IFEO** (v0.5+) —
//     installer-only mode plus an IFEO redirect on the game's exe
//     that runs CAMM's update check on every game launch. When the
//     user clicks Play in Steam, CAMM intercepts briefly, applies any
//     pending update, then spawns the game and exits. No log-tail, no
//     lifecycle wait — the user experiences the launch as if CAMM
//     weren't there. Opt in by setting IfeoTargetExeNames in
//     installer-only mode (and leaving GameInstance null).
//
// IsInstallerOnly is true when GameInstance is null. LogTailEnabled
// is true when both Sanitizer AND MarkerProtocol are set.
// UpdateOnlyIfeoEnabled is true when installer-only mode is combined
// with a non-empty IfeoTargetExeNames.
public sealed class CammModManifest
{
    // ---------------------------------------------------------------
    //  Always required
    // ---------------------------------------------------------------

    // Folder name for per-user state (launcher.ini, launcher.log,
    // .mod-redeploy-needed marker, per-payload installed-*.json
    // manifests). Lives under %LocalAppData%.
    public required string LocalAppDataFolderName { get; init; }

    // Filename of the installed launcher exe (without path).
    public required string LauncherExeName { get; init; }

    // User-Agent header for HTTP requests to GitHub. GitHub's API
    // requires a non-empty UA; pick something identifying.
    public required string UserAgent { get; init; }

    // Apps & Features registry-entry metadata.
    public required string AppsAndFeaturesKeyName { get; init; }
    public required string DisplayName { get; init; }
    public required string Publisher { get; init; }

    // Mod payloads — one or more deployable artifact groups. Each
    // payload has its own embed-resource prefix, its own dev-mode
    // source-discovery hint, and its own deploy destination. See
    // ModPayload.cs.
    public required IReadOnlyList<ModPayload> ModPayloads { get; init; }

    // Human-readable name of the target game. Used in installer /
    // wizard / uninstaller text ("Launch RimWorld from Steam",
    // "RimWorld's mod folder"). Distinct from DisplayName: DisplayName
    // is the mod's identity ("RimWorld Access"); this is the game's
    // identity ("RimWorld").
    public required string TargetGameDisplayName { get; init; }

    // ---------------------------------------------------------------
    //  Optional with defaults
    // ---------------------------------------------------------------

    // Storefront / launcher the user starts the game from. Defaults to
    // "Steam" because most accessibility-mod targets ship via Steam.
    // Overridable for Epic / GOG / standalone-installer games.
    public string TargetGameLauncherName { get; init; } = "Steam";

    // Project home page. Shown in Apps & Features ("Visit website"
    // link). Empty = no link.
    public string ProjectUrl { get; init; } = "";

    // ---------------------------------------------------------------
    //  Auto-update — null/empty fields disable the check
    // ---------------------------------------------------------------

    // GitHub Releases polling. Both Owner and Repo must be set (and
    // LauncherAssetNamePattern below) for auto-update to fire on
    // launcher startup. Leaving any null/empty skips the check —
    // useful during initial bring-up before the adopter has stood up
    // a GitHub Releases pipeline.
    public string? GitHubReleasesOwner { get; init; }
    public string? GitHubReleasesRepo { get; init; }

    // Asset filename pattern produced by the release pipeline, where
    // {0} is the version string. Used by Updater to find the launcher
    // exe in a GitHub release's assets. Example: "CivViAccess-{0}.exe"
    // produces filenames like CivViAccess-0.3.0.exe. {0} is the bare
    // version with no "v" prefix.
    public string? LauncherAssetNamePattern { get; init; }

    public bool AutoUpdateEnabled =>
        !string.IsNullOrEmpty(GitHubReleasesOwner)
        && !string.IsNullOrEmpty(GitHubReleasesRepo)
        && !string.IsNullOrEmpty(LauncherAssetNamePattern);

    // ---------------------------------------------------------------
    //  Launcher-mode only — leave null for installer-only mods
    //  (Harmony DLL, BepInEx plugin, in-game mod with no separate
    //   launcher process)
    // ---------------------------------------------------------------

    // Target-game exe filenames the IFEO redirect intercepts. Civ VI
    // Access has two (CivilizationVI.exe + CivilizationVI_DX12.exe);
    // most games have one. Null/empty = no IFEO redirect installed.
    public string[]? IfeoTargetExeNames { get; init; }

    // Process names (no .exe suffix) for the foreground-management
    // and lifecycle-watch loops. Null/empty = no game-launch
    // lifecycle handling.
    public string[]? GameProcessNames { get; init; }

    // Per-mod in-engine markup sanitizer for log-tail speech. Optional
    // even in launcher mode — Civ V Access, for example, has a
    // GameInstance (launcher mode) but emits no log-bridged speech
    // because its mod speaks in-process via a Lua proxy DLL. Setting
    // Sanitizer + MarkerProtocol to non-null enables the log-tail
    // bridge; leaving them null skips it within launcher mode.
    public Speech.IMessageSanitizer? Sanitizer { get; init; }

    // Per-mod log-line marker convention. Optional, same logic as
    // Sanitizer above. Both must be set for the log-tail speech
    // bridge to fire.
    public Speech.IScreenReaderMarkerProtocol? MarkerProtocol { get; init; }

    // Per-game hooks for the main launch flow (locate game, log file,
    // launch + closed announcements). Null = installer-only mode:
    // CammHost.RunAsync skips the entire game-launch / log-tail /
    // lifecycle-wait tail after handling args + install + update.
    //
    // GetLogFilePath() is ONLY called when LogTailEnabled returns
    // true (Sanitizer + MarkerProtocol both set). Adopters that don't
    // use the log-tail bridge can return any value from
    // GetLogFilePath; CAMM won't read it.
    public IGameInstance? GameInstance { get; init; }

    // Optional async hook invoked after all payloads have been
    // extracted and IFEO + Apps & Features have been registered, but
    // before "install complete" is announced to the user. Receives a
    // dictionary keyed by payload name with the install manifests
    // CAMM just wrote. Use for game-side config edits CAMM doesn't
    // model: RimWorld's ModsConfig.xml, BepInEx's plugin enable
    // list, ModInfo registration for engines that need it.
    //
    // Throwing from the hook fails the install (the wizard's Done
    // page shows the FailureBody). Idempotent / safe to re-run is the
    // adopter's responsibility — install-over-install will call it
    // again.
    public Func<IReadOnlyDictionary<string, PayloadInstallManifest>, Task>? PostInstallHook { get; init; }

    // ---------------------------------------------------------------
    //  v0.4.0: dependency installation + pre-install hook
    // ---------------------------------------------------------------

    // Optional list of external mods this adopter depends on
    // (Harmony for RimWorld, BepInEx / MelonLoader / IPA / etc.).
    // CAMM checks each dependency's sentinel at install time and,
    // with user consent, fetches the latest release from GitHub and
    // extracts it to the dependency's InstallPath. See
    // Camm/DependencyInstaller.cs for the runtime behavior.
    //
    // Dependencies are install-time only — CAMM does not auto-update
    // them. They also survive adopter-mod uninstall (shared resources
    // another mod may need).
    public IReadOnlyList<ModDependency>? Dependencies { get; init; }

    // Optional async hook invoked BEFORE payload extraction and
    // dependency installation. Symmetric partner to PostInstallHook.
    // Use for arbitrary scripted setup: migrating from a pre-CAMM
    // deployed state (e.g. deploy.ps1 artifacts), fetching a
    // non-GitHub-Releases dependency, transforming a config file
    // before the install lands.
    //
    // Throwing from the hook fails the install (wizard's Done page
    // shows FailureBody). Idempotent / safe to re-run is the
    // adopter's responsibility.
    //
    // CammHost.Manifest is statically available from inside the hook
    // if you need to read manifest fields.
    public Func<Task>? PreInstallHook { get; init; }

    // True when this manifest declares at least one dependency.
    // Convenience for diagnostics and conditional log lines.
    public bool HasDependencies => Dependencies is { Count: > 0 };

    // True when the manifest doesn't drive a game-launch flow. CAMM's
    // RunAsync exits cleanly after install/update/uninstall flow
    // completes rather than locating + launching the target game.
    public bool IsInstallerOnly => GameInstance is null;

    // True when CAMM should start the log-tail speech bridge after
    // launching the game. Requires both seam interfaces. False for
    // installer-only mode AND for launcher-mode adopters whose
    // speech happens in-process (Civ V Access pattern). When false
    // in launcher mode, CAMM still launches the game and waits for
    // its lifecycle, just without the log-tail loop.
    public bool LogTailEnabled => Sanitizer is not null && MarkerProtocol is not null;

    // Opt out of CAMM's "sticky NOINTERRUPT" window — a 3-second
    // post-NOINTERRUPT period during which subsequent interrupt-tier
    // lines also speak as NOINTERRUPT. The window was added before
    // adopter-side speech-priority systems existed; it dampens fast
    // follow-up interrupts so multi-part announces don't get chopped.
    //
    // Set true when the adopter's MarkerProtocol manages interrupt
    // priority itself (CivVIAccess's CivViSpeechShield does this
    // per-kind across Lua VMs). In that case the sticky window is
    // strictly over-dampening — a critical-tier announce arriving
    // 100ms after a status-tier NOINTERRUPT continuation would
    // incorrectly get downgraded to queued. Leaving false preserves
    // pre-0.6 behavior for adopters that depend on it.
    public bool DisableStickyNoInterruptWindow { get; init; } = false;

    // True when installer-only mode is combined with a non-empty
    // IfeoTargetExeNames — the v0.5 "update-only IFEO" opt-in. CAMM
    // still skips the locate-game / launch / log-tail / lifecycle-wait
    // path, but DOES register an IFEO redirect on the target game's exe
    // so the user gets update-on-game-launch behavior without a full
    // launcher process orchestrating the run.
    public bool UpdateOnlyIfeoEnabled =>
        IsInstallerOnly && IfeoTargetExeNames is { Length: > 0 };
}
