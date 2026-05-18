# CAMM v0.4.0 plan — Dependencies + PreInstallHook

**Date:** 2026-05-18
**Status:** Design draft, pre-implementation.

v0.4.0 adds two features driven by the v0.3.0 dual-track
AI-readability test gaps:

1. **`ModDependency`** — declarative external-mod installation
   (Harmony for RimWorld, BepInEx for Unity games, MelonLoader for
   Mono games, IPA for Beat Saber, etc.). CAMM checks for the
   dependency at install time and, with user consent, fetches and
   extracts it from GitHub Releases.
2. **`PreInstallHook`** — the symmetric partner to v0.3.0's
   `PostInstallHook`. Runs before payload extraction. For arbitrary
   scripted work: migrating from a pre-CAMM deployed state, fetching
   a non-GitHub-Releases dependency, transforming a config file
   before the install lands.

Both additions are **additive**. v0.3.x adopters keep working
untouched after a submodule bump.

---

## Why v0.4

The v0.3.0 dual-track tests landed two related gaps:

- **In-process mod adopters need their bootstrap layer.** RimWorld
  Access requires `brrainz.harmony`; the test report flagged that
  "CAMM cannot install the Harmony dependency" as a UX-cliff. For
  accessibility users, "go subscribe to the Steam Workshop item"
  is exactly the kind of step CAMM is supposed to eliminate.
- **Some install-time work doesn't fit any declarative shape.** The
  Civ V Access test report flagged that adopters upgrading from a
  pre-CAMM `deploy.ps1` install would see CAMM's `BackupAndReplace`
  misidentify the existing proxy DLL as vanilla. Migration logic
  is per-adopter and arbitrary.

## Goals

- Adopters can declare external-mod dependencies on the manifest,
  and CAMM will check / prompt / fetch / extract them at install
  time.
- Adopters can run arbitrary async C# before payload extraction
  without forking CAMM.
- Dependencies are tracked per-install so CAMM knows what it
  deployed.
- User consent is always explicit. No silent network fetches.
- All failure modes are user-visible and retryable.

## Out of scope (for v0.4)

| Item | Why deferred |
|---|---|
| Managed dependency updates | v0.4 is install-time only. Users keep whatever version was installed; Workshop-managed deps update via Workshop, GitHub-fetched deps stay put until manual update. |
| Dependency version constraints | No "Harmony >= 2.3" semantics. Whatever's on the dep's Latest pointer is what gets installed. |
| Dependency uninstall | When the adopter mod uninstalls, CAMM leaves deps in place — they're shared resources another mod may need. Documented. |
| Non-GitHub-Releases dep sources | v0.4 only fetches from GitHub Releases. Workshop / direct-URL / bundled-with-adopter sources require PreInstallHook + adopter-written logic. |
| `camm new` scaffolding tool | Both v0.3 test reports cited this. Deferred to v0.5+. |

---

## API surface

### `ModDependency` record (new)

```csharp
namespace Camm;

public sealed record ModDependency(
    string Name,                       // identifier ("brrainz.harmony")
    string DisplayName,                // user-facing ("Harmony")
    string GitHubReleasesOwner,        // "pardeike"
    string GitHubReleasesRepo,         // "HarmonyRimWorld"
    string AssetNamePattern,           // "Harmony-{0}.zip" — {0} = tag
    Func<string> InstallPath,          // directory the dep extracts into
    string SentinelFileName)           // proves the dep is installed
{
    // When the zip wraps content in a top-level directory (the
    // common GitHub Release shape — "HarmonyRimWorld-2.3.3/..."),
    // strip that prefix during extraction. "*" means "whatever the
    // first directory in the zip is." null means extract as-is.
    public string? ZipRootStripPrefix { get; init; }
}
```

### `DependencyInstallManifest` class (new)

Persisted to `%LocalAppData%\<adopter>\dep-<Name>.json`. Same shape
as `PayloadInstallManifest`. Not used by uninstall in v0.4 (deps
survive adopter-uninstall by design) but available for v0.5 if we
revisit:

```csharp
public sealed class DependencyInstallManifest
{
    public string DependencyName { get; set; } = "";
    public string InstalledVersion { get; set; } = "";  // GitHub tag
    public string InstallPath { get; set; } = "";
    public List<string> Files { get; set; } = new();
    public string SourceAssetUrl { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}
```

### `CammModManifest` additions

```csharp
public IReadOnlyList<ModDependency>? Dependencies { get; init; }
public Func<Task>? PreInstallHook { get; init; }

public bool HasDependencies =>
    Dependencies is { Count: > 0 };
```

