# Migration test prompts

These prompts are CAMM's AI-readability acceptance tests. Each one
asks a fresh Claude Code (or similar AI assistant) session to migrate
a real accessibility-mod codebase onto CAMM using nothing but CAMM's
public docs + the civ-vi-access reference implementation. The
assistant writes its full report to
`C:\dev\camm-test-reports\<test>-<YYYY-MM-DD-HHMM>.md` so the
maintainer can read it directly without chat-paste.

## How to run a test

1. Open a fresh Claude Code session (no prior conversation — close
   any previous test session to ensure no memory carries over).
2. Tell it: "Read `<absolute path to the *-prompt.md file>` and do
   what it says." That's the whole handoff. Do NOT paste the
   evaluation rubric — that's for you, not for the assistant.
3. Wait for the assistant to confirm a report path under
   `C:\dev\camm-test-reports\`.
4. Read the report, then read the matching `*-evaluation.md` file
   (in this directory) for the rubric.

## Tests

| Test | When to run | Prompt | Evaluation |
|---|---|---|---|
| **Civ V Access → CAMM** | The canonical "launcher mode" test — same paradigm as Civ VI Access, validates docs work for the launcher-mode adoption path. Civ V Access has 3 install artifacts so this also exercises CAMM v0.2.0's multi-payload `ModPayloads` list. | `civ-v-access-prompt.md` | `civ-v-access-evaluation.md` |
| **RimWorld Access → CAMM** | The "installer-only mode" stress test. RimWorld Access is a Harmony in-game DLL on .NET Framework 4.7.2 — paradigm-mismatched with CAMM's launcher model. Tests whether CAMM's installer-only mode (added in v0.2.0) is usable as a "just give me a signed installer + auto-updater" framework for non-launcher mods. | `rimworld-access-prompt.md` | `rimworld-access-evaluation.md` |

Both tests can run in parallel in separate Claude Code sessions if
you want to compare results. Each writes to a different filename
under `C:\dev\camm-test-reports\`.

## Why split prompt vs. rubric

If the assistant sees the rubric ("Claude correctly identifies
architectural mismatch and proposes installer-only mode → CAMM is
adopter-ready for non-launcher mods") it gets primed to give the
"good" answer rather than its honest take. The split keeps the
test honest — the assistant only ever sees the task, never the
success criteria.
