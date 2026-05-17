namespace Camm;

// Per-mod configuration object — the consumer constructs one of these
// at startup, calls CammHost.Initialize(manifest), and every CAMM
// module reads from it via CammHost.Manifest at call time.
//
// Replaces the v0.0.2 CammConfig (settable statics) with a single
// init-only object. `required` init properties give compile-time
// errors if a consumer forgets to set something — much safer than
// the old "set the wrong property and the launcher uses 'Camm' as
// its folder name" failure mode.
//
// Step 5 of CAMM_EXTRACTION_PLAN.md. Step 6+ adds methods to
// CammHost (RunAsync etc.) that take this manifest, so the consumer
// eventually doesn't construct it as a separate step at all —
// CammHost.RunAsync(args, manifest) is the whole entry point.
public sealed class CammModManifest
{
    // Folder name for per-user state (launcher.ini, launcher.log,
    // .mod-redeploy-needed marker). Lives under %LocalAppData%.
    public required string LocalAppDataFolderName { get; init; }

    // Filename of the installed launcher exe (without path). Used by
    // installer paths that reference the exe in Program Files. Civ VI
    // Access uses "CivViAccess.exe"; a Factorio Access adopter would
    // use "FactorioAccess.exe".
    public required string LauncherExeName { get; init; }

    // Asset filename pattern produced by the release pipeline, where
    // {0} is the version string. Used by Updater to find the launcher
    // exe in a GitHub release's assets. Example: "CivViAccess-{0}.exe".
    public required string LauncherAssetNamePattern { get; init; }

    // GitHub Releases polling. Owner is the user/org; Repo is the
    // repo name. Update checks fetch
    // https://api.github.com/repos/<Owner>/<Repo>/releases.
    public required string GitHubReleasesOwner { get; init; }
    public required string GitHubReleasesRepo { get; init; }

    // User-Agent header for HTTP requests to GitHub. GitHub's API
    // requires a non-empty UA; pick something identifying.
    public required string UserAgent { get; init; }

    // Target-game exe filenames the IFEO redirect intercepts. Civ VI
    // Access has two (CivilizationVI.exe + CivilizationVI_DX12.exe);
    // most games have one.
    public required string[] IfeoTargetExeNames { get; init; }

    // Process names (no .exe suffix) for the foreground-management
    // and lifecycle-watch loops. Distinct from IfeoTargetExeNames
    // because Process.GetProcessesByName uses the process name
    // without ".exe" and some games name the process differently
    // from the exe filename.
    public required string[] GameProcessNames { get; init; }

    // Mod payload metadata. Folder name = the directory CAMM looks
    // for in parent dirs during dev-mode source discovery. Sentinel
    // file = optional file CAMM checks for inside that folder to
    // confirm it's genuinely a mod source dir, not a name collision
    // (empty string disables the check).
    public required string ModPayloadFolderName { get; init; }
    public string ModPayloadSentinelFileName { get; init; } = "";

    // Default destination where the mod payload gets deployed at
    // install time (Civ VI's DLC dir, RimWorld's user-data Mods/
    // dir, etc.). A Func<string> rather than a constant because the
    // destination commonly needs Environment.GetFolderPath
    // resolution at call time.
    public required Func<string> ModPayloadDefaultDestination { get; init; }

    // Apps & Features registry-entry metadata. KeyName is the HKLM
    // Uninstall subkey name (usually equals LocalAppDataFolderName).
    // DisplayName / Publisher / ProjectUrl drive the Settings →
    // Installed Apps card.
    public required string AppsAndFeaturesKeyName { get; init; }
    public required string DisplayName { get; init; }
    public required string Publisher { get; init; }
    public string ProjectUrl { get; init; } = "";
}
