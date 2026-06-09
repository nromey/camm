# CAMM — Chameleon Access Mod Manager

Welcome to CAMM, the **Chameleon Access Mod Manager** — a reusable
Windows installer-and-launcher framework for accessibility-mod
authors. If you've written a screen-reader mod for a game and have
ended up reinventing installers, auto-updaters, and "transparent
launch" plumbing every time you ship, CAMM is the framework you
may not have had time to write yourself.

CAMM produces a single Native **Ahead-of-Time (AOT)** Windows
executable — installer, auto-updater, and (optionally) a transparent
game launcher all in one binary, in .NET 10. The "Native AOT" part
matters because the resulting `.exe` is self-contained: no .NET
runtime install required on the user's machine, and the executable
starts cold in well under a second.

Once CAMM is in place, the only thing you have to do is keep
shipping your mod. The installer lets the user pick an update
channel (stable, latest, or off), and CAMM checks your mod's GitHub
Releases page for a newer build every time the launcher exe runs.
In **launcher mode**, CAMM adds a Windows **Image File Execution
Options (IFEO)** redirect so the launcher exe runs first on every
game launch — that's where the "auto-update on every game launch"
story comes from. In **installer-only mode** (Harmony / BepInEx /
in-process mods), CAMM v0.5 added an opt-in "update-only IFEO":
set `IfeoTargetExeNames` on an otherwise-installer-only manifest
and CAMM registers the same redirect, but skips the launcher
process entirely — just runs the update check, spawns the game, and
exits. Either way, no more "please re-download the latest build"
notes in your README — and your users don't need to install any
.NET frameworks first.

Adopting CAMM is roughly 30 lines of `Program.cs` plus up to four
small implementation classes against the CAMM library. The
reference adopter (Civ VI Access) is about 200 lines of glue
against the CAMM submodule. Pre-1.0 means the API can still evolve,
but it's stable enough to ship a real working accessibility mod
today. Give it a try!

## Why CAMM exists

CAMM was extracted from
[Civ VI Access](https://github.com/nromey/civ-vi-access), an
in-progress accessibility mod for Civilization VI. While building
Civ VI Access, we kept running into the same realization: making a
game accessible to screen-reader users is hard work in its own
right, but building the installer, the auto-updater, the
signed-binary release pipeline, and the transparent-launch plumbing
around it was eating a disproportionate share of that work. And it
was the same set of problems every accessibility-mod author has
solved (or worked around) before.

Today, many mods ship with complicated installers that don't change
how the game launches. Users have to track down the installer to
update the mod — and some mods change daily. Worse, users often run
an installer once to play the game, then forget the installer ever
existed (after all, the game plays). But mods are in flux: authors
add support for new screens, new methods, even whole expansions,
and unless the mod ships with a built-in updater, the user can get
stuck running an outdated version without ever realizing it.

Civ V Access, RimWorld Access, Factorio Access, the ONI
accessibility mod, and others all live in this dynamic mod space.
Most of them ship as a zip the user has to download, extract, and
place in the right folder — with manual config edits, no
auto-update, and "Unknown publisher" warnings every time the
installer runs. Or the user has to keep finding the installer for
each mod every time it changes. The technical accessibility user
base — people who are already navigating a more friction-filled
computing experience than the average user — deserves better than
that, and mod authors deserve to spend their time writing mod code,
not installer code. We think CAMM can become the InstallShield for
game accessibility mods.

CAMM is the answer to "what if we just wrote that infrastructure
once?" The piece that makes it specifically worth using, even if
you'd otherwise hand-roll a zip-based release: CAMM-built mods
auto-update on every game launch via an IFEO redirect, with no
"please re-download the latest build" notes in your README.
Launcher-mode adopters get this by default; installer-only
adopters opt in via v0.5's update-only-IFEO mode (set
`IfeoTargetExeNames`, leave `GameInstance` null). Either way, the
user doesn't have to track down a fresh zip — that's the
irreducible value; everything else is a value-add you can opt into
or out of via the manifest.

