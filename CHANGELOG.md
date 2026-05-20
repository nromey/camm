# Changelog

Reverse-chronological. Dates are when work landed.

## Versioning

`0.0.x` for the initial extraction from `civ-vi-access`. `0.1.0` is
reserved for the point where `CammHost.RunAsync` lands (full routing
inside CAMM, consumer's Program.cs is a few lines) and a second mod
can consume CAMM without reading the civ-vi-access source. Pre-1.0
means any release can break API; consumers pin to a tag SHA and
upgrade when ready.

## 0.5.6 — 2026-05-19 — Replace coalesce with identical-text dedupe

v0.5.4/v0.5.5's deferred-pending coalesce window broke normal
interaction. The window held the latest interrupt-mode text and
replayed it 150ms later — but when the user pressed Down arrow then
Alt+V quickly, the verbosity toggle's "Verbose off" got stomped by
the next arrow announce that arrived within the window, so they
heard the arrow announce twice and the toggle confirmation never.

Replaced with a simpler identical-text dedupe: if the exact same
text was sent within IdenticalDedupeWindow (250ms), drop the
duplicate. Different text always reaches Tolk immediately, so
Tolk's natural last-write-wins handles rapid different-event
interrupts (Down → Alt+V works; the toggle's announce interrupts
the arrow announce just like before v0.5.4).

The original "sticky Alt+V" symptom (rapid Alt+V Alt+V) is
unaffected — Tolk will speak partial-first + full-second of the two
different "Verbose off" / "Verbose on" texts, and the user hears
the final state which matches the toggled reality. Future work to
make rapid toggling more audible-distinct will use earcons
(distinct enable/disable sounds via ElevenLabs-generated samples or
the wav-synth pattern) so we stop trying to solve a UX problem
inside the speech race.

Timer + pending-slot machinery removed. Net diff: -50 lines, no new
fields beyond `_lastSpokenText` / `_lastSpokenAt`.

## 0.5.5 — 2026-05-19 — Fix v0.5.4 build break (Timer disambiguation)

v0.5.4's coalesce-window implementation referenced `Timer` unqualified.
Because Camm.csproj has `<UseWindowsForms>true</UseWindowsForms>` for
the install wizard, that name was ambiguous against
`System.Windows.Forms.Timer` and broke local AOT publish. CI for both
the CAMM tag and the CivViAccess v0.3.6 consumption tag failed.

This release qualifies the `Timer` reference as `System.Threading.Timer`
explicitly. No behavior change vs the intended v0.5.4; the coalesce
window operates identically once it compiles.

Translators: no LOC changes since v0.5.4 (`Speech.UpdateMod` is still
the active key).

## 0.5.4 — 2026-05-19 — Rapid-interrupt coalesce + shorter update speech

Two fixes that surfaced during CivViAccess AdvancedSetup verbosity-
toggle testing.

### Rapid-interrupt coalesce window (the "sticky Alt+V" fix)

`AccessibleOutputHandler` adds a coalesce window around its Tolk
output. Tolk's `Output(text, interrupt=true)` is last-write-wins —
a second interrupt call within tens of milliseconds of the first
silences whatever was just queued, before any audible part plays.
This surfaced as "sticky Alt+V" in CivViAccess: rapidly toggling
verbosity twice produced silence even though the state correctly
cycled, because the second interrupt killed the first announcement.

The fix: after firing an interrupt-mode `Tolk.Output`, lock out
subsequent interrupt-mode calls for 150ms. Calls arriving within
the window are held as a "pending" slot and the latest one wins;
a `Timer` fires at the end of the window to play whatever was
pending. Single utterances pass through immediately; rapid bursts
collapse to first-played + last-pending so the final state is
always audible. Non-interrupt calls bypass the coalescer (they
queue naturally in Tolk's pipeline and don't trigger the swallow).

All speech now funnels through a single `SpeakWithCoalesce`
chokepoint, so the protection applies to both direct `Speak()`
callers and `OutputMessage`'s per-line dispatch.

### Shorter update-speech utterance

`Speech.UpdateToVersionPrefix` / `Speech.UpdateToVersionSuffix` LOC
keys are replaced with a single `Speech.UpdateMod = "Updating mod"`.
The original "Updating to version X.Y.Z." routinely got cut off by
the next speech event before Tolk could finish; the new utterance
fits within the window. The version number is still visible in the
launcher console output and Lua.log for users who want the exact
value.

Adopters with non-English `lang/<locale>.json` files should update
`Speech.UpdateToVersionPrefix` / `Speech.UpdateToVersionSuffix` →
`Speech.UpdateMod` accordingly. No other API surface change.

## 0.5.3 — 2026-05-18 — Speech-pipeline fixes discovered under load

Bundled fixes from a multi-hour CivViAccess AdvancedSetup test session
where three latent issues compounded into "no speech at all from the
installed launcher":

### Logger no longer truncates per session

`Logger.StartSession` previously did `File.WriteAllText(LogPath, "")`
on every launch. When the IFEO-redirected child launcher (Process B)
started its session, it wiped the parent launcher (Process A)'s
in-flight log. The interesting log was always Process A's (full game
lifecycle, log-tail, all speech), so diagnostic work was effectively
blind. Now appends with a session banner; the file grows but the
write rate is negligible (~one session per game launch).

