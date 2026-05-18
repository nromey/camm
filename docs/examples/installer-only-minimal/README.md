# Installer-only minimal example

This directory shows the smallest possible CAMM adopter shape for an
**installer-only** mod — the Harmony / BepInEx / MelonLoader / IPA
pattern. The three files below are reference text, not a buildable
project; copy them into your mod's repo as the starting point for a
new launcher project.

If you're adopting CAMM in **launcher mode** instead (your mod
intercepts the game launch via IFEO and runs alongside the running
game), the canonical example is the full
[civ-vi-access](https://github.com/nromey/civ-vi-access) reference
adopter — that's launcher-mode-with-log-tail and shows every seam.

Both v0.4.0 dual-track AI-readability test reports flagged the gap
that this directory closes: "no end-to-end installer-only example
exists in the docs themselves (only the launcher-mode civ-vi-access
reference adopter on GitHub)." Now there is one.

## Files

- [`Program.cs`](Program.cs) — manifest construction +
  `CammHost.RunAsync`. ~40 lines for the basic shape; the optional
  v0.4 / v0.5 fields are commented as opt-ins.
- [`installer.csproj`](installer.csproj) — csproj boilerplate. Same
  shape as `docs/getting-started.md` Step 2, with the launcher-mode-
  specific lines absent.
- [`app.manifest`](app.manifest) — copy of `camm/templates/app.manifest`.
  Don't modify; the v0.3.1 template fix preserves the XML-comment
  rule that side-by-side activation depends on.

## The two installer-only sub-modes

- **Plain installer-only.** `IfeoTargetExeNames` is null. The user
  runs the installer once, mod files land in the game's mod folder,
  Apps & Features registers an Uninstall entry. Updates apply only
  when the user re-runs the installer exe. Right answer when you
  want the simplest possible install with no game-launch
  involvement.
- **Installer-only with update-on-launch IFEO** (v0.5+).
  `IfeoTargetExeNames` is set to the game's executable filename(s).
  CAMM registers an IFEO redirect on the game's exe — on every
  game launch the launcher briefly runs to check for and apply
  updates, then spawns the real game and exits. No log-tail, no
  lifecycle wait. Right answer when you want the auto-update-on-
  launch UX without writing any game-side glue.

The example `Program.cs` below ships in plain installer-only mode by
default; the `IfeoTargetExeNames` line is commented as a one-liner
opt-in.

## Reference adopter

For a working production installer-only adopter using v0.4.0
features (the v0.4 RimWorld Access AI-readability test produced an
adopter with `Dependencies` for Harmony and `PostInstallHook` for
ModsConfig.xml), see the v0.4.0 test report at
`C:\dev\camm-test-reports\rimworld-access-2026-05-18-0715.md`
(maintainer-side artifact, not in this repo).
