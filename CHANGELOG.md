# Changelog

Reverse-chronological. Dates are when work landed.

## Versioning

`0.0.x` for the initial extraction from `civ-vi-access`. `0.1.0` is
reserved for the point where `CammHost.RunAsync` lands (full routing
inside CAMM, consumer's Program.cs is a few lines) and a second mod
can consume CAMM without reading the civ-vi-access source. Pre-1.0
means any release can break API; consumers pin to a tag SHA and
upgrade when ready.

## 0.1.1 — 2026-05-17 — Step 7: localization (LocaleCatalog + en.json)

`Camm.Localization.Strings.Get(key)` looks up user-visible text in
the locale catalog. Lookup chain:

1. Loose `lang/<culture>.json` next to the consuming launcher exe
   (CurrentUICulture.Name, e.g. `de-DE.json`).
2. Loose `lang/<language>.json` (parent culture, e.g. `de.json`).
3. Embedded `lang/en.json` inside Camm.dll — always available.
4. Return the key + log a warning. Missing-string bugs surface to
   the user but never crash the launcher.

Manifest substitution tokens (`__DISPLAY_NAME__`, `__TARGET_GAME__`,
`__TARGET_LAUNCHER__`, `__PUBLISHER__`, `__INSTALL_DIR__`,
`__LOCAL_APP_DATA_FOLDER__`, `__LAUNCHER_EXE__`, `__VERSION__`) get
replaced at `Get()` time from `CammHost.Manifest`. Keeps the
localized values game-agnostic in the JSON file itself; per-mod
identity is injected at runtime.

AOT-clean via source-generated `LocaleJsonContext`
(`Dictionary<string,string>` only). No reflection on user types.

**Adding a translation:** copy `Camm/lang/en.json` to a new
`Camm/lang/<culture>.json`, translate the values, leave the keys
and `__*__` tokens untouched. Crowdin parses flat JSON natively;
translators can drop new files as PRs against the CAMM repo.

**What's localized:** all user-visible wizard text, all TaskDialog
content (cancel-confirm, channel picker, already-installed,
uninstall confirm + completion), all Tolk-spoken status messages
(update notifications, foreground-failed, startup-timeout, etc.).

**What's NOT localized** (by design): log/diagnostic messages
(English for devs), the "Powered by CAMM" footer (lineage marker),
launcher.ini comments (English file-format docs).

Loose-file detection happens once at first `Strings.Get` call (lazy
init). Adding new translations doesn't require a rebuild of CAMM
or the consuming launcher — just drop the JSON file next to the
deployed exe.

## 0.1.0 — 2026-05-17 — First consumable release

The extraction from civ-vi-access is functionally complete. CAMM
now has a stable public surface that a second accessibility-mod
author can consume without reading civ-vi-access source:

    using Camm;

    return await CammHost.RunAsync(args, new CammModManifest
    {
        // Identity
        LocalAppDataFolderName = "...",
        DisplayName = "...",
        Publisher = "...",
        TargetGameDisplayName = "...",
        TargetGameLauncherName = "Steam",
        LauncherExeName = "...",
        AppsAndFeaturesKeyName = "...",
        ProjectUrl = "...",

        // Update channel
        LauncherAssetNamePattern = "<mod>-{0}.exe",
        GitHubReleasesOwner = "...",
        GitHubReleasesRepo = "...",
        UserAgent = "<Mod>.Launcher",

        // Target game
        IfeoTargetExeNames = new[] { "GameExe.exe" },
        GameProcessNames = new[] { "GameExe" },

        // Mod payload
        ModPayloadFolderName = "...",
        ModPayloadSentinelFileName = "...",
        ModPayloadDefaultDestination = () => @"<mod folder>",

        // Per-mod implementations
        Sanitizer = new MyGameMessageSanitizer(),
        MarkerProtocol = new MyGameScreenReaderMarkerProtocol(),
        GameInstance = new MyGameInstance(),
    });

Adopters write three small implementation classes (Sanitizer,
MarkerProtocol, GameInstance), a per-game manifest, and a payload
directory of in-game mod files. CAMM provides install wizard,
uninstaller, IFEO transparent-launch redirect, Tolk speech routing,
GitHub-Releases auto-update with channel selection, AOT-clean
release pipeline. Public-surface stability is committed from this
release forward; breaking changes will bump the minor version.

What's in: SemVer, Logger, ProcessLauncher, TolkBootstrap, Dialogs,
LauncherSettings + UpdateChannel, GitHubReleasesClient, Updater,
IfeoInstaller, AppsAndFeaturesRegistration, WindowFocusManager,
ModFiles, ModDeployer, Installer, ChannelPickerDialog, Wizard (5
pages), Speech (AccessibleOutputHandler, LogTailSpeaker, Mediator,
TextOutputHandler, IMessageSanitizer, IScreenReaderMarkerProtocol),
CammModManifest, CammHost (with RunAsync entry), IGameInstance.
Vendored Tolk runtime under third_party/tolk with SOURCE.md
documenting provenance.

What's not in this release (deferred):
- LocaleCatalog + en.json (Step 7 of the original extraction plan).
  Visible strings remain hardcoded English. The architecture
  accommodates a JSON catalog drop-in; no API churn expected when it
  lands in a 0.1.x release.
- camm-new bootstrap tool. Adopters currently clone /Camm/ via
  ProjectReference manually.
- camm-registry GitHub Pages site for discovering CAMM-built mods.

First adopter: civ-vi-access (the source mod) consumes this CAMM
release at v0.3.0.

## 0.0.6 — 2026-05-17 — Step 9: CammHost.RunAsync (the unified entry point)

CammHost gains a `RunAsync(args, manifest)` method that absorbs the
entire chameleon-mode routing flow: pending-self-update, Tolk
bootstrap, args dispatch (--install / --uninstall / --version /
--config / --install-from-wizard / --wizard-test), transparent
invocation detection, bare-exe install trigger, Already-Installed
dialog, update check + apply, game launch via the configured
IGameInstance, log-tail speech, lifecycle watch.

New required CammModManifest field: `GameInstance` (IGameInstance).
The interface has four members:
- `FindGameExe()` — absolute path to the target game's main exe
- `GetLogFilePath()` — absolute path to the game's log file CAMM
  tails for speech-bound lines
- `GetLaunchAnnouncement()` — what to speak just before launching
  (mods that distinguish first-vs-subsequent launch return different
  text from here)
- `GetClosedAnnouncement()` — what to speak when the game process
  exits

A consuming launcher's Program.cs now looks like:

    return await CammHost.RunAsync(args, new CammModManifest { ... });

For Civ VI Access (the test-case adopter in this commit's companion
PR), that's ~30 lines of manifest construction + the CammHost.RunAsync
call, down from ~670 lines of routing logic. The Civ-VI-specific
hooks (CivilizationVI path, Lua.log path, EULA-aware launch text,
"Sid Meier's Civilization VI closed.") live in a single
CivViGameInstance class.

## 0.0.5 — 2026-05-17 — Step 8: speech seams

Log-tail speech routing moves into CAMM via two new manifest-supplied
interfaces:

- **IMessageSanitizer** — strips/transforms per-game in-engine markup
  ([ICON_*], [COLOR:*], [NEWLINE], etc.) before lines reach Tolk.
- **IScreenReaderMarkerProtocol** — identifies which log lines are
  screen-reader-bound (Civ VI uses `#SCREENREADER`; other games will
  use different conventions) and parses any embedded options
  (NOINTERRUPT etc.).

New Camm.Speech namespace:

- AccessibleOutputHandler — Tolk-routing wrapper with the
  interrupt-policy logic (3-second non-interrupt window per
  NOINTERRUPT line). Reads MarkerProtocol + Sanitizer from
  CammHost.Manifest at call time.
- LogTailSpeaker — generic log-tail loop (renamed from LogFileWatcher),
  polls a file for new bytes and feeds them to the Mediator.
- Mediator — fans inbound log chunks to AccessibleOutputHandler (speech)
  + TextOutputHandler (console diagnostics).
- TextOutputHandler — Console.WriteLine wrapper.
- SpeechOptions — record returned by MarkerProtocol.ParseOptions
  (NoInterrupt for now; extensible).

Two new required CammModManifest fields:

    Sanitizer = new MyGameMessageSanitizer(),
    MarkerProtocol = new MyGameScreenReaderMarkerProtocol(),

Consumer side (civ-vi-access in this commit's companion PR):
CivViMessageSanitizer + CivViScreenReaderMarkerProtocol implement
the seams with the existing regex maps + `#SCREENREADER[...]`
prefix logic.

After this release, the consumer's CivViAccess/ directory holds
only Program.cs + the two seam implementations + the mod payload —
ready for Step 9 (CammHost.RunAsync) to consume the remaining
routing logic.

## 0.0.4 — 2026-05-17 — Step 6: Installer + Wizard + ChannelPickerDialog

Installer.cs, ChannelPickerDialog.cs, and the entire Wizard/ folder
(IWizardPage, InstallContext, InstallWizardForm + Welcome/Channel/
Ready/Installing/Done pages) move from civ-vi-access into CAMM.

All mod-specific strings now flow through CammModManifest:
- `Install {DisplayName}` / `Installing {DisplayName}. Please wait.`
  (Welcome, Installing headings + announcements)
- `by {Publisher}, version X.Y.Z` (Welcome subhead)
- `Launch {TargetGameDisplayName} from {TargetGameLauncherName}` (Done
  body, Uninstall completion dialog)
- `{TargetGameDisplayName}'s mod folder` / `{TargetGameLauncherName}
  launch redirect` (Uninstall body)
- `%LocalAppData%\{LocalAppDataFolderName}\launcher.ini` (Done body)
- `{DisplayName} — Update Channel` / `{DisplayName} Setup`
  (ChannelPickerDialog title, wizard form title)

Two new required manifest fields capture target-game identity:
- TargetGameDisplayName — "Civilization VI", "RimWorld", etc.
- TargetGameLauncherName — defaults to "Steam"; override for Epic /
  GOG / standalone-installer games.

Build configuration: Camm.csproj gains <UseWindowsForms>true</UseWindowsForms>
+ <_SuppressWinFormsTrimError>true</_SuppressWinFormsTrimError>
+ NoWarn=WFO0001 (the "OutputType=WindowsApplication required"
analyzer; CAMM is a library consumed by a WindowsApplication exe).
ApplicationConfiguration.Initialize() replaced with explicit
Application.EnableVisualStyles / SetCompatibleTextRenderingDefault /
SetHighDpiMode calls in InstallWizardForm.Run, since the source-
generated Initialize only emits in WindowsApplication projects.

After this release, a CAMM-built launcher's Program.cs only needs
to: construct a CammModManifest, call CammHost.Initialize, then
invoke the framework's per-mode entry points (Installer.Install,
Installer.Uninstall, ChannelPickerDialog.Show, etc.). Step 7 of the
plan (LocaleCatalog) starts moving the visible strings out into
lang/en.json so translators can contribute.

## 0.0.3 — 2026-05-17 — Step 5: CammModManifest + CammHost

CammModManifest replaces the v0.0.2 CammConfig settable-statics
pattern. `required` init-only properties on the manifest give
compile-time errors if a consumer forgets to set something — much
safer than the old "set the wrong property and the launcher writes
to %LocalAppData%\Camm\ instead of the intended folder" failure
mode.

CammHost.Initialize(manifest) is called once by the consumer at
process startup; CammHost.Manifest is the read accessor every CAMM
module uses (throws on uninitialized access — fail-fast for ordering
bugs).

API surface changes:
- Logger.StartSession(mode, folderName) → Logger.StartSession(mode).
  Reads CammHost.Manifest.LocalAppDataFolderName.
- TolkBootstrap.PrepareRuntime(tempDirPrefix) →
  TolkBootstrap.PrepareRuntime(). Reads
  CammHost.Manifest.LocalAppDataFolderName.

Internals: every reference to CammConfig.X in the v0.0.2 modules
became CammHost.Manifest.X (mechanical rename across Logger,
LauncherSettings, GitHubReleasesClient, Updater, IfeoInstaller,
AppsAndFeaturesRegistration, WindowFocusManager, ModDeployer).
CammConfig.cs deleted.

Still to come (Steps 6+ of CAMM_EXTRACTION_PLAN.md): move Installer +
Wizard into CAMM, introduce LocaleCatalog, move LogTailSpeaker +
AccessibleOutputHandler with sanitizer/marker-protocol seams, then
ship the full CammHost.RunAsync(args, manifest) routing entry point
that lets consumer Program.cs be a few lines.

## 0.0.1 — 2026-05-17 — Initial extraction (Step 1 of the migration plan)

First commit. Five lowest-risk modules extracted from `civ-vi-access`
per the
[CAMM_EXTRACTION_PLAN.md](https://github.com/nromey/civ-vi-access/blob/main/CAMM_EXTRACTION_PLAN.md)
roadmap (Step 1):

- **`Camm.Logger`** — file-based session logger to
  `%LocalAppData%\<modfolder>\launcher.log`. Mod-folder name is the
  one piece of per-mod state — set once at session start via
  `Logger.StartSession`.
- **`Camm.SemVer`** — three-part semver record (Major.Minor.Patch) with
  parsing, comparison, and assembly-version-driven `Current()`. Used
  across the update flow and about-print.
- **`Camm.ProcessLauncher`** — DEBUG_PROCESS IFEO-bypass spawn for the
  transparent-launch case. Standard pattern for any IFEO-using launcher.
- **`Camm.TolkBootstrap`** — embedded-resource extraction of Tolk
  sidecar DLLs (NVDA controller, SAPI bridge) into the running exe's
  directory, with `SetDllDirectory` fallback for pre-install runs.
- **`Camm.Dialogs`** — TaskDialog + MessageBox P/Invoke helpers,
  AOT-clean. `ShowChoice` accepts an optional `ownerHwnd` for parenting
  on a host Form (wizard use); falls back to console-window-as-owner
  for console-only callers. Handles Win11 foreground-stealing rules.

Plus the vendored `third_party/tolk/` Tolk runtime (C# binding + native
DLLs) that all this depends on.

No public entry point (`CammHost`) yet — Step 5 of the migration plan
introduces that. v0.0.1 is consumable today only via direct file
inclusion (`<Compile Include>`) or as a git submodule + ProjectReference
from a consuming repo that already knows the internal API. The first
real consumer is `civ-vi-access`.
