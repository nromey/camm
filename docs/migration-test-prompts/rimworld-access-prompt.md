# RimWorld Access → CAMM migration task

I want to convert the RimWorld Access mod
(https://github.com/aaronr7734/rimworld_access, also clonable at
`C:\dev\rimworld_access\` if you're on the right machine) to use the
CAMM framework (https://github.com/nromey/camm).

CAMM (Chameleon Access Mod Manager) is a reusable .NET 10
framework that provides install wizards, GitHub Releases auto-
update with stable / latest / off channels, Apps & Features
registration, and Azure Trusted Signing release pipeline. In
"launcher mode" CAMM also wires up IFEO transparent-launch, Tolk
speech routing of an in-game log file, and lifecycle handling for
the target game. In "installer-only mode" the IFEO / speech / log-
tail / lifecycle bits are skipped — CAMM is just a wizard +
installer + auto-updater for the mod's files.

The first launcher-mode adopter is Civ VI Access
(https://github.com/nromey/civ-vi-access). RimWorld Access is a
**different paradigm**: a Harmony in-game DLL on .NET Framework
4.7.2 using Prism for cross-platform screen-reader integration.
There's no separate launcher process to intercept the game launch
with, and the in-process Harmony DLL handles speech directly
through Prism — no log-tail bridge. So if CAMM applies to RimWorld
Access at all, it'll be in **installer-only mode**: the CAMM-built
launcher exe installs/updates/uninstalls the mod's files, and
once installed the user just runs RimWorld normally with the
Harmony DLL already in place.

## First task: assess architectural fit

Read in order:

1. CAMM's `README.md`. Pay attention to the "Is CAMM right for
   your mod?" section. RimWorld Access's situation matches the
   installer-only mode described there, but make your own call.
2. CAMM's `docs/getting-started.md` and `docs/manifest-reference.md`.
   The manifest-reference's "Mode-selection cheat sheet" shows which
   fields belong to installer-only mode (leave `GameInstance` /
   `IfeoTargetExeNames` / `GameProcessNames` / `Sanitizer` /
   `MarkerProtocol` null). Also note the v0.3.0 additions:
   `PostInstallHook` (optional async hook receiving per-payload
   install manifests, runs before "install complete" — useful for
   game-side config edits CAMM doesn't model, e.g. RimWorld's
   ModsConfig.xml), and mode-aware locale variants
   (`<key>.InstallerOnly` keys override the base key when in
   installer-only mode, addressing wizard copy that doesn't apply
   without an IFEO redirect).
3. Civ VI Access's source for reference shape (launcher mode). Your
   installer-only adopter will look similar in structure but
   thinner — only `Program.cs` + manifest, no `IGameInstance` or
   speech-bridge implementations.
4. RimWorld Access's codebase. Note:
   - The repo's `CLAUDE.md` describes the mod architecture (Harmony
     + Prism + .NET Framework 4.7.2 + auto-deploys to
     `$(RimWorldDir)\Mods\RimWorldAccess\`).
   - How the existing build/install process works (it builds the
     Harmony DLL + Prism native libs + About/ metadata, deploys to
     RimWorld's Mods folder).
   - What deploys where: the DLL, native libs, About/, sounds/,
     hooks/, etc.

Write a short fit-assessment that answers:

1. Does installer-only CAMM apply to RimWorld Access? Concretely:
   can CAMM v0.2.0's `ModPayloads` list deploy the right files to
   the right places? (RimWorld looks for mods under
   `%USERPROFILE%\AppData\LocalLow\Ludeon Studios\RimWorld by
   Ludeon Studios\Mods\<modname>\` OR
   `%STEAM%\steamapps\common\RimWorld\Mods\<modname>\` —
   pick one or document both.)
2. What does the resulting CAMM-based adopter look like?
   File count, LOC, csproj boilerplate, manifest field-by-field.
3. What doesn't apply (and should be null) — `IGameInstance`,
   `IfeoTargetExeNames`, `GameProcessNames`, `Sanitizer`,
   `MarkerProtocol`?
4. What benefit does CAMM provide RimWorld Access vs. its current
   "build with dotnet, auto-deploy via msbuild target" flow? Be
   honest — if the existing dev loop is already strong, the answer
   might be "limited, mostly auto-update for end users."

## Second task: produce the migration

If your fit-assessment says CAMM applies, work in a `worktree` so
you don't touch the user's working copy of RimWorld Access.
Produce a working installer-only CAMM-based adopter with these
acceptance criteria:

- A new launcher project (e.g. `installer/` or `RimWorldAccessInstaller/`)
  builds clean with `dotnet build` against CAMM as a submodule pinned
  to `v0.3.0`.
- `dotnet run -- --version` runs and reports sensible output
  (mod name, install state, channel, project URL).
- `dotnet run -- --wizard-test` opens the install wizard with
  RimWorld Access-specific text. The wizard's text should make
  sense even though there's no launcher relationship with the
  game itself.
- `Program.cs` builds the manifest with `GameInstance = null`
  (installer-only mode) and `ModPayloads` pointing at the
  RimWorld mod-folder content.
- The Harmony DLL build itself is **not migrated** — the existing
  rimworld_access.csproj that produces `rimworld_access.dll` stays
  as-is; the new installer project consumes that DLL as an
  embedded payload alongside Prism's native libs + About/.

Document any places where you had to guess (RimWorld Steam install
path, mod-folder location convention, Workshop vs local Mods/
divide, etc.).

## What you can ignore

- The Harmony patches themselves, the in-game UI hooks, the Prism
  speech integration — none of that changes. The Harmony DLL is
  just files CAMM deploys.
- Don't worry about NuGet versioning. CAMM is consumed via git
  submodule + `<ProjectReference>`, not NuGet.
- Don't worry about signing or GitHub Actions setup.
- Don't try to migrate the existing build pipeline. It stays. The
  new launcher project sits alongside the existing rimworld_access
  project; the launcher's csproj embeds the existing build's DLL +
  native libs + About/.

## Deliverable: report to disk

Write your final report to disk at this exact path:

    C:\dev\camm-test-reports\rimworld-access-<YYYY-MM-DD-HHMM>.md

Create the `C:\dev\camm-test-reports\` directory if it doesn't
exist. Replace `<YYYY-MM-DD-HHMM>` with the current date and time
so multiple runs don't overwrite each other — e.g.
`rimworld-access-2026-05-17-2230.md`.

The report must contain, as top-level sections:

1. **Fit assessment.** Full text of your architectural analysis
   from the first task. Don't summarize — paste the analysis
   verbatim.
2. **What I produced.** Concrete file paths and line counts.
   Reference the worktree path so the maintainer can inspect the
   build. If you decided CAMM doesn't apply, say so explicitly here
   instead.
3. **Gaps in CAMM's docs.** A numbered list of every place you had
   to guess, every claim CAMM made that turned out wrong or
   unclear, every spot where the README / getting-started /
   manifest-reference / civ-vi-access reference left you stuck. Be
   specific — quote the offending passage or note that no docs
   existed.
4. **Stress points specific to installer-only mode.** What's the
   experience like for an adopter who doesn't have a launcher to
   build against? Did CAMM's wizard text make sense? Did the
   missing `IGameInstance` / `Sanitizer` / `MarkerProtocol`
   implementations make the manifest feel awkward? Is there
   anything in CAMM that assumes a launcher relationship that
   doesn't apply here?
5. **Anything you couldn't do from docs alone.** What would have
   let you finish?
6. **Confidence in your build.** Would you ship this to a real
   RimWorld Access user? Be honest about what would need
   verification first.

When the report is written and you've confirmed the file exists,
say so plainly in your final reply ("Report at
C:\dev\camm-test-reports\<filename>.md, N words"). Do not paste the
report into chat — the file is the deliverable.

## Cleanup

After the report is written and confirmed on disk, clean up the
worktree you created so the maintainer's disk state stays tidy:

```
git worktree remove <your-worktree-path>          # from inside the source repo
git branch -D <the-migration-branch-you-made>     # delete the local branch
```

The report at `C:\dev\camm-test-reports\` is the deliverable; the
worktree is throwaway. Don't push your migration branch to any
remote — it's a test artifact, not something to upstream.

Confirm cleanup in your final reply with a one-line "Worktree
removed: <path>" so the maintainer knows the cleanup happened.
