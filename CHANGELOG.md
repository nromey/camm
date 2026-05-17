# Changelog

Reverse-chronological. Dates are when work landed.

## Versioning

`0.0.x` for the initial extraction from `civ-vi-access`. `0.1.0` is
reserved for the point where the public surface (`CammHost.RunAsync` +
`CammModManifest`) lands and a second mod can consume CAMM without
reading the civ-vi-access source. Pre-1.0 means any release can break
API; consumers pin to a tag SHA and upgrade when ready.

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
