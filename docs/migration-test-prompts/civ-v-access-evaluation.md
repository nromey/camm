# Civ V Access test: evaluation rubric

**Don't show this file to the assistant running the test.** It's the
maintainer's rubric for reading the resulting report.

## Why this is the canonical test

Civ V Access is the closest possible analogue to Civ VI Access — same
engine family (Firaxis), same paradigm (Lua mod + Tolk + log-tail
speech), same mod-side `#SCREENREADER` marker convention (or close
enough). If the docs work for any mod, they work for this one.
That's why it's the "easy mode" test — if it fails here, CAMM's
docs aren't ready for adopters.

## What to look for in the result

A genuinely consumable CAMM produces a report like:

- "What I produced": four files, ~200-250 LOC total, builds clean,
  smoke test ran. Maybe slightly different from Civ VI Access's file
  count because of bitness handling or whatever Civ V's IFEO target
  binaries are.
- "Gaps": a short list — under 10 items — of clarifying questions or
  small doc-quality issues, not "I don't understand any of this."
- "Differences from Civ VI Access": at minimum, mentions Civ V's
  32-bit exe, the Civ V exe filename(s) for IFEO target, the Civ V
  log file path (likely under `Documents\My Games\Sid Meier's
  Civilization 5\Logs\Lua.log` — note: NOT `%LocalAppData%` like
  Civ VI), the Civ V mod-folder convention (DLC dir vs Mods dir).
- "Confidence": cautious but positive. Maintainer can read this and
  agree.

Red flags:

- Assistant invents API names that don't exist (no Camm.IModPayload,
  no IGameInstance.GetVersion, etc.). Means it's hallucinating
  rather than reading the source.
- Assistant copy-pastes Civ VI Access strings without adapting them
  to Civ V context.
- Assistant misses the bitness issue entirely OR overcorrects (e.g.,
  tries to make CAMM x86, which would break Civ VI Access).
- Assistant produces something that builds but has wrong paths (Civ
  V Lua.log under the wrong dir, wrong exe name for IFEO target).

## How to interpret signals

| Outcome | What it means for CAMM |
|---|---|
| Clean migration, <10 doc-gap items, sensible confidence | CAMM docs are adopter-ready; ship the forum post |
| Migration builds, 10-25 doc-gap items | Doc work needed before forum post; the gaps section IS the doc TODO |
| Migration fails or guesses heavily | Need a `getting-started.md` walking through manifest fields one by one, plus a worked second-mod example |
| Assistant says "I can't tell what CAMM is for" | README needs a big "What CAMM does (and doesn't do)" section up top |

## After running

Whatever the gaps section says, that's the next round of doc work.
Each numbered gap-item is either:

- a doc to add (READMEs, getting-started, manifest-field reference)
- a code comment to expand (Public-API XML doc, source comment)
- a refactor (rename a confusing field, split a method, etc.)

When that work lands, re-run the test on a fresh Claude session.
Iterate until the gaps section is empty (or only has things you
choose not to fix).

## Cross-references

- Prompt file the assistant sees: `civ-v-access-prompt.md`
- Reports land at: `C:\dev\camm-test-reports\civ-v-access-<YYYY-MM-DD-HHMM>.md`