That single-binary-multiple-modes design is also where the
**chameleon** in the name comes from. The same `.exe` is your
installer when the user runs it from Downloads, your auto-updater
when Windows redirects a game launch through it, your settings
dialog when opened from Apps & Features, and (in launcher mode)
the transparent launcher that announces the upcoming game launch
before handing control to the real game executable. There's no
separate updater process, no background service — CAMM updates
itself by being the binary the game's launch path already runs.
Cool, eh?

And one more thing: the install wizard self-voices its screens via
Tolk. Install screens in general tend to be screen-reader-accessible
in name only; they often don't auto-read, and the user has to hunt
for what's on each page. CAMM's wizard tells the user what it's
installing, lets them pick an update channel (and which updates to
install, if any), then installs the mod plus the IFEO redirect that
keeps it updated, launches any helpers required to run the mod, and
hands control over to the game.

Also worth being upfront: **Civ VI Access itself is not complete,
but the installer is.** We are actively working on the mod, and we hope to have a playable Sid Meier's Civilization VI soon. Many in-game screens in Civ VI Access still
don't have accessibility coverage. We extracted CAMM out of that
mid-development work rather than after the fact, because the gap
CAMM fills was apparent from the first ship — and waiting until
Civ VI Access was finished would have meant a different
accessibility mod adopting Civ-VI-Access-shaped patterns without a
framework to inherit. The AI-readability tests against two
paradigm-mismatched adopters (Civ V Access in launcher mode without
log-tail, RimWorld Access in installer-only mode with a post-install
hook) have already confirmed that the framework holds up outside
its origin context. In other words: using two independent AI
sessions, we verified that CAMM installed properly for two
different mods that aren't Civ VI Access.

## Is CAMM right for your mod?

CAMM has two operating modes, and the manifest's fields tell CAMM
which one you want.

### Launcher mode

Launcher mode is for mods where you want CAMM to step in front of
the game launch. When the user clicks Play in Steam (or any
shortcut), the game's executable gets intercepted via an IFEO
redirect — **CAMM's launcher runs first**, does any setup work
(apply pending updates, prepare speech routing, announce the
launch), and then spawns the real game. While the game is running,
your launcher tails its log file and forwards screen-reader-bound
lines to Tolk. When the game exits, CAMM cleans up with a closed
announcement.

This is the Civ VI Access / Civ V Access execution shape: a Lua-or-similar
mod that lives inside the game, a Tolk speech bridge connecting
the game's speech-bound output to the user's screen reader, and an
IFEO transparent launcher tying it all together.

If your mod has a log-tail style speech bridge, you're in launcher
mode. If your mod wants the launcher exe to live in front of the
launch but speech happens in-process (the Civ V Access shape — its
Lua proxy DLL exposes Tolk as a global directly inside the game's
Lua context, with no log file involved), that's still launcher
mode. You just leave the speech-routing seams null and CAMM skips
the log tail while doing everything else.

### Installer-only mode

Installer-only mode is for mods where there's nothing for CAMM to
intercept. Your mod's runtime lives **inside the game's process** —
a Harmony DLL, a BepInEx plugin, a MelonLoader patch, a Fabric
mod, an in-game C# plugin. The user launches the game normally,
the game loader picks up your DLL, and your DLL handles everything
from there.

If you want to use CAMM, you'll still want a polished installer, an Apps & Features entry, an
auto-updater, and accessibility-friendly install UX. That's exactly
what CAMM gives you in installer-only mode: the install wizard,
Apps & Features registration, GitHub Releases auto-update — and
none of the IFEO redirect, Tolk speech routing, or game-launch
lifecycle, because none of those apply to your mod.

This is the RimWorld Access / BepInEx-plugin / Harmony-DLL shape.
CAMM hands you a single `.exe` your users can double-click; from
there CAMM stays out of the way of your in-game mod.

