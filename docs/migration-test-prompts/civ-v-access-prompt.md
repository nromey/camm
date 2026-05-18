# Civ V Access → CAMM migration task

I want to convert the Civ V Access mod
(https://github.com/rashadnaqeeb/Civ-V-Access, also clonable at
`C:\dev\Civ-V-Access\` if you're on the right machine) to use the
CAMM framework (https://github.com/nromey/camm).

CAMM (Chameleon Access Mod Manager) is a reusable .NET 10 launcher
framework that provides install wizards, IFEO transparent-launch,
Tolk speech routing, GitHub Releases auto-update with stable / latest
/ off channels, and Azure Trusted Signing release pipeline — all
behind a single `CammHost.RunAsync(args, manifest)` entry point.

The first adopter is Civ VI Access
(https://github.com/nromey/civ-vi-access), whose `CivViAccess/`
directory contains only ~200 lines of game-specific glue against
CAMM. Civ VI Access is the closest analogue to Civ V Access — same
mod paradigm (Lua + Tolk + log-tail speech bridge), same Firaxis
engine family. Study Civ VI Access's structure before starting; the
shape of a Civ V Access CAMM adopter should look nearly identical.

## First task: study the references

Read in order:

1. CAMM's `README.md`, `docs/getting-started.md`, and
   `docs/manifest-reference.md`. The README answers "is CAMM right
   for your mod" — confirm Civ V Access falls into launcher mode (it
   should: Lua mod + log-tail-style speech + IFEO redirect through
   the launcher exe). `docs/getting-started.md` walks through the
   adoption steps; `docs/manifest-reference.md` documents every
   manifest field.
2. CAMM's `CHANGELOG.md` (latest entry — v0.3.0). v0.3.0 introduced
   a few features specifically relevant to Civ V Access's shape: an
   optional `OverwriteStrategy.BackupAndReplace` for payloads that
   replace files the game ships with (engine DLLs, scripting host
   DLLs), and a `LogTailEnabled` derived property that lets launcher-
   mode adopters skip the log-tail bridge if they speak in-process
   via their own Tolk binding rather than through CAMM's log-tail
   loop. Read the v0.3.0 entry and judge whether either applies to
   Civ V Access.
3. The Civ VI Access `CivViAccess/` directory:
   - `Program.cs` (the thin shim: manifest construct + RunAsync call)
   - `CivViGameInstance.cs` (implements `Camm.IGameInstance`: paths to
     the game exe and log file, launch announcement, closed
     announcement)
   - `Speech/CivViMessageSanitizer.cs` (implements
     `Camm.Speech.IMessageSanitizer`: regex map for Civ VI's
     `[ICON_*]`, `[COLOR:*]`, `[NEWLINE]` markup)
   - `Speech/CivViScreenReaderMarkerProtocol.cs` (implements
     `Camm.Speech.IScreenReaderMarkerProtocol`: `#SCREENREADER`
     prefix + `NOINTERRUPT` parsing)
   - `CivViAccess.csproj` (ProjectReference to camm/Camm/Camm.csproj,
     embedded-resource globs for Tolk DLLs + mod payload)
