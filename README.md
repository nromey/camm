# CAMM — Chameleon Access Mod Manager

Reusable launcher framework for accessibility mod authors. Build a
Native AOT Windows mod installer / auto-updater (and optionally a
transparent launcher) in .NET 10 by writing ~30 lines of `Program.cs`
+ one to four small implementation classes against the CAMM library.

## Is CAMM right for your mod?

CAMM has two operating modes that the manifest's fields select:

### Launcher mode

For mods where:

- The user clicks the game in Steam (or any shortcut), the game
  exe gets intercepted, **CAMM's launcher runs first**, then it
  spawns the real game.
- Your mod listens to a game-side log file (or some structured
  channel) for lines the launcher should forward to a screen reader
  via Tolk.
- The game shuts down → CAMM's launcher exits cleanly with a closed
  announcement.

This is the Civ VI Access / Civ V Access shape. Lua mod
(or similar) + Tolk speech bridge + IFEO transparent-launch + log-tail.

### Installer-only mode

For mods where:

- Your mod's runtime is a DLL that lives **inside the game's process**
  (Harmony, BepInEx, MelonLoader, Fabric, an in-game C# plugin, etc.).
- The user doesn't go through your launcher to play the game — they
  just launch the game normally and your DLL is already loaded.
- You still want a polished installer, an Apps & Features entry,
  an auto-updater, and accessibility-friendly install UX.

This is the RimWorld Access / BepInEx-plugin / Harmony-DLL shape.
CAMM gives you the install wizard + Apps & Features registration +
GitHub Releases auto-update, and **skips** the IFEO redirect, Tolk
speech routing, and game-launch lifecycle (because your mod handles
all of those itself, from inside the game's process).

**Caveat: CAMM deploys files but does not modify game-side config.**
If your mod needs the game's mod-list config to be updated (e.g.
RimWorld's `ModsConfig.xml`, BepInEx's plugin enable list), you'll
need to document that as a manual step for users today, or wait for
the post-install hook coming in v0.3.0. CAMM also can't install
*your mod's* dependencies (RimWorld Access requires the Harmony
mod separately, for example) — users still acquire those through
their normal channels.

### Neither?

CAMM is Windows-only (the launcher exe is `net10.0-windows` with
P/Invokes to `kernel32`/`user32`/`comctl32`/registry, and the
included Tolk runtime is Windows-only). If your mod targets macOS or
Linux, CAMM isn't a fit — fork or look elsewhere.

CAMM is for **single-exe deployment** — a user downloads one signed
`.exe`, double-clicks it, and is installed. If your mod ships as a
.zip, .msi, .deb, or some other distribution format, CAMM's not the
right tool.

## What CAMM provides

- **5-page WinForms install wizard** (Welcome, Update channel,
  Ready, Installing, Done) with accessibility-first speech
  orchestration — Tolk-driven page announcements that beat NVDA's
  focus-event race, per-page initial focus, deterministic combobox
  announcements, full cancel-confirm flow.
- **Localizable strings**: every visible string flows through a
  JSON locale catalog (`lang/<culture>.json`) with manifest-driven
  token substitution (`__DISPLAY_NAME__`, `__TARGET_GAME__`, etc.).
- **Multi-payload install + update + uninstall**: a single mod can
  deploy files to multiple destinations (Civ V Access ships a DLC
  package + a proxy DLL + an engine fork), each tracked with its own
  install manifest so uninstall removes exactly the files CAMM wrote.
- **Apps & Features registration** so users uninstall via Windows
  Settings → Apps. Modify button opens your update-channel picker.
- **GitHub Releases auto-update** with Stable / Latest / Off
  channels. `.pending` self-update swap on next launch.
- **Azure Trusted Signing release pipeline template** so end users
  see "Verified Publisher: <you>" in UAC instead of "Unknown."
- **Launcher mode only**: IFEO transparent-launch redirect, Tolk
  speech relay of the game's log file, foreground handoff +
  follow-focus minimize, lifecycle wait.

## Quickstart

Read [`docs/getting-started.md`](docs/getting-started.md) for the
full walkthrough. The short version, once your mod knows it wants
CAMM:

1. Add CAMM as a git submodule: `git submodule add https://github.com/nromey/camm.git camm`
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
   - For each `ModPayload`, embed your payload directory with
     `<LogicalName><payload-name>/%(RecursiveDir)%(Filename)%(Extension)</LogicalName>`.
   - If your repo isn't flat (you have other `.cs` files in sibling
     dirs), set `<DefaultItemExcludes>$(DefaultItemExcludes);<dir>/**</DefaultItemExcludes>`
     so the SDK glob doesn't pull them in.
3. Write your manifest + supporting classes:
   - Always: `Program.cs` (manifest construct + `CammHost.RunAsync`).
   - Launcher mode only: `IGameInstance`, `IMessageSanitizer`,
     `IScreenReaderMarkerProtocol` implementations.
4. `dotnet build` + `dotnet run -- --version` + `dotnet run -- --wizard-test`.

[**The civ-vi-access reference adopter**](https://github.com/nromey/civ-vi-access)
shows the full launcher-mode shape (~200 LOC across 4 files).

## Reference docs

- [`docs/getting-started.md`](docs/getting-started.md) — step-by-step adoption walkthrough.
- [`docs/manifest-reference.md`](docs/manifest-reference.md) — every `CammModManifest` field documented with examples.
- [`docs/migration-test-prompts/`](docs/migration-test-prompts/) — AI-assistant acceptance tests for the docs (run a fresh Claude Code session against the prompts to verify the docs work).
- [`CHANGELOG.md`](CHANGELOG.md) — version history.

## Status

**v0.2.0+** — public surface stable. Adopter-ready for both launcher
and installer-only modes. First adopter: [civ-vi-access](https://github.com/nromey/civ-vi-access).
Pre-1.0 means minor versions may introduce additive API; consuming
mods pin to a tag SHA via git submodule and upgrade on their own
schedule.

## License

MIT. See [LICENSE](LICENSE).

Vendored Tolk runtime under `third_party/tolk/` keeps its own
LICENSE.txt + LICENSE-NVDA.txt; see `third_party/tolk/SOURCE.md`
for provenance.

## Naming

CAMM = **Chameleon Access Mod Manager** — "chameleon" because the
same binary changes its behavior based on context (running from
Downloads = installer mode, running from install dir post-install =
transparent launcher, running with `--config` = settings dialog,
etc.) and "access mod manager" because it's a framework for
accessibility-mod authors, not for end users directly.