You still get the benefit of the mod updater: opt into v0.5's
update-only-IFEO mode (set `IfeoTargetExeNames` on the manifest)
and CAMM will check for updates every time the user launches the
game, applying anything new before handing control off. If you'd
rather keep CAMM completely out of the launch path, leave
`IfeoTargetExeNames` null and updates apply whenever the user
re-runs the installer. Either way, no manual zip-tracking for
your users.

**A word on signing.** CAMM ships the *release pipeline template*,
not a signing identity. If you have your own Authenticode
certificate or an Azure Trusted Signing account, the template plugs
that in and your end users see "Verified Publisher: <you>" in the
UAC prompt. If you don't, the same installer still works — users
just see "Unknown publisher" SmartScreen warnings the first time
they run it (and have to click through). The auto-update,
Apps & Features registration, and wizard accessibility features
all work either way. CAMM can't sign on your behalf — the
certificate represents your verified identity to Microsoft and
end users.

One more caveat worth flagging: CAMM deploys files but doesn't
modify game-side config on its own. If your mod needs the game's
own configuration updated to enable it — RimWorld's
`ModsConfig.xml`, BepInEx's plugin enable list, anything like
that — you have two options. You can document the config edit as
a manual step in your README, or you can use CAMM's
`PostInstallHook` (added in v0.3.0) to do the edit programmatically
as part of the install. And if your mod requires a separate
bootstrap layer (Harmony for RimWorld, BepInEx for Unity games,
MelonLoader for Mono games, IPA for Beat Saber), v0.4.0's
`Dependencies` field lets CAMM check for the bootstrap layer at
install time, prompt the user, and fetch it from GitHub Releases —
so your users only have to double-click your installer once.

### Neither?

CAMM is Windows-only. The launcher exe targets `net10.0-windows`,
makes P/Invokes into `kernel32`, `user32`, `comctl32`, and the
Windows registry, and the Tolk runtime it bundles is Windows-only
too. If your mod targets macOS or Linux, CAMM isn't currently a fit — fork
it, or look elsewhere.

CAMM is also built around **single-exe deployment**. Your user
downloads one `.exe`, double-clicks, and their mod is installed. If your
mod's distribution path is a `.zip`, an `.msi`, a `.deb`, or some
other format, CAMM probably isn't the right tool either.

## What CAMM provides

Once you've adopted CAMM, every CAMM-built launcher comes with:

- **A 5-page WinForms install wizard** (Welcome, Update channel,
  Ready, Installing, Done) with accessibility-first speech
  orchestration. screen reader-driven page announcements that beat NVDA or JAWS's
  focus-event race, per-page initial focus, deterministic combobox
  announcements, and a full cancel-confirm flow that doesn't lose
  the user's place.
- **Localizable strings.** Every visible string flows through a
  JSON locale catalog (`lang/<culture>.json`) with manifest-driven
  token substitution (`__DISPLAY_NAME__`, `__TARGET_GAME__`, and so
  on). Translators like Crowdin drop new locale files; nothing else changes.
  Mode-aware variants (`<key>.InstallerOnly`) automatically override
  wording that doesn't apply when CAMM isn't acting as a launcher.
- **Multi-payload install, update, and uninstall.** A single mod
  can deploy files to multiple destinations — Civ V Access ships a
  DLC package, a proxy DLL, and an engine-fork DLL to three
  different locations, and CAMM tracks each one independently so
  uninstall removes exactly the files CAMM wrote, even across
  shared destination directories.
- **Apps & Features registration**, so your users uninstall via
  Windows Settings → Apps. The Modify button opens your
  update-channel picker.
- **GitHub Releases auto-update**, with Stable / Latest / Off
  channels. CAMM polls GitHub for newer mod releases every time the
  launcher exe runs, and applies updates with a `.pending`
  self-update swap. Launcher mode runs the launcher on every game
  launch via the IFEO redirect. Installer-only adopters can opt into
  the same behavior with v0.5's update-only-IFEO mode
  (`IfeoTargetExeNames` set, `GameInstance` null) — CAMM intercepts
  briefly, applies updates, spawns the game, exits.
