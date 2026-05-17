namespace Camm;

// Per-mod configuration consumed by the CAMM modules at runtime.
// The consuming launcher sets these once at startup, before any
// other CAMM module is used. Step 5 of the extraction plan
// (CAMM_EXTRACTION_PLAN.md) wraps these in a proper CammModManifest
// + CammHost.RunAsync entry point so consumers pass a single
// manifest object rather than mutating statics — at which point this
// class either becomes private state of CammHost or is renamed.
//
// Until that lands, settable statics are the cheapest path forward
// for Steps 3-4. The fields all have placeholder defaults so an
// unconfigured CAMM-built launcher doesn't crash; instead it writes
// files under %LocalAppData%\Camm\, polls GitHub for the (empty)
// owner/repo, and so on — clearly wrong values that surface in logs.
public static class CammConfig
{
    // Folder name for per-user state (launcher.ini, launcher.log,
    // .mod-redeploy-needed marker). Lives under %LocalAppData%.
    public static string LocalAppDataFolderName { get; set; } = "Camm";

    // Asset filename pattern produced by the release pipeline, where
    // {0} is the version string. Used by Updater to find the launcher
    // exe in a GitHub release's assets. Example: "CivViAccess-{0}.exe".
    public static string LauncherAssetNamePattern { get; set; } = "Camm-{0}.exe";

    // GitHub Releases polling. Owner is the user/org; Repo is the
    // repo name. Update checks fetch
    // https://api.github.com/repos/<Owner>/<Repo>/releases.
    public static string GitHubReleasesOwner { get; set; } = "";
    public static string GitHubReleasesRepo { get; set; } = "";

    // User-Agent header for HTTP requests to GitHub. GitHub's API
    // requires a non-empty UA; pick something identifying.
    public static string UserAgent { get; set; } = "Camm-Launcher";

    // Target-game exe filenames the IFEO redirect intercepts. Civ VI
    // Access has two (CivilizationVI.exe + CivilizationVI_DX12.exe);
    // most games have one.
    public static string[] IfeoTargetExeNames { get; set; } = Array.Empty<string>();

    // Process names (no .exe suffix) for the foreground-management
    // and lifecycle-watch loops. Distinct from IfeoTargetExeNames
    // because Process.GetProcessesByName uses the process name
    // without ".exe" and some games name the process differently
    // from the exe filename.
    public static string[] GameProcessNames { get; set; } = Array.Empty<string>();

    // Mod payload metadata. Folder name = the directory CAMM looks
    // for in parent dirs during dev-mode source discovery; sentinel
    // file = the file CAMM checks for inside that folder to confirm
    // it's genuinely a mod source dir, not a name collision.
    public static string ModPayloadFolderName { get; set; } = "";
    public static string ModPayloadSentinelFileName { get; set; } = "";

    // Default destination where the mod payload gets deployed at
    // install time (Civ VI's DLC dir, RimWorld's user-data Mods/
    // dir, etc.). A Func<string> rather than a constant because the
    // destination commonly needs Environment.GetFolderPath
    // resolution at call time.
    public static Func<string> ModPayloadDefaultDestination { get; set; } =
        () => string.Empty;

    // Apps & Features registry-entry metadata. KeyName is the HKLM
    // Uninstall subkey name (usually equals LocalAppDataFolderName).
    // DisplayName / Publisher / ProjectUrl drive the Settings →
    // Installed Apps card.
    public static string AppsAndFeaturesKeyName { get; set; } = "Camm";
    public static string DisplayName { get; set; } = "Camm Mod";
    public static string Publisher { get; set; } = "";
    public static string ProjectUrl { get; set; } = "";

    // Filename of the installed launcher exe (without path). Used by
    // installer paths that reference the exe in Program Files. Civ
    // VI Access uses "CivViAccess.exe"; a Factorio Access adopter
    // would use "FactorioAccess.exe".
    public static string LauncherExeName { get; set; } = "Camm.exe";
}
