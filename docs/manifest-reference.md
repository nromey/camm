# `CammModManifest` reference

Every field on `CammModManifest`, what it does, whether it's
required, and an example value. Source of truth is
[`Camm/CammModManifest.cs`](../Camm/CammModManifest.cs) — this
doc is a more-readable companion.

The manifest is consumed by `CammHost.RunAsync(args, manifest)` at
process startup. Once set, fields are read by every CAMM module
via `CammHost.Manifest`.

## Modes

The manifest's fields select one of three operating modes:

- **Launcher mode with log-tail**: `GameInstance`, `Sanitizer`, and
  `MarkerProtocol` all non-null. CAMM installs + registers IFEO +
  spawns the game + tails its log for speech + waits for game exit.
  Civ VI Access shape.
- **Launcher mode without log-tail**: `GameInstance` non-null,
  `Sanitizer` and `MarkerProtocol` null. CAMM installs + registers
  IFEO + spawns the game + waits for game exit, but no log-tail
  speech bridge. For mods whose runtime speaks in-process (Civ V
  Access pattern: Lua proxy DLL injecting Tolk into the game's
  scripting context). Saves you from writing two seam classes that
  CAMM would never call.
- **Installer-only mode**: `GameInstance` null. CAMM installs +
  registers in Apps & Features, then exits. No game launch ever
  happens via CAMM. By default no IFEO redirect either — updates
  apply when the user re-runs the installer exe.
- **Installer-only mode with update-on-launch IFEO** (v0.5+):
  `GameInstance` null + `IfeoTargetExeNames` non-null. CAMM
  registers an IFEO redirect on the target game's exe, and on every
  game launch the launcher briefly runs to apply any pending update
  before spawning the real game. No log-tail, no lifecycle wait, no
  foreground handoff — the user experiences the launch as if CAMM
  weren't there. Opt in to this if you want auto-update parity with
  launcher mode in an installer-only adopter shape.

Three derived properties:
- `IsInstallerOnly` → true when `GameInstance is null`.
- `LogTailEnabled` → true when both `Sanitizer` and `MarkerProtocol`
  are set. False otherwise (installer-only mode OR launcher mode
  without log-tail).
- `UpdateOnlyIfeoEnabled` → true when installer-only mode is combined
  with a non-empty `IfeoTargetExeNames`. v0.5+; selects between the
  two installer-only sub-modes.

The mode-selection guide and "which fields apply" matrix below
explain which manifest fields belong to which mode.

## Always-required fields

These must be set regardless of mode.

### `LocalAppDataFolderName : string` *(required)*

Folder name under `%LocalAppData%` for per-user state. Holds
`launcher.ini`, `launcher.log`, the redeploy marker, per-payload
installed-files manifests.

```csharp
LocalAppDataFolderName = "CivVIAccess"
```

Should be ASCII, no spaces (used as a Windows path component). Pick
something unique so two CAMM-built mods don't fight over the same
folder.

### `LauncherExeName : string` *(required)*

Filename of the launcher exe (without path) once installed. Used in
install-dir naming and various status messages.

```csharp
LauncherExeName = "CivViAccess.exe"
```

Should match the `<AssemblyName>` in your csproj.

### `UserAgent : string` *(required)*

HTTP User-Agent for GitHub API requests. GitHub's API requires a
non-empty UA — pick something identifying.

```csharp
UserAgent = "CivVIAccess.Launcher"
```

### `AppsAndFeaturesKeyName : string` *(required)*

HKLM Uninstall subkey name. Usually equals
`LocalAppDataFolderName`. Exposed separately so adopters with
collisions can choose a different key.

```csharp
AppsAndFeaturesKeyName = "CivVIAccess"
```

### `DisplayName : string` *(required)*