- **An Azure Trusted Signing release pipeline template** so end
  users see "Verified Publisher: <you>" in the UAC prompt once you
  plug in a signing identity. (Unsigned installers also work; users
  just see SmartScreen "Unknown publisher" warnings on first run.)
- **Backup and restore for vanilla file replacement** (v0.3.0). If
  your mod replaces a file the game shipped — a forked engine DLL,
  a scripting-host proxy — CAMM renames the existing file to
  `.original` on install and restores it on uninstall. Your users
  get a clean game install back when they remove your mod.
- **A post-install hook** (v0.3.0) for mods that need to modify
  game-side config after files-on-disk: ModsConfig.xml edits,
  plugin enable lists, ModInfo registration, anything else CAMM
  doesn't model directly.
- **Declarative external-mod dependencies** (v0.4.0). If your mod
  needs Harmony, BepInEx, MelonLoader, IPA, or any other GitHub-
  released bootstrap layer, declare it on the manifest. At install
  time CAMM checks for it, prompts the user, and (with consent)
  downloads and extracts it. One-click install for the user instead
  of a manual "go subscribe to the Workshop item" step.
- **A pre-install hook** (v0.4.0), the symmetric partner to the
  post-install hook. Runs before payloads extract — for arbitrary
  scripted setup like migrating from a pre-CAMM deployed state.

Launcher mode adds:

- **IFEO transparent-launch redirect**, so your user keeps clicking
  Play in Steam exactly the way they always have.
- **Screen-reader speech relay** of the game's log file to whichever
  screen reader is running — through Tolk (default) or Prism. Ship one
  backend or both; see [`build/PRISM.md`](build/PRISM.md).
- **Foreground handoff and follow-focus minimize**, so the launcher
  console doesn't fight the game for focus.
- **Lifecycle wait**, so the launcher exits cleanly when the game
  does and announces it to the user.

## Quickstart

Read [`docs/getting-started.md`](docs/getting-started.md) for the
full walkthrough — it's the source of truth and goes step by step.
The short version, once you've decided CAMM is right for your mod:

1. Add CAMM as a git submodule:
   `git submodule add https://github.com/nromey/camm.git camm`,
   then check out the latest tag inside the submodule (currently
   `v0.6.0`).
2. In your launcher project's csproj:
   - Set `<TargetFramework>net10.0-windows</TargetFramework>` +
     `<PlatformTarget>x64</PlatformTarget>`.
   - Add `<UseWindowsForms>true</UseWindowsForms>` +
     `<_SuppressWinFormsTrimError>true</_SuppressWinFormsTrimError>`.
   - Add `<PublishAot>true</PublishAot>` +
     `<InvariantGlobalization>true</InvariantGlobalization>`.
   - Add `<ProjectReference Include="..\camm\Camm\Camm.csproj" />`.
   - Embed the Tolk DLLs:
     `<EmbeddedResource Include="..\camm\third_party\tolk\dist\x64\*.dll"><LogicalName>tolk/%(Filename)%(Extension)</LogicalName></EmbeddedResource>`
   - (Optional) For the Prism backend, `<Import Project="..\camm\build\Camm.Prism.targets" />`
     and set `<CammPrismMode>BuildFromSource</CammPrismMode>` — builds
     `prism.dll` from the pinned Prism submodule and embeds it. Ship Tolk,
     Prism, or both; pick the active one with
     `CammModManifest.ScreenReaderBackend`. See [`build/PRISM.md`](build/PRISM.md).
   - For each `ModPayload`, embed your payload directory with
     `<LogicalName><payload-name>/%(RecursiveDir)%(Filename)%(Extension)</LogicalName>`.
   - If your repo isn't flat (you have other `.cs` files in sibling
     dirs), set `<DefaultItemExcludes>$(DefaultItemExcludes);<dir>/**</DefaultItemExcludes>`
     so the SDK glob doesn't pull them in.
3. Copy `camm/templates/app.manifest` into your launcher project's
   directory.