3. The Civ V Access codebase. Note what's analogous to Civ VI Access
   (Lua-mod-side `#SCREENREADER` markers? Tolk integration? log file
   path? launcher .exe vs in-game-only?) and what differs (Civ V is
   x86, Civ VI is x64 — does that affect anything? Civ V's mod load
   convention vs Civ VI's? UI markup conventions?).

## Second task: produce the migration

Work in a `worktree` so you don't touch the user's working copy of
Civ V Access. Produce a working CAMM-based Civ V Access launcher
with these acceptance criteria:

- Builds clean with `dotnet build` against CAMM as a submodule
  pinned to `v0.3.0`.
- `dotnet run -- --version` runs and reports sensible output
  (Civ V Access version, install state, channel, project URL).
- `dotnet run -- --wizard-test` opens the install wizard with
  Civ-V-Access-specific text ("Install Civ V Access", "Launch Sid
  Meier's Civilization V from Steam", etc.).
- The four implementation files exist:
  - `<root>/Program.cs` (manifest construct + RunAsync)
  - `<root>/CivVGameInstance.cs` (or similar; implements
    `Camm.IGameInstance` with Civ V's exe path, log file path,
    launch + closed announcements)
  - `<root>/Speech/CivVMessageSanitizer.cs` (sanitizes Civ V's
    in-engine markup — research what that looks like by reading the
    existing Civ V Access source)
  - `<root>/Speech/CivVScreenReaderMarkerProtocol.cs` (whatever
    prefix Civ V Access already uses for screen-reader log lines)

Notes for the bitness question: Civ V's main exe is 32-bit. CAMM's
launcher is 64-bit and ships x64 Tolk DLLs. A 64-bit launcher
spawning a 32-bit game via DEBUG_PROCESS is fine (no DLL bitness
mismatch within the launcher process). The IFEO redirect targets
`CivilizationV.exe` (and any DX12 / Steam variants the existing Civ
V Access intercepts).

Multi-payload note: as of CAMM v0.2.0 the manifest's
`ModPayloads` field is a list of `ModPayload` entries, one per
deploy destination. Civ V Access ships **three** install artifacts
according to its `deploy.ps1` / `installer/Core/Installer.cs`: the
DLC payload, a proxy DLL (`lua51_Win32.dll` at the game root), and
an engine fork DLL (in `Assets\DLC\Expansion2\`). Use three
`ModPayload` entries — each with its own `Name` (embed prefix),
`FolderName` (dev-mode source dir), `SentinelFileName`, and
`DefaultDestination`. The Civ VI Access reference has a one-element
list (`Name = "mod"`); your multi-payload csproj will need three
`<EmbeddedResource>` globs with three different `<LogicalName>`
prefixes.

## What you can ignore

- Don't worry about NuGet versioning. CAMM is consumed via git
  submodule + `<ProjectReference>`, not NuGet.
- Don't worry about signing or GitHub Actions setup.
- Don't migrate the in-game Lua/UI code — Civ V Access's mod payload
  stays as-is, just gets embedded as resources via the new
  CAMM-based csproj.

## Deliverable: report to disk

Write your final report to disk at this exact path:

    C:\dev\camm-test-reports\civ-v-access-<YYYY-MM-DD-HHMM>.md

Create the `C:\dev\camm-test-reports\` directory if it doesn't exist.
Replace `<YYYY-MM-DD-HHMM>` with the current date and time so
multiple runs don't overwrite each other — e.g.
`civ-v-access-2026-05-17-2230.md`.

The report must contain, as top-level sections:

1. **What I produced.** Concrete file paths and line counts.
   Reference the worktree path so the maintainer can inspect the
   build.
2. **Gaps in CAMM's docs.** A numbered list of every place you had
   to guess, every claim CAMM made that turned out wrong or unclear,
   every spot where the README / CHANGELOG / source comments left
   you stuck. Be specific — quote the offending passage or note
   that no docs existed. Examples of what counts: "I had no idea
   whether `LauncherAssetNamePattern` should include the `v` prefix
   from the tag or not, had to read GitHubReleasesClient.cs to find
   out" or "CAMM's README says 'install wizard' but doesn't say
   what happens if my mod has no in-game payload — does
   ModPayloadFolderName have to point at something? Can it be empty?"
3. **Differences from Civ VI Access I had to figure out.** Civ V is
   not just "Civ VI with a different number" — what concrete
   differences (paths, markup, bitness, log location, mod-source
   discovery, etc.) did you have to research, and where? If Civ V
   Access's existing source documented them, say so; if you had to
   reverse-engineer them, say so.
4. **Anything you couldn't do from docs alone.** What would have
   let you finish — a getting-started.md, a worked example
   walking through "step 1: clone CAMM as submodule, step 2: add
   ProjectReference, step 3: ...", a `camm new` scaffolding tool,
   something else?
5. **Confidence in your build.** Would you ship this to a real Civ
   V Access user, or are there things you're guessing at that need
   verification first? Be honest.

When the report is written and you've confirmed the file exists, say
so plainly in your final reply ("Report at
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