User-facing name of your mod. Shows up in install-wizard pages,
Apps & Features card, Tolk-spoken announcements. Also drives the
install location (`Program Files\<DisplayName>\`).

```csharp
DisplayName = "Civ VI Access"
```

### `Publisher : string` *(required)*

Your name (or your project's name). Shows in Apps & Features and
on the Welcome page's subhead.

```csharp
Publisher = "Noel Romey"
```

### `ModPayloads : IReadOnlyList<ModPayload>` *(required, non-empty)*

The deployable artifact groups for your mod. **Must have at least
one entry.** Each entry has its own embed-resource prefix, dev-mode
source-discovery hint, and deploy destination. See
[ModPayload](#modpayload-record-fields) below.

```csharp
ModPayloads = new[]
{
    new ModPayload(
        Name: "mod",
        FolderName: "CivViAccessMod",
        SentinelFileName: "CivViAccessMod.modinfo",
        DefaultDestination: () => @"C:\...\DLC\CivViAccessMod"),
},
```

Multi-root example (Civ V Access shape):

```csharp
ModPayloads = new[]
{
    new ModPayload(
        Name: "dlc",
        FolderName: "dlc",
        SentinelFileName: "DLC.modinfo",
        DefaultDestination: () => @"C:\Game\Assets\DLC\MyMod"),
    new ModPayload(
        Name: "proxy",
        FolderName: "proxy",
        SentinelFileName: "lua51.dll",
        DefaultDestination: () => @"C:\Game"),  // single-file payload
    new ModPayload(
        Name: "engine",
        FolderName: "engine",
        SentinelFileName: "MyEngine.dll",
        DefaultDestination: () => @"C:\Game\Assets\Engine"),
},
```

### `TargetGameDisplayName : string` *(required)*

The target game's name as users see it. Used in wizard / installer
/ uninstaller text ("Launch Civilization VI from Steam",
"Civilization VI's mod folder").

```csharp
TargetGameDisplayName = "Civilization VI"
```

Even installer-only adopters set this (the visible strings still
reference your target game).

## Optional fields with defaults

### `TargetGameLauncherName : string = "Steam"`

Storefront / launcher the user starts the game from. Override for
non-Steam targets.

```csharp
TargetGameLauncherName = "Steam"   // (default)
TargetGameLauncherName = "Epic"
TargetGameLauncherName = "GOG"
TargetGameLauncherName = "standalone"
```

### `ProjectUrl : string = ""`

Project home page. Shows in Apps & Features as a "Visit website"
link. Empty = no link.

```csharp
ProjectUrl = "https://github.com/nromey/civ-vi-access"
```

### `ScreenReaderBackend : ScreenReaderBackend = Tolk`

Which screen-reader output library CAMM routes in-game speech through:
`Tolk` (default — the cross-mod convention) or `Prism`
([ethindp/prism](https://github.com/ethindp/prism), broader cross-platform
reach). `AccessibleOutputHandler` calls through the chosen `IScreenReader`;
the dedupe + interrupt policy is backend-agnostic.

```csharp
ScreenReaderBackend = Camm.Speech.ScreenReaderBackend.Prism   // default is Tolk
```

You must also **bundle** the matching native DLLs — embed `tolk/*` and/or
`prism/*` resources in your launcher exe. An adopter ships Tolk, Prism, or
both:

- Tolk: embed the vendored binaries
  (`..\camm\third_party\tolk\dist\x64\*.dll` → `tolk/...`).
- Prism: import `camm/build/Camm.Prism.targets` and set
  `<CammPrismMode>BuildFromSource</CammPrismMode>` (builds `prism.dll` from
  the pinned Prism submodule during your build and embeds it as
  `prism/prism.dll`). See [`build/PRISM.md`](../build/PRISM.md).

If `Prism` is selected but its native lib is missing or no Prism backend
initializes, CAMM falls back to Tolk automatically. Override the selection
at launch (no rebuild) with the `CAMM_SCREEN_READER_BACKEND` env var
(`tolk` / `prism`) — handy for A/B testing before a runtime picker exists.

## Auto-update fields (all-or-nothing)

These three fields work as a unit. Either set all three (auto-update
enabled) or leave all null (auto-update disabled — useful during
initial bring-up before you've stood up a GitHub Releases pipeline).

`manifest.AutoUpdateEnabled` is `true` only if all three are set.

**Setting these before any release exists is safe.** During initial
bring-up the repo will have no releases (or a single "draft" /
"prerelease" with no matching asset). CAMM's update check handles
this gracefully — the GitHub Releases API returns 404 for "no
releases on this repo," which the updater treats as "no update
available, carry on." No dialog, no error, no failure. Same for
the case where releases exist but none match
`LauncherAssetNamePattern` — the updater logs the miss and moves
on. You can ship the launcher with these fields populated before
your release pipeline is wired up; the first real release will
just start working.

### `GitHubReleasesOwner : string?`

The GitHub user/org that owns the releases repo.

```csharp
GitHubReleasesOwner = "nromey"
```

### `GitHubReleasesRepo : string?`

The GitHub repo name.

```csharp
GitHubReleasesRepo = "civ-vi-access"
```

### `LauncherAssetNamePattern : string?`

Filename pattern of the launcher exe in your GitHub release's
assets. `{0}` gets replaced with the bare version string (no "v"
prefix).

```csharp
LauncherAssetNamePattern = "CivViAccess-{0}.exe"
// Matches: CivViAccess-0.3.0.exe, CivViAccess-0.3.1.exe, ...
```

## Launcher-mode and update-only-IFEO fields

Set these for launcher mode. Leave null for plain installer-only
mode. `IfeoTargetExeNames` is the exception — see its note below.

### `IfeoTargetExeNames : string[]?`

The target-game exe filenames CAMM's IFEO redirect intercepts. When
the user launches one of these from Steam (or any other path), the
OS prepends your launcher to the command line.

```csharp
IfeoTargetExeNames = new[] { "CivilizationVI.exe", "CivilizationVI_DX12.exe" }
```

**Mode interaction (v0.5+):** this field is required in launcher
mode, optional in installer-only mode. Setting it in installer-only
mode opts into "update-on-launch IFEO" — CAMM registers the redirect
but skips the rest of the launcher-mode flow (no log-tail, no
lifecycle wait, no game-launch announcement). On every game launch,
CAMM runs the update check and then spawns the real game. Leave
null to fall back to plain installer-only mode (updates apply only
when the user re-runs the installer exe).

Civ VI ships two binaries (DX11 + DX12 variants); most games have
one. Match against the actual exe filenames the game ships, not the
process names.

### `GameProcessNames : string[]?`

Process names (no `.exe` suffix) for foreground-management +
lifecycle-watch. Distinct from `IfeoTargetExeNames` because
`Process.GetProcessesByName` uses the process name without `.exe`,
and some games name the process differently from the exe filename.

```csharp
GameProcessNames = new[] { "CivilizationVI", "CivilizationVI_DX12" }
```

### `Sanitizer : IMessageSanitizer?`

Per-mod in-engine markup sanitizer for log-tail speech. Strips
markup (`[ICON_*]`, `[COLOR:*]`, `[NEWLINE]`, etc.) from each line
before it reaches Tolk.

Optional even in launcher mode. Setting Sanitizer AND MarkerProtocol
enables the log-tail bridge; leaving both null skips it (e.g. for
mods whose speech goes through an in-process Tolk binding, not a
log file). Setting only one of the two is treated the same as
setting neither — log-tail requires both.

```csharp
Sanitizer = new CivViMessageSanitizer()
```

### `MarkerProtocol : IScreenReaderMarkerProtocol?`

Per-mod log-line marker convention. Identifies which log lines are
speech-bound (vs. ignored debug output) and parses any in-marker
options. Same optionality rules as `Sanitizer` — both or neither.

```csharp
MarkerProtocol = new CivViScreenReaderMarkerProtocol()
```

### `GameInstance : IGameInstance?`

Per-game hooks for the main launch flow: locate the game exe, find
its log file, supply launch + closed announcements. **Null →
installer-only mode**: CAMM skips transparent-invocation detection,
locate-game, foreground-handoff, log-tail, lifecycle-wait.

```csharp
GameInstance = new CivViGameInstance()
```

This is the single field that selects between launcher mode and
installer-only mode.

`IGameInstance.GetLogFilePath()` is **only called when
`LogTailEnabled` is true** — i.e. when Sanitizer + MarkerProtocol are
both set. Launcher-mode adopters without a log-tail bridge can
return any string from GetLogFilePath; CAMM won't open the file.

Setting `GameInstance` without also setting `IfeoTargetExeNames` /
`GameProcessNames` produces an inconsistent manifest — CAMM will try
to launch the game but won't intercept it via IFEO, won't know what
process to wait for. Don't do that.

### `PostInstallHook : Func<IReadOnlyDictionary<string, PayloadInstallManifest>, Task>?` *(optional)*

Async hook invoked after all payloads have been extracted and IFEO
+ Apps & Features have been registered, but BEFORE the "install
complete" announcement to the user. Receives a dictionary keyed by
`ModPayload.Name` with the install manifests CAMM just wrote (each
manifest lists the files written and any backed-up `.original`
files).

```csharp
PostInstallHook = async installed =>
{
    var modsConfig = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "AppData", "LocalLow", "Ludeon Studios",
        "RimWorld by Ludeon Studios", "Config", "ModsConfig.xml");
    await ModsConfigEditor.EnsureModEnabled(modsConfig, "shane12300.rimworldaccess");
}
```

Use for game-side config CAMM doesn't model: RimWorld's
`ModsConfig.xml`, BepInEx's plugin enable list, ModInfo
registration for engines that require it. Throwing from the hook
fails the install (wizard's Done page shows FailureBody). Idempotent
behavior is your responsibility — install-over-install will call it
again with the new payload manifests.

### `Dependencies : IReadOnlyList<ModDependency>?` *(optional, v0.4.0)*

External-mod dependencies your adopter requires — typically a
bootstrap layer the mod runs inside (Harmony for RimWorld, BepInEx
for Unity games, MelonLoader for Mono games, IPA for Beat Saber).

```csharp
Dependencies = new[]
{
    new ModDependency(
        Name: "brrainz.harmony",
        DisplayName: "Harmony",
        GitHubReleasesOwner: "pardeike",
        GitHubReleasesRepo: "HarmonyRimWorld",
        AssetNamePattern: "Harmony-{0}.zip",
        InstallPath: () => @"C:\...\Mods\Harmony",
        SentinelFileName: "About/About.xml")
    {
        ZipRootStripPrefix = "*",
    },
},
```

At install time CAMM checks each dependency's sentinel
(`InstallPath/SentinelFileName`). Missing → prompts the user
(Install / Skip / Cancel), then fetches the latest release from
GitHub Releases and extracts it. Idempotent — present-sentinel
means CAMM skips the fetch entirely. See
[`ModDependency` record fields](#moddependency-record-fields) for
each field's semantics.

Dependencies are install-time only — CAMM doesn't auto-update them
after first install. They also survive adopter-mod uninstall
(they're shared resources another mod may need). All network /
extraction failures show a retry-or-skip-or-cancel dialog; the user
always knows when a dependency couldn't be installed.

### `PreInstallHook : Func<Task>?` *(optional, v0.4.0)*

Symmetric partner to `PostInstallHook`. Runs after Tolk extraction
and BEFORE dependency installation and payload extraction.

```csharp
PreInstallHook = async () =>
{
    // Migrate from a pre-CAMM deploy.ps1 install.
    var staleBackup = Path.Combine(GameDir, "lua51_original.dll");
    if (File.Exists(staleBackup))
    {
        File.Move(staleBackup,
            Path.Combine(GameDir, "lua51_Win32.dll"),
            overwrite: true);
    }
},
```

Use for arbitrary scripted setup CAMM doesn't model declaratively:
migrating from a pre-CAMM deployed state, fetching a
non-GitHub-Releases dependency (CAMM's `Dependencies` field only
supports GitHub-Releases sources), transforming a config file
before payloads land.

Same contract as `PostInstallHook`: throw to fail the install,
idempotency is your responsibility, `CammHost.Manifest` is
statically available from inside the hook.

## `ModDependency` record fields

`record ModDependency(string Name, string DisplayName, string GitHubReleasesOwner, string GitHubReleasesRepo, string AssetNamePattern, Func<string> InstallPath, string SentinelFileName)`.
Init-only `ZipRootStripPrefix : string?` for zip-extraction-prefix
handling.

### `Name : string`

Stable identifier — persisted as the manifest filename at
`%LocalAppData%\<adopter>\dep-<Name>.json` and used in log messages.
Use the dependency's canonical ID: `"brrainz.harmony"`, `"BepInEx"`,
etc.

### `DisplayName : string`

User-facing name shown in the install consent prompt, "Downloading
..." status, and error messages.

### `GitHubReleasesOwner / GitHubReleasesRepo : string`

GitHub user-or-org and repository name to fetch from. CAMM calls
`api.github.com/repos/<owner>/<repo>/releases/latest`.

### `AssetNamePattern : string`

Filename pattern of the asset to download. `{0}` substitutes the
release's tag with any leading `v` stripped (same convention as
CAMM's own `LauncherAssetNamePattern`). Examples:
`"Harmony-{0}.zip"`, `"BepInEx_x64_{0}.zip"`, `"plugin.dll"` (for a
single-file dep whose name doesn't change between versions).

### `InstallPath : Func<string>`

Directory the dependency extracts into. Called at runtime so the
closure can do `Environment.GetFolderPath` resolution. Must return
an absolute path.

### `SentinelFileName : string`

Relative path inside `InstallPath` that proves the dependency is
already installed. Sub-paths like `"About/About.xml"` are honored.
CAMM skips the fetch entirely if this file exists. Empty strings
are not supported (the record throws at construction).

### `ZipRootStripPrefix : string?` *(init-only, optional)*

When the dependency's zip wraps content in a top-level directory
(the common GitHub Release shape — `HarmonyRimWorld-2.3.3/...`),
strip that prefix during extraction so the dependency lands
directly under `InstallPath`.

- Literal string (e.g. `"HarmonyRimWorld-2.3.3"`) → strip that
  exact prefix.
- `"*"` → strip whatever the first directory in the zip turns out
  to be. Use this when the prefix changes per release.
- `null` (default) → extract as-is, preserving any top-level zip
  folders.

Bare-DLL dependencies (`AssetNamePattern` ending in `.dll`) ignore
this — there's no archive to strip.

## Derived properties

### `IsInstallerOnly : bool`

True when `GameInstance is null`. Convenience for diagnostics; CAMM
modules use it to decide whether the launcher should exit after
install + update or continue into the game-launch flow.

### `AutoUpdateEnabled : bool`

True when all of `GitHubReleasesOwner`, `GitHubReleasesRepo`, and
`LauncherAssetNamePattern` are non-empty. CAMM skips the update
check on startup when this is false.

### `LogTailEnabled : bool`

True when both `Sanitizer` and `MarkerProtocol` are non-null. CAMM
starts the log-tail speech bridge only when this is true; otherwise
launcher mode still spawns the game and waits for lifecycle, but
without reading its log file for speech-bound lines.

### `UpdateOnlyIfeoEnabled : bool` *(v0.5.0)*

True when `IsInstallerOnly` is true AND `IfeoTargetExeNames` has
at least one entry. Selects between the two installer-only sub-
modes: plain installer-only (false — no IFEO redirect, user
re-runs installer for updates) vs. installer-only with
update-on-launch (true — IFEO redirect on the game's exe applies
updates before each launch).

### `HasDependencies : bool` *(v0.4.0)*

True when `Dependencies` is non-null and non-empty. Convenience for
diagnostics and conditional log lines — `Installer.ApplyInstall`
uses it to decide whether to log the "Checking N declared
dependency(ies)..." line.

## ModPayload record fields

`record ModPayload(string Name, string FolderName, string SentinelFileName, Func<string> DefaultDestination)`.

### `Name : string`

Used as the embed-resource prefix in your csproj's
`<EmbeddedResource>` glob. A payload `Name="dlc"` reads resources
whose logical name starts with `dlc/`.

```xml
<EmbeddedResource Include="..\dlc\**\*.*">
    <LogicalName>dlc/%(RecursiveDir)%(Filename)%(Extension)</LogicalName>
</EmbeddedResource>
```

Convention: lowercase, short, ASCII. Civ VI Access uses `"mod"` for
its single payload.

### `FolderName : string`

Dev-mode source-discovery hint. CAMM walks up from the launcher
exe's directory at startup, looking for a directory named this
that contains the sentinel. If found, CAMM treats it as the mod
source and copies its contents into `DefaultDestination()` (the
dev edit-build-launch loop).

```csharp
FolderName = "CivViAccessMod"
```

For multi-payload mods, each payload has its own FolderName — they
can all live as siblings at the repo root.

**Walk semantics.** The walk is parent-walk + one-step-down: at
each parent directory, CAMM checks both that directory and its
immediate children for a folder named `FolderName`. It does NOT
recurse deeply. If your source lives more than one level inside
its containing directory (e.g. `repo/src/dlc/<your folder>`),
dev-mode discovery won't find it; the embedded-resource path is
your only path, and that's fine — install/update flows always
read from embedded resources regardless.

**Not-found behavior.** If no matching folder + sentinel is found
during the walk, CAMM silently skips dev-mode redeploy for that
payload and proceeds with the embedded-resource extraction. This
is the expected path for installer-only adopters with existing
build pipelines (the payload is assembled from build outputs
elsewhere on disk — there's no single source folder for CAMM to
find). Set `FolderName` to a plausible string anyway (it's
required) and don't worry that the folder doesn't exist on disk.

### `SentinelFileName : string`

A file path **relative to FolderName** that proves the discovered
directory is genuinely your mod source (and not a random
name-collision elsewhere on disk).

```csharp
SentinelFileName = "CivViAccessMod.modinfo"
SentinelFileName = "About/About.xml"           // sub-path is fine
SentinelFileName = ""                          // disables the check (any matching folder wins)
```

Sub-paths are supported (`"About/About.xml"`, `"src/main.lua"`,
etc.) — useful when your folder root has no distinctive file but
a known subdirectory does.

Empty string disables the sentinel check — only the folder name
match is used. Less safe; use only when your folder name is
distinctive enough that a name collision is implausible.

**Not-found behavior.** If the folder is found but the sentinel
file doesn't exist relative to it, CAMM treats the folder as a
false positive and continues walking. If no `FolderName + sentinel`
pair is found anywhere on the walk, dev-mode redeploy silently
no-ops for this payload — see `FolderName` above.

### `DefaultDestination : Func<string>`

Where this payload deploys at install + update time. Returns a
directory path (not a file). Called at runtime so it can do
`Environment.GetFolderPath` resolution.

**Call timing.** CAMM invokes the closure once per `ExtractTo`
call — i.e. once per install pass and once per update pass per
payload, not once per file extracted. If your closure reads
side-channel state (env vars, registry, a config file), reads at
that cadence will be consistent throughout a single extraction
but may differ across runs. For deterministic destinations
(typical case), this doesn't matter.

```csharp
DefaultDestination = () => @"C:\Program Files (x86)\Steam\...\DLC\CivViAccessMod"

DefaultDestination = () => Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    "AppData", "LocalLow", "Ludeon Studios",
    "RimWorld by Ludeon Studios", "Mods", "MyMod")
```

Single-file payloads: make `DefaultDestination` return the *parent
directory* of where the file lands. The payload contains just that
one file; CAMM extracts it into the parent.

## Mode-selection cheat sheet

```
                            launcher    | launcher       | installer-  | installer-only
                            +log-tail   | (no log-tail)  | only        | +update-IFEO (v0.5)
                            ----------- | -------------- | ----------- | -------------------
LocalAppDataFolderName       required   |   required     | required    |   required
LauncherExeName              required   |   required     | required    |   required
UserAgent                    required   |   required     | required    |   required
AppsAndFeaturesKeyName       required   |   required     | required    |   required
DisplayName                  required   |   required     | required    |   required
Publisher                    required   |   required     | required    |   required
ModPayloads                  required   |   required     | required    |   required
TargetGameDisplayName        required   |   required     | required    |   required
TargetGameLauncherName     default OK   | default OK     | default OK  | default OK
ProjectUrl                 optional     | optional       | optional    | optional
GitHubReleasesOwner        optional*    | optional*      | optional*   | optional*
GitHubReleasesRepo         optional*    | optional*      | optional*   | optional*
LauncherAssetNamePattern   optional*    | optional*      | optional*   | optional*
IfeoTargetExeNames           required   |   required     | LEAVE NULL  |   required
GameProcessNames             required   |   required     | LEAVE NULL  | LEAVE NULL
GameInstance                 required   |   required     | LEAVE NULL  | LEAVE NULL
Sanitizer                    required   |  LEAVE NULL    | LEAVE NULL  | LEAVE NULL
MarkerProtocol               required   |  LEAVE NULL    | LEAVE NULL  | LEAVE NULL
PostInstallHook            optional     | optional       | optional    | optional
PreInstallHook             optional     | optional       | optional    | optional
Dependencies               optional     | optional       | optional    | optional
```

`installer-only +update-IFEO` is the v0.5 opt-in: installer-only
mode plus an `IfeoTargetExeNames` value. CAMM registers the IFEO
redirect on the game's exe and runs the update check on every
game launch before spawning the real game. `GameProcessNames`
stays null in this mode because CAMM doesn't wait for the game's
lifecycle — it spawns and exits.

`*` = all three or none; auto-update is enabled only when all three
are set.

"required" = your launcher won't work right without it. The
compiler accepts null for the launcher-mode fields because
installer-only adopters and log-tail-skipping launcher-mode adopters
need to leave them null. Setting `GameInstance` without
`IfeoTargetExeNames` / `GameProcessNames` is an inconsistent
configuration CAMM doesn't validate but will fail with a poor
runtime error.

### Per-payload `OverwriteStrategy` (new in v0.3.0)

Each `ModPayload` has an `OverwriteStrategy` init-only property
defaulting to `Replace`. Set `BackupAndReplace` for payloads that
overwrite files the game ships (engine DLLs, scripting host DLLs):

```csharp
new ModPayload(
    Name: "engine",
    FolderName: "engine",
    SentinelFileName: "CvGameCore_Expansion2.dll",
    DefaultDestination: () => @"...\Assets\DLC\Expansion2")
{
    OverwriteStrategy = OverwriteStrategy.BackupAndReplace,
}
```

`BackupAndReplace` renames each existing target to `<filename>.original`
before writing the new content. On uninstall, CAMM deletes the
CAMM-installed file and restores the `.original` rename, returning
the game install to its pre-CAMM state. Without this, uninstall via
the standard per-file install manifest would simply delete the engine
DLL and leave the user with a broken game.

The pre-CAMM state is captured at the FIRST `BackupAndReplace`
install (when `.original` doesn't yet exist). Subsequent installs
that find a pre-existing `.original` preserve it — they don't
overwrite the user's actual vanilla file with a CAMM-modified copy
from a prior install.
