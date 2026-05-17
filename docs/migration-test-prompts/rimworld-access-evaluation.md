# RimWorld Access test: evaluation rubric

**Don't show this file to the assistant running the test.** It's the
maintainer's rubric for reading the resulting report.

## Why this is the stress test

RimWorld Access is a Harmony in-game DLL on .NET Framework 4.7.2
using Prism for cross-platform screen reader integration. CAMM is
a .NET 10 Windows launcher framework with Tolk speech routing,
IFEO transparent-launch, and log-tail-based in-game speech relay.
The two paradigms barely overlap. CAMM v0.2.0's
**installer-only mode** (GameInstance = null) was added partly to
make adoption like this possible — the launcher exe is just a
wizard + auto-updater for the Harmony DLL + supporting files.

The test checks whether:

- The README's "Is CAMM right for your mod?" section successfully
  routes a paradigm-mismatched adopter into installer-only mode.
- The manifest-reference's "Mode-selection cheat sheet" makes it
  obvious which fields to leave null.
- The getting-started guide's installer-only example is usable
  without a launcher-mode reference adopter to follow.
- Installer-only mode actually works end-to-end (the wizard text
  makes sense, the deploy targets the right Mods/ folder, etc.).

## What to look for in the result

A genuinely consumable installer-only mode produces a report like:

- "Fit assessment": confirms installer-only mode applies, identifies
  the four launcher-mode fields to leave null, calls out RimWorld's
  mod-folder convention (Steam Workshop vs. user-folder Mods/).
- "What I produced": a small launcher project (probably < 100 LOC)
  with `Program.cs` + `.csproj` + `app.manifest`. No `IGameInstance`
  / `IMessageSanitizer` / `IScreenReaderMarkerProtocol` files.
- "Gaps": doc-quality items, ideally < 10. Look for new gaps
  specific to installer-only mode (the docs are written from a
  launcher-mode-first perspective — places where the
  installer-only path is harder to follow).
- "Stress points specific to installer-only mode": the most
  valuable section. Honest answers about whether CAMM's installer-
  only mode feels coherent or feels like a half-conversion of a
  launcher framework.
- "Confidence": should be cautiously positive if installer-only
  mode is solid; the report should flag any "I would not ship this"
  items concretely.

Red flags:

- Assistant forces launcher-mode (tries to write an IGameInstance
  for RimWorld even though there's no IFEO redirect possible) →
  README's mode-selection guidance failed.
- Assistant invents fields or APIs (Camm.HarmonyAdapter, etc.) →
  hallucinating instead of reading docs.
- Assistant gives up before producing code, citing "not enough
  documentation" → installer-only mode docs aren't complete.

## How to interpret signals

| Outcome | What it means for CAMM |
|---|---|
| Working installer-only adopter, < 10 doc gaps, sensible confidence | Installer-only mode is adopter-ready; ship the forum post mentioning it explicitly |
| Working adopter, but stress-points section finds awkward mode-mismatch | Localize the wizard strings for installer-only adopters (different `Wizard.Done.SuccessBody` etc.); minor mode-specific copy work |
| Adopter doesn't build OR assistant says "doesn't apply" but evidence suggests it should | Mode-selection guidance + installer-only example missing from docs |
| Assistant explicitly says "doesn't apply, here's why" with thoughtful reasons | Useful negative result — document what CAMM is *not* for, more clearly |

## After running

The stress-points section drives the docs work for installer-only
adopters. Examples of fixes that might land:

- Add an installer-only worked example to `docs/getting-started.md`
  (currently it's launcher-mode-first with "for installer-only
  adopter you also fill in" stuck mid-flow).
- Add a "Wizard text for installer-only mods" subsection to
  `docs/manifest-reference.md` — explain why "Launch X from Steam"
  shows even though the adopter doesn't run their game through
  CAMM.
- Possibly: locale-catalog keys that select between launcher-mode
  and installer-only-mode wording (`Wizard.Done.SuccessBody.Launcher`
  vs `Wizard.Done.SuccessBody.InstallerOnly`).

## Cross-references

- Prompt file the assistant sees: `rimworld-access-prompt.md`
- Reports land at: `C:\dev\camm-test-reports\rimworld-access-<YYYY-MM-DD-HHMM>.md`