Both nullable / optional. Existing adopters get identical behavior.

### `Camm.DependencyInstaller` (new)

```csharp
public static class DependencyInstaller
{
    public static async Task EnsureAsync(
        ModDependency dep,
        Action<string> log,
        Action<string> speak,
        CancellationToken ct = default);
}
```

Behavior:

1. **Sentinel check.** If `InstallPath()/SentinelFileName` exists,
   log "Dependency X already present at <path>" and return.
   Idempotent — present sentinel means done.
2. **Fetch release metadata.** One GET to the GitHub Releases API
   for the dep's owner/repo. Find the asset matching
   `AssetNamePattern`. If no match, error and prompt user.
3. **Consent prompt** (TaskDialog). Three buttons:
   - **Install \<DisplayName\>** (default): download + extract.
   - **Skip — I'll install it manually.** Continue install without
     the dep. User-visible "may not function until installed
     separately" warning. Logged so support can correlate later.
   - **Cancel install.** Throw `OperationCanceledException` →
     `Installer.ApplyInstall` aborts.
4. **Download.** Stream the asset to a temp file. Show "Downloading
   __DEPENDENCY_DISPLAY_NAME__..." status via Tolk.
5. **Extract.** Zip → `ZipArchive` with optional `ZipRootStripPrefix`
   handling. Bare DLL → file copy. Other extensions → throw with a
   clear error (v0.4 supports zip and bare DLL only).
6. **Persist manifest.** Write `DependencyInstallManifest` to
   `%LocalAppData%\<adopter>\dep-<dep.Name>.json`.
7. **Failure recovery.** Network errors, hash mismatches, extraction
   failures all show a retry/skip/cancel dialog.

### `Camm.Installer.ApplyInstall` modifications

New flow (additions in **bold**):

1. Copy launcher exe → install dir.
2. Extract Tolk sidecars → install dir.
3. **`PreInstallHook?.Invoke()`** (NEW — throw aborts install).
4. **Foreach `Dependency`: `DependencyInstaller.EnsureAsync(dep)`**
   (NEW — may prompt user, may abort).
5. Foreach `ModPayload`: `ExtractTo(payload)`.
6. Register IFEO (launcher mode only).
7. Register Apps & Features.
8. `PostInstallHook?.Invoke(installedPayloads)` (unchanged v0.3.0).
9. Speak "installed".

PreInstallHook runs **before** dependency check so an adopter can,
e.g., create the destination directory the dependency check will
target.

---

## Behavior specification

### Sentinel check semantics

`File.Exists(Path.Combine(InstallPath(), SentinelFileName))` —
sub-paths inside `SentinelFileName` are honored (same convention
as `ModPayload.SentinelFileName`). Empty `SentinelFileName` is
invalid; ModDependency throws at construction.

### Consent prompt

Standard CAMM TaskDialog. Body text:

> **\<DisplayName\>** needs \<DependencyDisplayName\> to function.
>
> Download \<DependencyDisplayName\> from GitHub
> (\<owner\>/\<repo\>) and install it now? Approximate download
> size: \<size\>. You only need to do this once.

Prompt fires once per install pass per missing dep. Once a dep is
on disk, future install runs skip the prompt (sentinel detection).

### Zip extraction

`AssetNamePattern` ending in `.zip` → use
`System.IO.Compression.ZipArchive`.

`AssetNamePattern` ending in `.dll` → file-copy to
`InstallPath()/<filename>.dll`. Single-file deps.

Other extensions → throw with a clear error. v0.4 supports zip
and bare DLL only; .7z / .tar.gz / .msi / etc. require a future
release.

`ZipRootStripPrefix` handling:
- Literal string (e.g. `"HarmonyRimWorld-2.3.3"`) → strip that
  exact prefix from every zip entry's path.
- `"*"` → strip whatever the first directory of the first entry
  is. Handles the common "zip-wraps-content-in-tag-named-dir"
  shape regardless of version.
- `null` → extract as-is, preserving any top-level zip folders.

### PreInstallHook contract

`Func<Task>` — no arguments. Runs after Tolk extract, before
dependency-fetch.

Throws → install fails (wizard's Done page shows FailureBody, same
as any other ApplyInstall exception).

Adopter is responsible for:
- **Idempotency.** Hook may run repeatedly (re-install, repair).
- **Side-effect tracking.** CAMM doesn't track what PreInstallHook
  writes; uninstall doesn't unwind it.
- **Logging** via `Camm.Logger.Info` / `Camm.Logger.Warn`.

`CammHost.Manifest` is available from within the hook, so the hook
can read the manifest if it needs to.

### Network failure handling

