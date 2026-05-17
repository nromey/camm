# Changelog

Reverse-chronological. Dates are when work landed.

## Versioning

`0.0.x` for the initial extraction from `civ-vi-access`. `0.1.0` is
reserved for the point where `CammHost.RunAsync` lands (full routing
inside CAMM, consumer's Program.cs is a few lines) and a second mod
can consume CAMM without reading the civ-vi-access source. Pre-1.0
means any release can break API; consumers pin to a tag SHA and
upgrade when ready.

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
