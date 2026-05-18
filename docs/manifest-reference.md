# `CammModManifest` reference

Every field on `CammModManifest`, what it does, whether it's
required, and an example value. Source of truth is
[`Camm/CammModManifest.cs`](../Camm/CammModManifest.cs) — this
doc is a more-readable companion.

The manifest is consumed by `CammHost.RunAsync(args, manifest)` at
process startup. Once set, fields are read by every CAMM module
via `CammHost.Manifest`.

## Modes

The manifest's fields select one of two operating modes:

- **Launcher mode**: `GameInstance` is non-null. CAMM installs +
  registers IFEO + spawns the game + tails its log for speech +
  waits for game exit.
- **Installer-only mode**: `GameInstance` is null. CAMM installs +
  registers in Apps & Features + auto-updates, then exits.

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

## Launcher-mode-only fields

Set these for launcher mode. Leave null for installer-only mode.

### `IfeoTargetExeNames : string[]?`

The target-game exe filenames CAMM's IFEO redirect intercepts. When
the user launches one of these from Steam (or any other path), the
OS prepends your launcher to the command line. Null/empty = no
IFEO redirect installed (installer-only mode).

```csharp
IfeoTargetExeNames = new[] { "CivilizationVI.exe", "CivilizationVI_DX12.exe" }
```

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
before it reaches Tolk. Null = no log-tail speech bridge.

```csharp
Sanitizer = new CivViMessageSanitizer()
```

### `MarkerProtocol : IScreenReaderMarkerProtocol?`

Per-mod log-line marker convention. Identifies which log lines are
speech-bound (vs. ignored debug output) and parses any in-marker
options. Null = no log-tail speech bridge.

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
installer-only mode. Setting `GameInstance` without also setting
`IfeoTargetExeNames` / `GameProcessNames` / `Sanitizer` /
`MarkerProtocol` produces an inconsistent manifest — CAMM will try
to launch the game but won't intercept it via IFEO, won't relay
speech, won't know what process to wait for. Don't do that.

## Derived properties

### `IsInstallerOnly : bool`

True when `GameInstance is null`. Convenience for diagnostics; CAMM
modules use it to decide whether the launcher should exit after
install + update or continue into the game-launch flow.

### `AutoUpdateEnabled : bool`

True when all of `GitHubReleasesOwner`, `GitHubReleasesRepo`, and
`LauncherAssetNamePattern` are non-empty. CAMM skips the update
check on startup when this is false.

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
                            launcher mode | installer-only
                            ------------- | --------------
LocalAppDataFolderName          required  |    required
LauncherExeName                 required  |    required
UserAgent                       required  |    required
AppsAndFeaturesKeyName          required  |    required
DisplayName                     required  |    required
Publisher                       required  |    required
ModPayloads                     required  |    required
TargetGameDisplayName           required  |    required
TargetGameLauncherName        default OK  |  default OK
ProjectUrl                    optional    |  optional
GitHubReleasesOwner           optional*   |  optional*
GitHubReleasesRepo            optional*   |  optional*
LauncherAssetNamePattern      optional*   |  optional*
IfeoTargetExeNames              required  |     LEAVE NULL
GameProcessNames                required  |     LEAVE NULL
Sanitizer                       required  |     LEAVE NULL
MarkerProtocol                  required  |     LEAVE NULL
GameInstance                    required  |     LEAVE NULL
```

`*` = all three or none; auto-update is enabled only when all three
are set.

"required" in launcher mode = your launcher won't work right
without it. The compiler accepts null for the launcher-mode fields
because `IsInstallerOnly` adopters need to leave them null, but
setting `GameInstance` without the others is an inconsistent
configuration CAMM doesn't validate but will fail with a poor
runtime error.
