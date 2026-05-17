# Test prompt: convert RimWorld Access to use CAMM

This is a self-contained prompt for a fresh Claude Code session. It's a
test of whether CAMM's public docs + the civ-vi-access reference
implementation are enough for an AI assistant to migrate an existing
accessibility-mod codebase to CAMM with no additional human guidance.

The intent isn't to actually ship a CAMM-based RimWorld Access — it's
to surface gaps in CAMM's documentation. If Claude can't do this from
docs alone, the gap shows us exactly what to add.

---

## Prompt to paste into a fresh Claude Code session

> I want to convert the RimWorld Access mod
> (https://github.com/aaronr7734/rimworld_access, also clonable at
> `C:\dev\rimworld_access\` if you're on the right machine) to use the
> CAMM framework (https://github.com/nromey/camm).
>
> CAMM (Chameleon Access Mod Manager) is a reusable .NET 10 launcher
> framework that provides install wizards, IFEO transparent-launch,
> Tolk speech routing, GitHub Releases auto-update with stable / latest
> / off channels, and Azure Trusted Signing release pipeline — all
> behind a single `CammHost.RunAsync(args, manifest)` entry point. The
> first adopter is Civ VI Access
> (https://github.com/nromey/civ-vi-access), whose `CivViAccess/`
> directory contains only ~200 lines of game-specific glue against
> CAMM. That's the reference shape to study before starting.
>
> **First task: assess architectural fit.** Read CAMM's README and
> CHANGELOG, then look at how Civ VI Access consumes CAMM (its
> `CivViAccess/Program.cs`, its `CivViGameInstance.cs`, its two
> `Speech/Civ*.cs` files, and its `CivViAccess.csproj`). Then look at
> the RimWorld Access codebase. Don't write any code yet — instead
> write a short fit-assessment that answers:
>
> 1. Does CAMM's launcher process model match RimWorld Access's
>    architecture? (RimWorld Access is a Harmony in-game DLL on .NET
>    Framework 4.7.2; CAMM is a .NET 10 launcher exe with IFEO
>    transparent-launch. Are these compatible? Partially? Not at
>    all?)
> 2. If RimWorld Access can adopt CAMM in full: what does the migration
>    look like? Outline the new file structure, what becomes manifest
>    fields, what becomes `IGameInstance` / `IMessageSanitizer` /
>    `IScreenReaderMarkerProtocol` implementations.
> 3. If it can only partially adopt: which CAMM features make sense
>    (installer wizard? auto-update?), which don't (Tolk vs Prism, IFEO
>    redirect, log-tail speech for in-game text), and what would the
>    hybrid shape look like.
> 4. If CAMM is the wrong tool entirely: say so plainly. Explain what
>    CAMM is missing that would have to be added before this kind of
>    mod could adopt it.
>
> Be honest about gaps. The point of this exercise is to identify
> CAMM's documentation and architectural shortcomings, not to force a
> conversion that doesn't fit.
>
> **Second task (only if your fit-assessment says CAMM applies):**
> work in a `worktree` so you don't touch the user's working copy.
> Produce a working build. The acceptance criteria are:
>   - The new RimWorld Access launcher (if applicable) builds clean
>     with `dotnet build` against CAMM as a submodule.
>   - `dotnet run -- --version` runs and produces sensible output.
>   - Document any places where you had to guess (RimWorld install
>     path, RimWorld process name, RimWorld's log file location,
>     what "speech-bound" log lines look like, etc.).
>
> **What you can ignore:**
>   - Don't worry about NuGet versioning. CAMM is consumed via git
>     submodule + `<ProjectReference>`, not NuGet.
>   - Don't worry about signing or GitHub Actions setup.
>   - Don't worry about migrating the mod's actual Harmony patches or
>     in-game UI work. Those stay as-is; CAMM is only about the
>     launcher / installer wrapper, if applicable.
>
> Report back at the end with:
>   - Your fit-assessment.
>   - What you produced (file paths, line counts).
>   - Gaps in CAMM's docs that slowed you down or made you guess.
>   - Anything you couldn't do from docs alone.

---

## What to look for in the result

The output that confirms CAMM is genuinely consumable:

- The assistant identifies the architectural mismatch correctly (CAMM
  is a launcher-process framework; RimWorld Access is an in-process
  DLL). Bonus points for proposing a hybrid use (CAMM as installer
  shell wrapping a `Mods/`-folder copy plus auto-update, even though
  Tolk and IFEO don't apply).
- The assistant builds the manifest correctly — every required field
  filled, including the four implementations (`Sanitizer`,
  `MarkerProtocol`, `GameInstance`, plus the standard scalar fields).
- The "gaps" section names specific docs we're missing (e.g., "CAMM's
  README doesn't explain that ModPayloadFolderName is the *dev-mode
  source* dir, not the deployed-on-the-user's-machine dir" or "no
  guidance on what Tolk does for cross-platform — does it work on
  macOS / Linux at all?").
- The assistant doesn't quietly invent fields, fake APIs, or "make it
  work" by copy-pasting from civ-vi-access without understanding.

## How to interpret signals

| Outcome | What it means for CAMM |
|---|---|
| Claude correctly identifies architectural mismatch and proposes hybrid use | CAMM is consumable but the docs need to explain what kind of mod CAMM fits |
| Claude tries to force a full conversion and produces broken code | CAMM's README needs a "is CAMM right for your mod?" section up top |
| Claude completes a working hybrid build | CAMM is in great shape; docs cover the main path |
| Claude can't read CAMM's surface and gets stuck early | Need a `getting-started.md` walking through the four implementations + manifest construction |

Either way the result of this test goes back into CAMM's docs.