Download / metadata-fetch failures show retry/skip/cancel:
- **Retry** — re-fetch.
- **Skip** — same as the consent-prompt skip path.
- **Cancel** — abort the install.

No silent retries. No silent fallbacks. The user always knows
when a dependency is missing.

### Dependency timing relative to elevation

`ApplyInstall` runs inside the elevated (UAC-prompted) process.
Dependencies extract under that elevation. For deps that target
`%LocalLow%\...` (Harmony in the RimWorld case), this means
Administrator owns the resulting files — same as how the adopter's
own payload extracts today. Consistent with v0.3.x behavior.
Revisit in v0.5+ if file-ownership becomes a real pain.

---

## File-by-file changes

### New files

- **`Camm/ModDependency.cs`** — the record + `DependencyInstallManifest`
  class + AOT-clean `JsonSourceGenerationContext`. ~80 LOC.
- **`Camm/DependencyInstaller.cs`** — `EnsureAsync` + the
  zip-extract / single-DLL handlers + the TaskDialog prompts.
  ~200 LOC.

### Modified files

- **`Camm/CammModManifest.cs`** — add `Dependencies` +
  `PreInstallHook` + `HasDependencies`. Update mode-selection
  docstring at top to mention dependencies are optional in every
  mode.
- **`Camm/Installer.cs`** — wire `PreInstallHook` + dependency loop
  into `ApplyInstall`. ~30 lines added.
- **`Camm/lang/en.json`** — new locale keys (below).
- **`docs/manifest-reference.md`** — document the new fields. Update
  the cheat sheet (`Dependencies` / `PreInstallHook` rows).
- **`docs/getting-started.md`** — add a section parallel to the
  existing "Optional v0.3.0 fields" for v0.4.
- **`README.md`** — update "What CAMM provides" to mention
  dependency install. Update Status to v0.4.0.
- **`CHANGELOG.md`** — v0.4.0 entry.

### Locale keys (lang/en.json)

```json
"Dependency.Prompt.Title": "__DISPLAY_NAME__ — Required component",
"Dependency.Prompt.Instruction": "__DISPLAY_NAME__ needs __DEPENDENCY_DISPLAY_NAME__ to function.",
"Dependency.Prompt.Content": "Download __DEPENDENCY_DISPLAY_NAME__ from GitHub and install it now? You only need to do this once.",
"Dependency.Prompt.InstallButton.Heading": "Install __DEPENDENCY_DISPLAY_NAME__",
"Dependency.Prompt.InstallButton.Note": "Download and install automatically (about __DEPENDENCY_SIZE__).",
"Dependency.Prompt.SkipButton.Heading": "Skip — I'll install it manually",
"Dependency.Prompt.SkipButton.Note": "__DISPLAY_NAME__ may not function until __DEPENDENCY_DISPLAY_NAME__ is installed separately.",
"Dependency.Prompt.CancelButton.Heading": "Cancel install",
"Dependency.Prompt.CancelButton.Note": "Don't install __DISPLAY_NAME__ either.",
"Dependency.Downloading": "Downloading __DEPENDENCY_DISPLAY_NAME__...",
"Dependency.Installing": "Installing __DEPENDENCY_DISPLAY_NAME__...",
"Dependency.Installed": "__DEPENDENCY_DISPLAY_NAME__ installed.",
"Dependency.Skipped": "__DEPENDENCY_DISPLAY_NAME__ was skipped. __DISPLAY_NAME__ may not function until it is installed separately.",
"Dependency.DownloadFailed.Title": "__DEPENDENCY_DISPLAY_NAME__ download failed",
"Dependency.DownloadFailed.Content": "Could not download __DEPENDENCY_DISPLAY_NAME__: __ERROR_MESSAGE__"
```

Adds two new substitution tokens to `Strings.Substitute`:
`__DEPENDENCY_DISPLAY_NAME__` and `__DEPENDENCY_SIZE__`. Both
populated at prompt-render time (per-dependency, scoped to the
prompt).

---

## Adopter migration

For an existing v0.3.x adopter, upgrading is a submodule bump and a
rebuild — no Program.cs changes required.

To opt in to a dependency:

```csharp
Dependencies = new[]
{
    new ModDependency(
        Name: "brrainz.harmony",
        DisplayName: "Harmony",
        GitHubReleasesOwner: "pardeike",
        GitHubReleasesRepo: "HarmonyRimWorld",
        AssetNamePattern: "Harmony-{0}.zip",
        InstallPath: () => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "Ludeon Studios",
            "RimWorld by Ludeon Studios", "Mods", "Harmony"),
        SentinelFileName: "About/About.xml")
    {
        ZipRootStripPrefix = "*",
    },
},
```

