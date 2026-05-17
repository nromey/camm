# RimWorld Access test: evaluation rubric

**Don't show this file to the assistant running the test.** It's the
maintainer's rubric for reading the resulting report.

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

## Cross-references

- Prompt file the assistant sees: `rimworld-access-prompt.md`
- Reports land at: `C:\dev\camm-test-reports\rimworld-access-<YYYY-MM-DD-HHMM>.md`
