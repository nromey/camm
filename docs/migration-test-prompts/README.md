# Migration test prompts

These prompts are CAMM's AI-readability acceptance tests. Each one
asks a fresh Claude Code (or similar AI assistant) session to migrate
a real accessibility-mod codebase onto CAMM using nothing but CAMM's
public docs + the civ-vi-access reference implementation. The
assistant writes its full report to
`C:\dev\camm-test-reports\<test>-<YYYY-MM-DD-HHMM>.md` so the
maintainer can read it directly without chat-paste.

## How to run a test

1. Open a fresh Claude Code session (no prior conversation).
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
| **Civ V Access → CAMM** | First. Canonical "easy mode" — same paradigm as Civ VI Access, validates docs work for the obvious adoption path. | `civ-v-access-prompt.md` | `civ-v-access-evaluation.md` |
| RimWorld Access → CAMM | Stress test. Different paradigm entirely (Harmony in-game DLL on .NET Framework 4.7.2 vs CAMM's .NET 10 launcher) — exercises CAMM's "is this the wrong tool for my mod?" guidance. | `rimworld-access-prompt.md` | `rimworld-access-evaluation.md` |

Run Civ V Access first. If it succeeds cleanly, the docs are
adopter-ready for the canonical case and we can ship the forum post.
If it surfaces gaps, those gaps are the next round of doc work —
fix them, re-run, iterate. RimWorld Access is only worth running
once the easy-mode test passes.

## Why split prompt vs. rubric

If the assistant sees the rubric ("Claude correctly identifies
architectural mismatch and proposes hybrid use → CAMM is consumable
but the docs need...") it gets primed to give the "good" answer
rather than its honest take. The split keeps the test honest — the
assistant only ever sees the task, never the success criteria.
