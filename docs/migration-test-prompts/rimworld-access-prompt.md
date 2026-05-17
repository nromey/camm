# RimWorld Access → CAMM migration task

I want to convert the RimWorld Access mod
(https://github.com/aaronr7734/rimworld_access, also clonable at
`C:\dev\rimworld_access\` if you're on the right machine) to use the
CAMM framework (https://github.com/nromey/camm).

CAMM (Chameleon Access Mod Manager) is a reusable .NET 10 launcher
framework that provides install wizards, IFEO transparent-launch,
Tolk speech routing, GitHub Releases auto-update with stable / latest
/ off channels, and Azure Trusted Signing release pipeline — all
behind a single `CammHost.RunAsync(args, manifest)` entry point. The
first adopter is Civ VI Access
(https://github.com/nromey/civ-vi-access), whose `CivViAccess/`
directory contains only ~200 lines of game-specific glue against
CAMM. That's the reference shape to study before starting.

## First task: assess architectural fit

Read CAMM's README and CHANGELOG, then look at how Civ VI Access
consumes CAMM (its `CivViAccess/Program.cs`, its
`CivViGameInstance.cs`, its two `Speech/Civ*.cs` files, and its
`CivViAccess.csproj`). Then look at the RimWorld Access codebase.
Don't write any code yet — instead write a short fit-assessment that
answers:

1. Does CAMM's launcher process model match RimWorld Access's
   architecture? (RimWorld Access is a Harmony in-game DLL on .NET
   Framework 4.7.2; CAMM is a .NET 10 launcher exe with IFEO
   transparent-launch. Are these compatible? Partially? Not at all?)
2. If RimWorld Access can adopt CAMM in full: what does the migration
   look like? Outline the new file structure, what becomes manifest
   fields, what becomes `IGameInstance` / `IMessageSanitizer` /
   `IScreenReaderMarkerProtocol` implementations.
3. If it can only partially adopt: which CAMM features make sense
   (installer wizard? auto-update?), which don't (Tolk vs Prism, IFEO
   redirect, log-tail speech for in-game text), and what would the
   hybrid shape look like.
4. If CAMM is the wrong tool entirely: say so plainly. Explain what
   CAMM is missing that would have to be added before this kind of
   mod could adopt it.

Be honest about gaps. The point of this exercise is to identify
CAMM's documentation and architectural shortcomings, not to force a
conversion that doesn't fit.

## Second task (only if your fit-assessment says CAMM applies)

Work in a `worktree` so you don't touch the user's working copy.
Produce a working build. The acceptance criteria are:

- The new RimWorld Access launcher (if applicable) builds clean with
  `dotnet build` against CAMM as a submodule.
- `dotnet run -- --version` runs and produces sensible output.
- Document any places where you had to guess (RimWorld install path,
  RimWorld process name, RimWorld's log file location, what
  "speech-bound" log lines look like, etc.).

## What you can ignore

- Don't worry about NuGet versioning. CAMM is consumed via git
  submodule + `<ProjectReference>`, not NuGet.
- Don't worry about signing or GitHub Actions setup.
- Don't worry about migrating the mod's actual Harmony patches or
  in-game UI work. Those stay as-is; CAMM is only about the launcher
  / installer wrapper, if applicable.

## Deliverable: report to disk

Write your final report to disk at this exact path:

    C:\dev\camm-test-reports\rimworld-access-<YYYY-MM-DD-HHMM>.md

Create the `C:\dev\camm-test-reports\` directory if it doesn't exist.
Replace `<YYYY-MM-DD-HHMM>` with the current date and time so
multiple runs don't overwrite each other — e.g.
`rimworld-access-2026-05-17-2230.md`.

The report must contain, as top-level sections:

1. **Fit assessment.** Full text of your architectural analysis from
   the first task. Don't summarize — paste the analysis verbatim.
   This is the section the CAMM maintainer reads first.
2. **What I produced.** Concrete file paths and line counts. If you
   didn't produce code (because the fit assessment said not to), say
   so explicitly here.
3. **Gaps in CAMM's docs.** A numbered list of every place you had
   to guess, every claim CAMM made that turned out wrong or unclear,
   every spot where the README / CHANGELOG / source comments left
   you stuck. Be specific — quote the offending passage or note that
   no docs existed. This is the section that drives the next round
   of doc work.
4. **Anything you couldn't do from docs alone.** What would have let
   you finish — a getting-started.md, a worked example for a
   non-launcher mod, a `camm new` scaffolding tool, something else?

When the report is written and you've confirmed the file exists, say
so plainly in your final reply ("Report at
C:\dev\camm-test-reports\<filename>.md, N words"). Do not paste the
report into chat — the file is the deliverable.