### Tolk DLLs are force-overwritten on install

`TolkBootstrap.ExtractTo` had a "skip if same-size file already there"
optimization. Tolk version updates can ship same-size binaries, so
when a user upgraded their launcher exe on top of an older install,
ExtractTo skipped writing the new DLLs and the new launcher loaded
the old Tolk. `Tolk.IsLoaded` returned true (old DLL loaded fine),
`HasSpeech` returned true (old DLL's SAPI probe worked), but
`Tolk.Output` calls silently produced no audible speech because of
ABI drift between the new launcher's expectations and the old DLL.
Always overwrite now — the per-pid temp dir case is unaffected (fresh
dir each time), the install case is what this fixes.

### Transparent-invocation re-entry no longer breaks log-tail

v0.5.2's unconditional short-circuit fired for EVERY transparent
invocation, including the legitimate first one (IFEO redirect from the
user-launched `CivilizationVI.exe`). Process A short-circuited and
exited, so log-tail never started — the user heard zero in-game
speech even though the mod was emitting `#SCREENREADER` lines fine.

Correct rule: the first launcher to acquire the single-instance mutex
runs the full lifecycle (log-tail + game wait), regardless of whether
it's transparent-invoked. Subsequent launchers (mutex contended)
check whether they're transparent — if yes, bypass-spawn the IFEO
target and exit silently so the game's internal exe chain
(`CivilizationVI.exe -> CivilizationVI_DX12.exe`) keeps working
without firing a confusing "another launcher running" announcement
mid-game-startup.

Mutex acquire signature picked up a `speakIfContended` parameter so
the transparent re-entry path can suppress the spoken warning while
still using the same mutex.

## 0.5.2 — 2026-05-18 — Transparent invocation short-circuit in launcher mode

Bug fix paired with v0.5.1: the mutex caught user-initiated launcher
dups, but it did NOT correctly handle the case where the game's own
internal process tree re-fires IFEO. Civ-VI-style games (and likely
others) launch an internal child exe at runtime — Civ VI bootstrap
`CivilizationVI.exe` spawns `CivilizationVI_DX12.exe` for the actual
DX12 game process. When both exes are registered in IFEO, the
child spawn re-fires IFEO and Windows starts a second launcher
process with the child exe path as `args[0]`.

In launcher mode pre-v0.5.2, that second launcher ran the full flow
including the log-tail loop, meaning two launchers tailed the same
`Lua.log` and every screen-reader announcement was routed to Tolk
twice. v0.5.1's mutex would catch that on the second launcher's
acquire, but blocking it would also prevent it from spawning the
IFEO-targeted child exe — the game itself would fail to launch.

### Fix

Launcher mode now mirrors installer-only mode's transparent-
invocation handling: if `args[0]` is an IFEO-registered exe path,
spawn it via `ProcessLauncher.LaunchBypassingIfeo` and exit
immediately. No mutex acquire, no log-tail loop, no lifecycle
wait — the user-launched parent launcher already owns all of
that for the whole game session.

Placement: short-circuit is BEFORE the mutex check, so
transparent invocations never try to acquire. The mutex remains
the right defense for genuinely-duplicate user-initiated launches
(double-clicked shortcut, leaked self-update relaunch).

## 0.5.1 — 2026-05-18 — Single-instance launcher gate

Bug fix discovered during a CivViAccess screen-reader test session.
Two CAMM launcher processes can end up running against the same
adopter (a self-update relaunch that doesn't fully terminate the
prior process, a stale instance from a previous run that never
exited cleanly, etc.). Both processes tail the game's Lua.log and
both route every marker-prefixed line to Tolk independently, so the
user hears every announcement echoed ~100 ms apart. The second
fire interrupts the first mid-word, which combined with fast
keyboard nav produces unintelligible truncated readouts ("culture
victory, c", "diplomatic victory, c", "religious victory, ch") and
makes the screen reader experience unusable.

### Mutex-based single-instance gate

Right before the launcher would spawn the game (launcher mode), CAMM
now tries to acquire a named per-session mutex
(`Local\Camm.SingleInstance.<LocalAppDataFolderName>`). The first
launcher gets it and proceeds; any later launcher invocation finds
the mutex held, speaks `Speech.AnotherLauncherRunning`, and exits
with code 3. Mutex releases on process exit (registered ProcessExit
handler). `AbandonedMutexException` is treated as "we got it" so a
single crash never permanently locks the user out.

Placement notes:
- Per-session scope (`Local\`) rather than system-wide (`Global\`):
  different desktop sessions can each have their own running
  launcher.
- Keyed off `LocalAppDataFolderName` so future CAMM consumers
  (Factorio adopter etc.) get their own mutex name and can coexist.
- Acquired AFTER args-dispatch routes (`--install` / `--uninstall`
  / `--version` / `--config` / `--wizard-test`) so those one-shot
  operations still run alongside an active launcher.
- Installer-only mode skipped: that mode's process is a short
  update-check-then-spawn-and-exit, so concurrent invocations are
  expected and benign.

### New string

- `Speech.AnotherLauncherRunning` — spoken when the second launcher
  invocation is refused.

## 0.5.0 — 2026-05-18 — Update-only IFEO + installer-only example

Closes the gap the v0.4.0 RimWorld Access AI-readability test
caught: the README pitched "auto-update on every game launch via
IFEO" as CAMM's irreducible value, but installer-only mode didn't
register the IFEO redirect, so updates only fired when the user
manually re-ran the installer. v0.5.0 fixes the actual behavior
gap, not just the docs.

### Update-only IFEO for installer-only mode

`IfeoTargetExeNames` is now optional in installer-only mode instead
of strictly null. When an installer-only manifest sets it, CAMM:

  * Registers the IFEO redirect on the named game executables at
    install time (same code path as launcher mode — no new
    registration logic).
  * On every game launch, runs the launcher exe just long enough to
    apply any pending update from GitHub Releases, then spawns the
    real game via `ProcessLauncher.LaunchBypassingIfeo` and exits
    immediately. No log-tail loop, no foreground handoff, no
    lifecycle wait — the user experiences the game launch as if
    CAMM weren't there.

Opt in by adding one line to the manifest:

```csharp
IfeoTargetExeNames = new[] { "YourGame.exe" },
```

No new manifest fields. The combination of `GameInstance` null +
`IfeoTargetExeNames` non-empty IS the signal. Exposed as the
derived property `manifest.UpdateOnlyIfeoEnabled` for diagnostics.

`GameProcessNames` stays null in this mode — CAMM doesn't wait for
the game's lifecycle, so it doesn't need to know what process to
watch.

### docs/examples/installer-only-minimal/

New directory. Three reference files (Program.cs, installer.csproj,
app.manifest) showing the smallest possible installer-only adopter
shape, with v0.4 / v0.5 opt-in fields documented inline as
commented-out blocks. Closes the doc gap both v0.4 test reports
flagged: "no end-to-end installer-only example exists in the docs
themselves (only the launcher-mode civ-vi-access reference adopter
on GitHub)."

### Behind the scenes

* `CammHost.RunAsync` gains a transparent-invocation branch inside
  installer-only mode: if the launcher was invoked via IFEO and a
  game path is present, spawn the game and exit; otherwise fall
  through to the existing "install / update complete" exit.
* The "transparent invocation (launcher mode only)" comment block
  is updated — the detection now applies to any mode that sets
  `IfeoTargetExeNames`.
* `CammModManifest`'s top docstring expands to four modes
  (launcher with log-tail, launcher without log-tail, installer-only,
  installer-only with update-on-launch IFEO).
* `docs/manifest-reference.md` mode-selection cheat sheet gains a
  fourth column for the new sub-mode.

### What's NOT in v0.5 (deferred)

* **`camm new` scaffolding tool.** Cited by v0.3 and v0.4 test
  reports. The new `docs/examples/installer-only-minimal/` plus the
  existing civ-vi-access reference adopter cover most of the same
  territory. Revisit if real adopters still feel the friction.
* **Manifest validator** (`CammHost.Validate(manifest)`) — flagged
  by the v0.4 Civ V Access report for catching generic-looking
  sentinel filenames, hooks that mutate without dry-run, etc.
  Defer to v0.6+ when there's a concrete failure mode to validate
  against rather than speculative heuristics.
* **Documented dependency asset patterns** (Harmony, BepInEx,
  MelonLoader, IPA) — flagged by the v0.4 RimWorld report. The
  example `Program.cs` shows the Harmony pattern; rest can land
  in the post-v0.5 docs review.

## 0.4.0 — 2026-05-18 — Dependencies + PreInstallHook

Two API-additive feature additions driven by the v0.3.0 dual-track
AI-readability test gaps. Existing v0.3.x adopters upgrade by
bumping the submodule SHA and rebuilding; no `Program.cs` changes
required.

### `Dependencies` on `CammModManifest`

A new `IReadOnlyList<ModDependency>?` field declaring external
mods the adopter requires (Harmony for RimWorld, BepInEx for
Unity-based games, MelonLoader for Mono games, IPA for Beat
Saber, anything else that follows the "in-process mod loader
needs a bootstrap layer" shape). Each `ModDependency` is:

    new ModDependency(
        Name: "brrainz.harmony",
        DisplayName: "Harmony",
        GitHubReleasesOwner: "pardeike",
        GitHubReleasesRepo: "HarmonyRimWorld",
        AssetNamePattern: "Harmony-{0}.zip",
        InstallPath: () => @"C:\...\Mods\Harmony",
        SentinelFileName: "About/About.xml")
    { ZipRootStripPrefix = "*" }

At install time, `Installer.ApplyInstall` checks each dependency's
sentinel. Missing → prompt user (Install / Skip / Cancel) → fetch
latest release metadata from
`api.github.com/repos/<owner>/<repo>/releases/latest` → download
the asset matching `AssetNamePattern` → extract.

Extraction supports `.zip` (with optional `ZipRootStripPrefix` for
GitHub's common "wrap content in a tag-named folder" shape, where
`"*"` strips whatever the first directory turns out to be) and
bare `.dll` (single-file deps). Other formats fail with a clear
error in v0.4.

Per-dep manifest persisted at
`%LocalAppData%\<adopter>\dep-<Name>.json` with installed version,
file list, and source URL. Not used for uninstall in v0.4 (deps
survive adopter-mod uninstall by design — they're shared resources)
but available for future versions (managed dependency updates,
`--dependency-status` diagnostics).

Network and extraction failures show a retry / skip / cancel
dialog. No silent fallbacks; the user always knows when a
dependency is missing.

User consent is mandatory. CAMM never silently network-fetches a
dependency. The prompt shows download size pulled live from the
GitHub Releases API.

`DependencyInstaller` is a new public class. `ModDependency` and
`DependencyInstallManifest` are new public types. `Strings.Get`
gains three substitution tokens: `__DEPENDENCY_DISPLAY_NAME__`,
`__DEPENDENCY_SIZE__`, `__ERROR_MESSAGE__`. Locale catalog gains
the `Dependency.*` keys for the prompts and status messages.

### `PreInstallHook` on `CammModManifest`

Symmetric partner to v0.3.0's `PostInstallHook`. `Func<Task>?`
that runs after launcher-exe + Tolk extraction, before dependency
installation, before payload extraction.

Use cases:

  * Migrating from a pre-CAMM deployed state (e.g. Civ V Access's
    `deploy.ps1` artifacts — detect `lua51_original.dll` and the
    sibling-directory engine backup, restore vanilla state so
    CAMM's `BackupAndReplace` doesn't misidentify the proxy DLL as
    vanilla on first install).
  * Fetching a non-GitHub-Releases dependency. `Dependencies`
    covers GitHub Releases sources; PreInstallHook covers
    everything else (Workshop subscription URLs, direct downloads,
    bundled-with-adopter zips).
  * Transforming a config file before payloads land.

Throws → install fails (wizard's Done page shows FailureBody).
Idempotent / safe to re-run is the adopter's responsibility —
re-install / repair will call it again.

`CammHost.Manifest` is statically available from inside the hook.

### `Installer.ApplyInstall` flow

New step ordering:

  1. Copy launcher exe → install dir (unchanged).
  2. Extract Tolk sidecars → install dir (unchanged).
  3. **`PreInstallHook?.Invoke()`** (NEW).
  4. **Foreach `Dependency`: `DependencyInstaller.EnsureAsync(dep)`**
     (NEW).
  5. Foreach `ModPayload`: `ExtractTo(payload)` (unchanged).
  6. Register IFEO (launcher mode only) (unchanged).
  7. Register Apps & Features (unchanged).
  8. `PostInstallHook?.Invoke(installedPayloads)` (unchanged
     v0.3.0).
  9. Speak "installed" (unchanged).

`OperationCanceledException` from PreInstallHook or
DependencyInstaller bubbles out and aborts the install — wizard
shows the cancellation cleanly, nothing partial-state.

### What's NOT in v0.4 (deferred)

* **Managed dependency updates.** Install-time presence check
  only; deps stay at whatever version was installed.
* **Dependency version constraints.** No "Harmony >= 2.3"
  semantics — whatever's at the dep repo's Latest pointer is what
  gets installed.
* **Dependency uninstall.** Deps survive adopter-mod uninstall
  (shared resources another mod may need).
* **Non-GitHub-Releases dependency sources.** PreInstallHook
  covers these; `Dependencies` is GitHub-Releases-only for v0.4.
* **`camm new` scaffolding tool.** Cited by both v0.3 test
  reports; deferred to v0.5+.

### Docs

* `CAMM_V040_PLAN.md` (new at repo root) — the design doc this
  release was implemented against.

Full docs pass (README, getting-started, manifest-reference,
Status / cheat-sheet updates) lands as a follow-up commit, not in
the v0.4.0 tag itself.

### Test plan

* RimWorld Access dual-track migration test re-run with v0.4 to
  validate declarative `Dependencies` resolves the
  "manually-subscribe-to-Harmony" UX cliff.
* Civ V Access dual-track migration test re-run with v0.4 to
  validate `PreInstallHook` handles the pre-CAMM `deploy.ps1`
  migration cleanly.

## 0.3.1 — 2026-05-18 — app.manifest template fix (ship-stopper)

Critical patch. The `templates/app.manifest` file shipped in v0.2.1
contained a comment block with the literal strings `--version` and
`--config` inside `<!-- ... -->`. XML forbids `--` inside comments
(W3C XML 1.0 §2.5 — only the closing `-->` may contain it). MSBuild
embeds the malformed manifest into the EXE without complaint, but
Windows side-by-side activation rejects it at process startup with:

> The application has failed to start because its side-by-side
> configuration is incorrect.

The chained error in Event Viewer (`Activation context generation
failed ... Invalid Xml syntax`) is the only hint at the real cause.

The bug was caught by both v0.3.0 AI-readability test adopters
independently (Civ V Access + RimWorld Access). Both followed
`docs/getting-started.md` Step 3 verbatim ("copy
`camm/templates/app.manifest`"), both got an unrunnable binary, and
both diagnosed the cause from outside the CAMM toolchain (PowerShell
XML parse, Event Viewer log).

Fix: rewrote the trustInfo comment to describe day-to-day code paths
without `--` flag prefixes. Behavior of the manifest is unchanged;
the binary now starts.

`docs/getting-started.md` Step 3 also gains a one-paragraph warning
about the `--`-in-XML-comments rule, with a PowerShell `[xml]$x =
Get-Content` validation snippet adopters can wire into their build
as a pre-step.

`civ-vi-access/CivViAccess/app.manifest` was NOT affected (the
trustInfo block I added there in the v0.2.1 sync uses shorter
wording with no flag-prefix strings).

## 0.3.0 — 2026-05-17 — Adaptive launcher mode + post-install hook + backup/restore

API-additive minor bump driven by the same v0.2.0 test reports. The
through-line: every adopter has different needs for which CAMM
features they want; the framework should let them turn off the
pieces they don't need without losing the always-want pieces
(installer wizard, Apps & Features, auto-update). v0.3.0 makes
launcher mode adaptive (three modes instead of two), gives adopters
a way to run their own logic after install, and lets payloads that
overwrite vanilla files unwind themselves on uninstall.

### Three operating modes (`LogTailEnabled` derived property)

Launcher mode now subdivides into "with log-tail" and "without
log-tail" based on whether `Sanitizer` AND `MarkerProtocol` are
set. Civ VI Access (full log-tail) is unchanged. Civ V Access
(speech via in-process Lua proxy, no log file involvement) can now
adopt CAMM in launcher mode without writing two no-op seam classes
CAMM would never call.

CAMM detects "launcher mode without log-tail" at startup and skips
the log-file-watch loop while still doing IFEO redirect, game spawn,
foreground-handoff, lifecycle-wait, and closed announcement. The
`IGameInstance.GetLogFilePath()` method is only called when
`LogTailEnabled` is true; adopters without log-tail can return any
string from it.

The "Mode-selection cheat sheet" in `docs/manifest-reference.md`
gains a third column.

### `PostInstallHook` on the manifest

Optional `Func<IReadOnlyDictionary<string, PayloadInstallManifest>, Task>?`
field. Runs after all payloads have been extracted and Apps & Features
is registered, before the wizard's "install complete" announcement.
Receives the per-payload install manifests CAMM just wrote (file
lists, backup entries).

Use case: game-side config edits CAMM doesn't model. RimWorld's
`ModsConfig.xml` needs `<li>foo.bar</li>` added to enable a mod;
BepInEx has its own plugin enable list per game. Hook throws →
install fails (wizard shows FailureBody). Idempotent behavior is the
adopter's responsibility — install-over-install will call it again.

### `OverwriteStrategy` on `ModPayload`

New enum + init-only property:

```csharp
new ModPayload(...) { OverwriteStrategy = OverwriteStrategy.BackupAndReplace }
```

`Replace` (default, behavior unchanged) overwrites existing files in
the destination, deletes only the CAMM-installed files on uninstall.

`BackupAndReplace` is for payloads that overwrite files the game
shipped — Civ V Access's forked `CvGameCore_Expansion2.dll` engine
DLL, its `lua51_Win32.dll` Lua proxy. At extract time, CAMM renames
each existing target to `<filename>.original` before writing the new
content. At uninstall time, CAMM deletes the CAMM-installed file
and restores the `.original` rename, returning the game install to
its pre-CAMM state. The `.original` files are tracked in the payload's
install manifest under a new `backups` array.

The pre-CAMM state is captured at the FIRST `BackupAndReplace`
install (when `.original` doesn't yet exist). Subsequent installs
that find a pre-existing `.original` preserve it — they don't
overwrite the user's actual vanilla file with a CAMM-modified copy
from a prior install.

Without this, Civ V Access uninstall via CAMM would leave the user
with a missing engine DLL and a broken Lua scripting host — game
fails to launch. With this, CAMM-built mods can replace vanilla
files safely.

### Mode-aware locale variants

`Strings.Get(key)` now auto-prefers `<key>.InstallerOnly` over
`<key>` when `manifest.IsInstallerOnly` is true. The variant is
optional per-key — keys whose copy works in both modes don't need
duplicates.

Catalog additions (with `.InstallerOnly` variants where copy
differed):

- `Console.Title` / `Console.Title.InstallerOnly` —
  "Installer" vs "Launcher" in the console window title.
- `Console.Initializing` / `Console.Initializing.InstallerOnly` —
  startup line printed past the args dispatch.
- `About.ProductLabel` / `About.ProductLabel.InstallerOnly` —
  product label in `--version` output ("Civ VI Access Launcher 0.3.0"
  vs "RimWorld Access Installer 0.1.0").
- `Wizard.Welcome.Body.InstallerOnly` — installer-only wording
  ("copy the updater to Program Files" + payload-into-mod-folder)
  instead of launcher-mode wording ("copy the launcher to Program
  Files").
- `Installer.Uninstall.ConfirmContent.InstallerOnly` — drops the
  bullet about removing the IFEO launch redirect (false in
  installer-only mode).
- `Installer.Uninstall.CompleteBody.InstallerOnly` — drops the
  "Steam will launch directly again" wording (also false; the game
  always launched directly).

Fixes the test report's gap #4.2 ("uninstall wizard's body text
references IFEO / launch-redirect cleanup that didn't happen") for
installer-only adopters.

### Behind the scenes

- `PayloadInstallManifest` gains a `Backups` list (per-file
  rename-back instructions). Old v0.2.x manifest files deserialize
  cleanly into the new shape (empty Backups list).
- `Installer.ApplyInstall` collects each payload's install manifest
  into a dictionary and passes it to `PostInstallHook` if set.
- `CammHost.RunAsync` skips the log-file-watch tail when
  `LogTailEnabled` is false but still does IFEO + spawn + lifecycle.
- `Strings.Get` is mode-aware via `CammHost.Manifest.IsInstallerOnly`;
  reads from a try/catch so unit tests calling Strings without
  initializing the manifest still work.

## 0.2.1 — 2026-05-17 — Doc patches from v0.2.0 dual-track AI-readability tests

No API changes. Documentation + template patches addressing
adopter-discovered gaps from the v0.2.0 AI-readability acceptance
tests (Civ V Access launcher-mode test + RimWorld Access
installer-only-mode test).

- **`templates/app.manifest`** — new. Ships the canonical app.manifest
  with Common Controls v6, PerMonitorV2 DPI, Windows 10/11 supportedOS,
  AND an `asInvoker` `<trustInfo>` block. The trustInfo block prevents
  Windows' installer-detection heuristic from auto-elevating any exe
  whose filename contains `install` / `setup` / `update` / `patch` —
  which was a silent foot-gun for the RimWorld Access adopter who
  chose `RimWorldAccessInstaller.exe` and got an unrunnable binary.
- **`docs/getting-started.md`** — Step 3 references the template
  instead of telling adopters to "copy from civ-vi-access". New
  "Multi-source single-payload pattern" subsection explaining how
  to assemble a payload from multiple source dirs via per-file
  `<EmbeddedResource Include>` items + `Condition="Exists(...)"`.
  New "Adopting CAMM for a mod with an existing build pipeline"
  section covering the second-project shape (installer/ project +
  existing build, what to delete, what to keep). New "Bitness:
  x64 launcher + 32-bit game" section. New `dotnet publish`
  walkthrough including the `$(Configuration)` interaction with
  multi-source payloads. New FAQ entry on dev-mode walk semantics.
- **`docs/manifest-reference.md`** — LocalLow path example fixed
  (`"RimWorld" → "RimWorld by Ludeon Studios"`). New paragraph on
  `FolderName` walk semantics + not-found behavior (parent-walk +
  one-step-down, silent no-op on miss). New paragraph on
  `SentinelFileName` not-found behavior. New paragraph on
  `DefaultDestination` call timing (once per ExtractTo). New
  paragraph on auto-update fields + empty-releases-repo behavior.
- **`README.md`** — installer-only mode caveat added about
  game-side config not being modified (ModsConfig.xml, BepInEx
  plugin lists). Flags v0.3.0's coming post-install hook.

The `civ-vi-access` reference adopter's `app.manifest` was also
updated to match the new template (added the `<trustInfo>` block;
harmless when the exe filename doesn't trigger the UAC heuristic,
useful for any adopter that copies from civ-vi-access).

## 0.2.0 — 2026-05-17 — Multi-payload + installer-only mode

API-breaking minor bump. Two architectural changes plus a docs
overhaul driven by the v0.1.1 AI-readability acceptance test
(report at `civ-vi-access-2026-05-17-1735.md`).

### Multi-payload `ModPayloads` list

`CammModManifest`'s three singular `ModPayload*` fields collapse
into a required `IReadOnlyList<ModPayload>`. Each entry has its own
`Name` (embed-resource prefix), `FolderName` (dev-mode source
discovery), `SentinelFileName`, and `DefaultDestination`. Mods that
deploy to multiple destinations (Civ V Access: DLC package + proxy
DLL + engine fork) now adopt CAMM cleanly — one list entry per
destination, one `<EmbeddedResource>` glob per payload prefix.

Install / update / uninstall all iterate the list. Each payload
writes a per-install manifest at
`%LocalAppData%\<folder>\installed-<payload-name>.json` listing
every file it deployed. Uninstall reads each manifest and removes
exactly those files — safe across shared destinations (e.g. dropping
a single DLL into the game root, which mustn't be recursive-deleted).
Empty CAMM-owned subdirectories are cleaned after; foreign content
is preserved.

Civ VI Access (the test-bed consumer) adopts with a one-element
list, no behavioral change. Multi-root adopters get the new
capability for free.

### Installer-only mode

`IfeoTargetExeNames`, `GameProcessNames`, `GameInstance`,
`Sanitizer`, and `MarkerProtocol` are now nullable. When
`GameInstance` is null, CAMM detects "installer-only mode" and:

- Skips IFEO transparent-launch registration (the mod isn't a
  launcher-paradigm consumer; nothing to intercept).
- Skips log-tail speech routing (the mod's runtime is in-process,
  not log-emitting).
- Skips game-launch lifecycle (no `FindGameExe` / `GetLogFilePath`
  / `LaunchAnnouncement` / `ClosedAnnouncement` to call).
- Exits cleanly after install / update / uninstall flow completes.

Use case: Harmony-based in-game DLLs (RimWorld Access pattern),
BepInEx / MelonLoader plugins, any mod that lives inside the
target game's process. Adopters get install wizard + Apps & Features
+ GitHub Releases auto-update + signed-release pipeline; speech
routing stays the consumer mod's responsibility (Prism, in-process
Tolk, whatever).

### Auto-update opt-out

`GitHubReleasesOwner`, `GitHubReleasesRepo`, and
`LauncherAssetNamePattern` become nullable. Setting all three
enables auto-update; leaving any null disables the check. Removes
a real friction point for pre-release adopters who haven't stood up
a GitHub Releases pipeline yet.

### Docs overhaul

The v0.1.1 test report identified these gaps; all addressed:

- `README.md` — rewritten. The "v0.0.1, no public surface yet" text
  is gone. New top sections explain "Is CAMM right for your mod?"
  with launcher-mode vs. installer-only-mode guidance.
- `docs/getting-started.md` — new, ~400-line step-by-step adoption
  walkthrough. Covers clone-submodule → csproj boilerplate (TFM,
  WinForms flags, `<DefaultItemExcludes>` for non-flat repos,
  embedded-resource conventions per payload) → seam interface
  implementations (launcher mode only) → `Program.cs` (manifest
  + RunAsync) → smoke test. Has a "Common questions" FAQ.
- `docs/manifest-reference.md` — new. Every `CammModManifest` field
  documented with required/optional status, examples, and a
  mode-selection cheat sheet.
- Test prompts updated to reflect v0.2.0 API (multi-payload list,
  installer-only mode reference).

### Behind the scenes

- `IfeoInstaller`, `WindowFocusManager`, `Updater` accept
  null/empty manifest fields with empty-array / no-op fallbacks.
- `AccessibleOutputHandler.OutputMessage` early-returns if
  `Sanitizer` / `MarkerProtocol` are null (defensive — LogTailSpeaker
  wouldn't be started in installer-only mode anyway).
- `CammHost.RunAsync` rewritten: args dispatch is mode-agnostic;
  the game-launch / log-tail / lifecycle-wait tail only runs when
  `!manifest.IsInstallerOnly`.

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
