# CAMM — Chameleon Access Mod Manager

Reusable launcher framework for accessibility mod authors. Build a Native
AOT Windows mod installer + transparent launcher in .NET 10 by writing
about 300 lines of game-specific glue against the CAMM library.

Out of the box CAMM ships:

- **Tolk-based screen reader output** (NVDA, JAWS, SAPI fallback) with
  native DLL bootstrap from embedded resources
- **5-page WinForms install wizard** (Welcome, Update channel, Ready,
  Installing, Done) with accessibility-first speech orchestration —
  Tolk-driven page announcements that beat NVDA's focus-event race,
  per-page initial focus, deterministic combobox announcements
- **Multi-mode "chameleon" launcher binary** — same exe acts as installer,
  uninstaller, settings UI, transparent launcher (IFEO redirect), and
  self-updater based on args + install state
- **AOT-clean TaskDialog + MessageBox helpers** with proper screen reader
  semantics and Win11 foreground-stealing workarounds
- **Apps & Features registration** so users can uninstall through the
  standard Windows Settings UI
- **GitHub Releases auto-update** with Stable / Latest / Off update
  channels and `.pending` swap for self-update
- **Azure Trusted Signing release pipeline** (GitHub Actions template)
  so end users see "Verified Publisher" in UAC instead of "Unknown"

## Status

**Pre-1.0, in extraction from [civ-vi-access](https://github.com/nromey/civ-vi-access).**
The first consuming mod is Civ VI Access itself; once that dogfooding
proves the abstractions hold, additional adopters can wire in.

The extraction roadmap lives in
[`CAMM_EXTRACTION_PLAN.md`](https://github.com/nromey/civ-vi-access/blob/main/CAMM_EXTRACTION_PLAN.md)
on the civ-vi-access repo. CAMM tracks the migration step-by-step.

This v0.0.1 release contains only the lowest-risk template modules
(Logger, SemVer, ProcessLauncher, TolkBootstrap, Dialogs). The rest
land in subsequent releases as the migration progresses.

## Getting started

Adoption docs land alongside the public surface (`CammHost.RunAsync`,
`CammModManifest`) — they don't exist yet. Until they do, read the
extraction plan linked above and the civ-vi-access source for the
intended shape.

If you're an accessibility-mod author who wants to consume CAMM before
the docs land, open an issue and we'll talk.

## License

MIT. See [LICENSE](LICENSE).

## Naming

CAMM = **Chameleon Access Mod Manager** — "chameleon" because the same
binary changes its behavior based on context (running from Downloads =
installer mode, running from install dir post-install = transparent
launcher, etc.) and "access mod manager" because it's a framework for
accessibility-mod authors, not end users.
