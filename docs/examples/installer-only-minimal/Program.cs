// CAMM installer-only minimal example — reference text, not a
// buildable project. Copy into your mod's repo as the starting
// point for a new launcher project.
//
// Installer-only mode is for in-process mods: Harmony, BepInEx,
// MelonLoader, IPA, Fabric, in-game C# plugins. The mod's runtime
// lives inside the game's process; CAMM is purely the install /
// update / uninstall framework.

using Camm;

return await CammHost.RunAsync(args, new CammModManifest
{
    // Identity --------------------------------------------------------
    LocalAppDataFolderName = "YourModInternalName",   // %LocalAppData%\YourModInternalName
    LauncherExeName        = "YourModInstaller.exe",  // matches <AssemblyName> in csproj
    AppsAndFeaturesKeyName = "YourModInternalName",   // HKLM\...\Uninstall\YourModInternalName
    DisplayName            = "Your Mod Display Name", // user-facing
    Publisher              = "Your Name",
    ProjectUrl             = "https://github.com/you/your-mod",
    UserAgent              = "YourModInstaller",      // for GitHub API requests

    // Target game (still required even in installer-only mode — used
    // by wizard copy strings, not by any launch flow) ----------------
    TargetGameDisplayName  = "Your Target Game",
    TargetGameLauncherName = "Steam",                 // or "Epic", "GOG", "standalone"

    // Mod payloads ----------------------------------------------------
    ModPayloads = new[]
    {
        new ModPayload(
            Name: "mod",
            FolderName: "YourModPayloadFolder",
            SentinelFileName: "About/About.xml",
            DefaultDestination: () => Path.Combine(
                // Most in-process mods drop into a per-user mod folder.
                // For RimWorld the canonical path is the LocalLow
                // mods folder; other games will vary.
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "AppData", "LocalLow",
                "YourGame Publisher", "YourGame", "Mods", "YourMod")),
    },

    // Auto-update (optional — set all three or leave all null) -------
    GitHubReleasesOwner      = "you",
    GitHubReleasesRepo       = "your-mod",
    LauncherAssetNamePattern = "YourModInstaller-{0}.exe",

    // Launcher-mode fields are NULL for installer-only ---------------
    // GameInstance, GameProcessNames, Sanitizer, MarkerProtocol all
    // omitted. CAMM detects installer-only mode from GameInstance
    // being null and skips the launcher flow entirely.

    // Opt-in: update-only-IFEO mode (v0.5+) --------------------------
    // Uncomment to register an IFEO redirect on the game's exe so
    // updates apply on every game launch instead of only when the
    // user re-runs the installer. CAMM intercepts briefly, runs the
    // update check, spawns the game, and exits — no log-tail, no
    // lifecycle wait.
    //
    // IfeoTargetExeNames = new[] { "YourGame.exe" },

    // Opt-in: PostInstallHook (v0.3.0+) ------------------------------
    // Use when your install isn't complete until you modify game-side
    // config (RimWorld's ModsConfig.xml, BepInEx's plugin enable list,
    // etc.). Receives the per-payload install manifests CAMM wrote.
    //
    // PostInstallHook = async installed =>
    // {
    //     await MyGameConfigEditor.EnableMod("your.mod.id");
    // },

    // Opt-in: PreInstallHook (v0.4.0+) -------------------------------
    // Symmetric partner — runs before payload extraction. Use for
    // migrating from a pre-CAMM deployed state, fetching a non-
    // GitHub-Releases dependency, or transforming game-side config
    // before payloads land.
    //
    // PreInstallHook = async () =>
    // {
    //     // your pre-extract setup here
    // },

    // Opt-in: declarative dependencies (v0.4.0+) ---------------------
    // Use when your mod requires an external bootstrap layer
    // (Harmony for RimWorld, BepInEx for Unity, MelonLoader for
    // Mono, IPA for Beat Saber). CAMM checks the sentinel at
    // install time, prompts the user, and fetches from GitHub
    // Releases.
    //
    // Dependencies = new[]
    // {
    //     new ModDependency(
    //         Name: "brrainz.harmony",
    //         DisplayName: "Harmony",
    //         GitHubReleasesOwner: "pardeike",
    //         GitHubReleasesRepo: "HarmonyRimWorld",
    //         AssetNamePattern: "Harmony-{0}.zip",
    //         InstallPath: () => Path.Combine(LocalLowMods, "Harmony"),
    //         SentinelFileName: "About/About.xml")
    //     {
    //         ZipRootStripPrefix = "*",
    //     },
    // },
});