To opt in to a PreInstallHook:

```csharp
PreInstallHook = async () =>
{
    // Detect pre-CAMM deploy.ps1 state and clean it before
    // BackupAndReplace runs (Civ V Access migration scenario).
    var staleBackup = Path.Combine(GameDir, "lua51_original.dll");
    if (File.Exists(staleBackup))
    {
        Camm.Logger.Info("Detected deploy.ps1 backup; restoring vanilla.");
        File.Move(staleBackup, Path.Combine(GameDir, "lua51_Win32.dll"),
            overwrite: true);
    }
    await Task.CompletedTask;
},
```

---

## Test plan

### Acceptance test 1 — RimWorld Access with Harmony as a declared dep

Re-run `docs/migration-test-prompts/rimworld-access-prompt.md`
against v0.4. Prompt should mention `Dependencies` +
`PreInstallHook` as available. Expected adopter behaviors:

- Manifest's `Dependencies` lists `brrainz.harmony`.
- `--install` (real, not `--wizard-test`):
  - Consent prompt fires → user accepts → Harmony downloads →
    extracts to RimWorld's LocalLow Mods folder.
  - Sentinel `About/About.xml` is present after the install.
- A second `--install` (repair) skips the dependency fetch (sentinel
  check trips first).
- PostInstallHook's `ModsConfig.xml` edit adds `brrainz.harmony`
  before `shane12300.rimworldaccess` (load order matters).
- Uninstall removes RimWorld Access files but leaves Harmony in
  place. User opens Settings → Apps to confirm only RimWorld Access
  is gone.

### Acceptance test 2 — Civ V Access pre-CAMM migration via PreInstallHook

Re-run `docs/migration-test-prompts/civ-v-access-prompt.md` against
v0.4 with the new PreInstallHook framing. Expected adopter:

- PreInstallHook detects `deploy.ps1`-shaped artifacts
  (`lua51_original.dll`, the sibling `Assets\DLC\DLC_CivVAccess.backup\`
  directory).
- If found, restores vanilla state before CAMM's `BackupAndReplace`
  runs.
- Subsequent install proceeds normally; uninstall correctly restores
  vanilla.

### Smoke test — existing v0.3.x adopters unchanged

`civ-vi-access`'s `CivViAccess` project builds clean against v0.4
with no `Program.cs` changes. `--version`, `--wizard-test`,
`--install` all behave identically to v0.3.1.

---

## Open questions

Items where I have a recommendation but want a second opinion
before locking in:

1. **`--dependency-status` flag** — prints what's installed / missing
   for every declared dependency, no side effects. Useful for users
   and for diagnostics. Adds one more arg to `CammHost`'s dispatch.
   *Recommendation: yes, cheap and adds value.*

2. **`ZipRootStripPrefix` default.** "*" is the common case for
   GitHub Releases zips. Should we default to "*", or keep `null`
   for the safer "extract as-is" default?
   *Recommendation: keep `null` default (explicit > implicit for
   file-layout decisions).*

3. **`__DEPENDENCY_SIZE__` token.** Requires fetching release
   metadata BEFORE prompting. One extra HTTP request, fast (cached
   by the dep manifest). Or prompt first and fetch on accept.
   *Recommendation: fetch first so the prompt has accurate size.*

4. **PreInstallHook signature.** Should it receive a context object
   (manifest reference, install state, etc.)? Or stay
   parameterless? `CammHost.Manifest` is statically accessible.
   *Recommendation: parameterless. Keep the surface minimal.*

5. **Dep install timing relative to elevation.** v0.4 extracts deps
   inside the elevated install process — Administrator-owned files
   under `%LocalLow%`. Consistent with how payloads work today.
   Should we de-elevate for dep extraction? Adds complexity.
   *Recommendation: leave as-is for v0.4; revisit if real pain.*

---

## After v0.4 lands

The two v0.3 dual-track test gaps not addressed by v0.4:

- **`camm new` scaffolding tool.** Both reports cited this as the
  highest-leverage adopter UX improvement after the docs themselves.
  Estimated cost: a `dotnet new` template with `--display-name` /
  `--target-game` / `--mode` switches. Worth a v0.5 milestone.
- **`--wizard-test --dump-strings`** flag. Lets automated tests
  verify locale-token substitution without driving the WinForms
  surface. Small. Could land in v0.4.x if convenient.

Stretch goal for v0.5 or later: managed dependency *updates*. v0.4
installs whatever's Latest at install time; v0.5+ could check for
newer dep versions on every game launch alongside the adopter-mod
self-update.