4. Write `Program.cs` (manifest construction + `CammHost.RunAsync`).
   Add `IGameInstance`, `IMessageSanitizer`, and
   `IScreenReaderMarkerProtocol` implementations for launcher mode
   if your mod uses the log-tail speech bridge.
5. `dotnet build`, then `dotnet run -- --version` and
   `dotnet run -- --wizard-test` to smoke-test.

[**The civ-vi-access reference adopter**](https://github.com/nromey/civ-vi-access)
shows the full launcher-mode shape in about 200 lines across four
files. When something in the docs is unclear, that repo is your
second source of truth.

## Why .NET 10

Two reasons we picked .NET 10 over earlier framework versions:

1. **Long-Term Support.** .NET 10 is the current LTS release —
   supported by Microsoft through November 2028 with security and
   stability updates. Adopting an LTS version means CAMM (and your
   mod) won't have to chase a new framework version every 18 months;
   the even-numbered .NET releases are the stable foundation, and
   10 is the freshest of those.

2. **Mature Native AOT.** AOT compiles your launcher into a single
   self-contained native `.exe` with the runtime baked in — that's
   what lets us promise "no first-install-.NET friction" for end
   users, and what makes the binary cold-start in well under a
   second. WinForms-under-AOT was historically painful; .NET 10's
   improvements (plus a small documented escape hatch in your
   csproj — `<_SuppressWinFormsTrimError>true</_SuppressWinFormsTrimError>`)
   make it routine.

The combination is the unlock for CAMM's single-exe deployment
story: long enough support window that adopters aren't on a
treadmill, AOT good enough that the resulting binary is genuinely
self-contained.

## Reference docs

- [`docs/getting-started.md`](docs/getting-started.md) — step-by-step
  adoption walkthrough.
- [`docs/manifest-reference.md`](docs/manifest-reference.md) — every
  `CammModManifest` field documented with examples and a three-mode
  cheat sheet (launcher-with-log-tail, launcher-without-log-tail,
  installer-only).
- [`build/PRISM.md`](build/PRISM.md) — the optional Prism speech backend:
  the Tolk/Prism/both bundling choice and build-from-source pipeline.
- [`docs/migration-test-prompts/`](docs/migration-test-prompts/) —
  AI-assistant acceptance tests for the docs. Point a fresh Claude
  Code session at the prompts to verify the docs still produce a
  working adopter.
- [`CAMM_V040_PLAN.md`](CAMM_V040_PLAN.md) — design doc for the
  v0.4.0 features (Dependencies + PreInstallHook).
- [`CHANGELOG.md`](CHANGELOG.md) — version history.

## Status

**v0.5.0** — public surface stable, four operating modes (launcher
with log-tail, launcher without log-tail, installer-only,
installer-only with update-on-launch IFEO), and the optional
v0.3.0 / v0.4.0 / v0.5.0 features (backup-and-replace, post-install
hook, pre-install hook, declarative external-mod dependencies,
mode-aware locale variants, update-only IFEO). The first adopter is
[civ-vi-access](https://github.com/nromey/civ-vi-access). Two more
adopter shapes were validated via AI-readability tests across
v0.3.0 and v0.4.0: Civ V Access in launcher-without-log-tail mode
(with backup-and-replace + pre-install hook) and RimWorld Access in
installer-only mode (with post-install hook + declarative
Harmony dependency).

Pre-1.0 means minor versions may introduce additive API changes.
Consuming mods pin to a tag SHA via git submodule and upgrade on
their own schedule.

## License

MIT. See [LICENSE](LICENSE).

The vendored Tolk runtime under `third_party/tolk/` keeps its own
LICENSE.txt and LICENSE-NVDA.txt; see `third_party/tolk/SOURCE.md`
for provenance.

## Naming

CAMM stands for **Chameleon Access Mod Manager**. The "Chameleon"
part is the single-binary-multiple-modes architecture explained in
[Why CAMM exists](#why-camm-exists) above. "Access Mod Manager"
because the framework is for accessibility-mod authors, not for
end users directly — your users see your mod's branding, not
CAMM's.
