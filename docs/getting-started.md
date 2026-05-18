# Getting started with CAMM

This guide walks you through wiring CAMM into your mod, end to end.
Plan on roughly half an hour the first time through. The result is a
single signed `.exe` your users can download, double-click, and have
your mod installed — plus automatic updates on every game launch
from then on.

We'll assume you've read [the README](../README.md) and decided CAMM
fits your mod. (Quick recap if you're skipping around: **launcher
mode** is for mods where you want CAMM to intercept the game launch
and route screen-reader speech from a log file, and **installer-only
mode** is for in-process mods — Harmony, BepInEx, MelonLoader,
Fabric — where CAMM just handles install, uninstall, and auto-update
for files you ship.)

Throughout this guide, the reference adopter is
[civ-vi-access](https://github.com/nromey/civ-vi-access). Its
`CivViAccess/` directory is about 200 lines of glue against CAMM and
demonstrates every pattern below in working production code. When
something here is unclear, that repo is your second source of truth.

---

## Step 1: add CAMM as a submodule

CAMM is consumed via git submodule plus a `<ProjectReference>` in
your csproj — not via NuGet. This keeps you in control of which CAMM
version compiles into your build: you check out a specific tag
inside the submodule, and that's the version you get. Upgrades are
always a deliberate "check out a newer tag and commit the SHA bump,"
never a surprise.

From your mod's repo root:

```
git submodule add https://github.com/nromey/camm.git camm
cd camm && git checkout v0.5.0 && cd ..
git add camm .gitmodules
git commit -m "Add CAMM v0.5.0 as a submodule"
```

That's all for this step. From here on, the rest of the work happens
inside your own launcher project.

---

## Step 2: create your launcher project

Your CAMM-based launcher needs its own subdirectory inside your
repo. Don't put it at the repo root if you have other `.cs` files
there — the .NET SDK's default file glob will pull every loose `.cs`
file into your launcher build, and you'll spend an afternoon
chasing cryptic conflicts against CAMM's own types. Civ VI Access
uses `CivViAccess/`; pick something similar that matches your mod's
name.

Inside that directory, create your `.csproj`. This is the
boilerplate for a CAMM-based launcher — read the inline comments
before changing anything:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0-windows</TargetFramework>
    <PlatformTarget>x64</PlatformTarget>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>

    <!-- Bump this on each shipped release. The release pipeline
         overrides via -p:Version=<tag> at publish time. -->
    <Version>0.1.0</Version>

    <!-- The produced exe's filename (no version, no spaces). -->
    <AssemblyName>YourModLauncher</AssemblyName>
    <Product>Your Mod Display Name</Product>
    <Company>Your Name</Company>

    <!-- LibraryImport's source generator emits unsafe pointer code
         for marshalling. Required for CAMM's P/Invokes. -->
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>

    <!-- Native AOT: produces a single self-contained native exe
         with no .NET runtime install requirement. -->
    <PublishAot>true</PublishAot>
    <InvariantGlobalization>true</InvariantGlobalization>

    <!-- WinForms is required for the install wizard. The trim-
         error suppression is needed because PublishAot implies
         trimming and the SDK errors out on WinForms + trimming by
         default. Safe to suppress: CAMM's wizard surface is code-
         only (no Designer, no data binding, no property grid). -->
    <UseWindowsForms>true</UseWindowsForms>
    <_SuppressWinFormsTrimError>true</_SuppressWinFormsTrimError>

    <!-- App manifest declares Common Controls v6 (TaskDialog) and
         PerMonitorV2 DPI. Required — without this, TaskDialogIndirect
         throws EntryPointNotFoundException at first use. Copy
         camm/templates/app.manifest into your project dir (see Step 3). -->
    <ApplicationManifest>app.manifest</ApplicationManifest>
  </PropertyGroup>

  <!-- CAMM as a project reference. -->
  <ItemGroup>
    <ProjectReference Include="..\camm\Camm\Camm.csproj" />
  </ItemGroup>

  <!-- Tolk native sidecars: the consuming exe embeds them, CAMM's
       TolkBootstrap extracts them at runtime so the exe is
       self-contained. Source path points into CAMM's third_party. -->
  <ItemGroup>
    <EmbeddedResource Include="..\camm\third_party\tolk\dist\x64\*.dll">
      <LogicalName>tolk/%(Filename)%(Extension)</LogicalName>
    </EmbeddedResource>
  </ItemGroup>

  <!-- Mod payload(s) — one EmbeddedResource glob per payload. The
       LogicalName prefix must match the ModPayload.Name in your
       manifest. Civ VI Access has one payload "mod"; multi-root
       adopters add more globs with different prefixes. -->
  <ItemGroup>
    <EmbeddedResource Include="..\YourModPayloadFolder\**\*.*"
                      Exclude="..\YourModPayloadFolder\bin\**\*;..\YourModPayloadFolder\obj\**\*">
      <LogicalName>mod/%(RecursiveDir)%(Filename)%(Extension)</LogicalName>
    </EmbeddedResource>
  </ItemGroup>

</Project>
```

A handful of those settings deserve a moment of attention.
`PublishAot` and `InvariantGlobalization` together produce a single
self-contained native exe with no .NET runtime requirement — that's
what makes "double-click an installer, no prerequisites" work for
your end users. `UseWindowsForms` is non-negotiable because CAMM's
install wizard is a WinForms surface, and `_SuppressWinFormsTrimError`
is the documented escape hatch for combining WinForms with
`PublishAot`. (CAMM's wizard is intentionally code-only — no
Designer, no data binding, no property grid — so trimming is safe.)

### Multi-source single-payload pattern

Sometimes your mod's payload doesn't live in a single folder.
RimWorld Access, for example, ships three files into the same mod
folder, but those files come from three different places: an XML
file in the repo's `About/` directory, a Harmony DLL from
`bin/Debug/net472/`, and a native library from a shared dependency.

That's fine. CAMM doesn't care where the source files live on disk —
it cares about their `LogicalName` prefix. You can use multiple
`<EmbeddedResource>` items, all sharing the same prefix:

```xml
<ItemGroup>
  <EmbeddedResource Include="..\About\About.xml">
    <LogicalName>mod/About/About.xml</LogicalName>
  </EmbeddedResource>

  <EmbeddedResource Include="..\bin\$(Configuration)\net472\rimworld_access.dll">
    <LogicalName>mod/Assemblies/rimworld_access.dll</LogicalName>
  </EmbeddedResource>

  <EmbeddedResource Include="..\..\shared\native\prism.dll">
    <LogicalName>mod/prism.dll</LogicalName>
  </EmbeddedResource>
</ItemGroup>
```

All three resources end up under the `mod/` prefix. At install time
CAMM extracts them into the payload's `DefaultDestination`,
preserving the relative path after `mod/`. This is how RimWorld
Access adopts CAMM without restructuring its source tree.

The same trick works in reverse: two separate `<EmbeddedResource>`
globs can both feed into the same payload prefix. Civ V Access uses
this to combine a proxy DLL from one directory with x86 Tolk runtime
DLLs from a sibling directory, all destined for the game root.

If a per-file source path might not exist at build time (a sibling
project's DLL hasn't been built yet, a native lib hasn't been
fetched), wrap it with `Condition="Exists('...')"`. The missing file
silently drops out instead of failing the build. Just remember to
inspect the final embedded-resource list before shipping — a
silently-skipped resource produces a silently-broken install.

### When your repo isn't flat

If your launcher project sits next to other `.cs` files — a
`tests/` folder, an old `installer/` directory, anything the SDK's
`**/*.cs` glob can reach — the SDK will compile all of it into your
launcher. You'll get CS0436 conflicts against CAMM's types and a
handful of unrelated build errors.

The fix is a `<DefaultItemExcludes>` line in your csproj's
`PropertyGroup`:

```xml
<DefaultItemExcludes>$(DefaultItemExcludes);..\tests\**;..\installer\**;..\third_party\**;..\camm\**</DefaultItemExcludes>
```

List every sibling directory you want the glob to skip. The CAMM
submodule itself (`..\camm\**`) should always be on the list — you're
consuming it via `<ProjectReference>`, so you don't want your csproj
globbing over its source files independently.

---

## Step 3: copy `app.manifest`

Copy `camm/templates/app.manifest` into your launcher project's
directory. The template handles four things you need:

1. **Common Controls v6 dependency**, required by `TaskDialogIndirect`
   (CAMM's channel picker and several wizard dialogs). Without this,
   the first TaskDialog call throws `EntryPointNotFoundException` at
   runtime.

2. **An `asInvoker` `<trustInfo>` block** that prevents Windows'
   installer-detection heuristic from auto-elevating your exe at
   startup. This matters if your `LauncherExeName` contains
   `install`, `setup`, `update`, or `patch` — Windows otherwise
   prompts for UAC on every launch of such exes (and `dotnet run`
   will fail with "the requested operation requires elevation"). The
   block is harmless when not strictly needed, so the template ships
   it by default. CAMM handles elevation internally via a UAC
   reinvocation when it actually needs to write to Program Files or
   HKLM; day-to-day operation stays unprivileged.

3. **PerMonitorV2 DPI awareness**, so the wizard renders crisply on
   high-DPI displays instead of being bitmap-stretched.

4. **Windows 10 / 11** in the supportedOS list.

One quirk worth knowing: if you edit the template's comment blocks,
watch out for the XML rule that `--` may not appear inside a
`<!-- ... -->` block (only the closing `-->` may contain it).
MSBuild embeds malformed manifests without complaint, but Windows'
side-by-side activation rejects them at process startup with a
useless "side-by-side configuration is incorrect" error. To catch
this before your binary fails to run, you can XML-parse the manifest
as a build-time pre-step in PowerShell:

```
[xml]$x = Get-Content app.manifest
```

If the manifest is malformed, that one line surfaces a real error
pointing at the offending line and column.

---

## Step 4: write `Program.cs`

This is your launcher's entry point. The whole file boils down to
constructing a `CammModManifest` and handing it to
`CammHost.RunAsync` — every routing decision (install, update,
uninstall, version-print, wizard-open, transparent game launch) is
made by CAMM based on the manifest's contents and the command-line
arguments. You don't write a switch statement; CAMM is the switch
statement.

Here's the smallest possible `Program.cs`, for an installer-only
adopter:

```csharp
using Camm;

return await CammHost.RunAsync(args, new CammModManifest
{
    // Identity
    LocalAppDataFolderName = "YourModInternalName",
    LauncherExeName = "YourModLauncher.exe",
    AppsAndFeaturesKeyName = "YourModInternalName",
    DisplayName = "Your Mod Display Name",
    Publisher = "Your Name",
    ProjectUrl = "https://github.com/you/your-mod",
    UserAgent = "YourModLauncher",

    // Target game
    TargetGameDisplayName = "Your Target Game",
    TargetGameLauncherName = "Steam",  // or "Epic", "GOG", "standalone"

    // Mod payloads
    ModPayloads = new[]
    {
        new ModPayload(
            Name: "mod",
            FolderName: "YourModPayloadFolder",
            SentinelFileName: "some-file-that-proves-this-is-your-mod-source.json",
            DefaultDestination: () => @"C:\Path\To\Game\Mods\YourMod"),
    },

    // Auto-update (optional — set all three or leave all null)
    GitHubReleasesOwner = "you",
    GitHubReleasesRepo = "your-mod",
    LauncherAssetNamePattern = "YourModLauncher-{0}.exe",
});
```

If you're in **launcher mode** — your mod intercepts the game launch
via IFEO and runs alongside the running game — you'll also fill in:

```csharp
    IfeoTargetExeNames = new[] { "YourGame.exe" },
    GameProcessNames = new[] { "YourGame" },
    GameInstance = new YourGameInstance(),
```

And if your mod uses CAMM's log-tail speech bridge (the Civ VI
Access shape — the in-game Lua code emits `#SCREENREADER`-prefixed
log lines that CAMM forwards to Tolk in the launcher process), add
the two speech-routing seams:

```csharp
    Sanitizer = new YourGameMessageSanitizer(),
    MarkerProtocol = new YourGameScreenReaderMarkerProtocol(),
```

If your mod speaks in-process via its own Tolk binding instead (the
Civ V Access shape — a Lua proxy DLL exposes Tolk as a global inside
the game's Lua context, with no log channel involved), just leave
`Sanitizer` and `MarkerProtocol` null. CAMM still does everything
else launcher mode does — IFEO redirect, game spawn, lifecycle wait —
it just skips the log-tail loop. The derived property
`manifest.LogTailEnabled` is `true` only when both seams are set.

And if you don't fill in `GameInstance` at all, CAMM detects
installer-only mode and skips the entire game-launch path. Your
launcher exe is just install, update, and uninstall — exactly what
a Harmony or BepInEx mod adopter wants.

### Optional v0.3.0 fields

Two fields you'll only need in specific situations.

**`OverwriteStrategy.BackupAndReplace`** on a `ModPayload`. Use this
when your payload replaces a file the game shipped — a forked engine
DLL, a scripting-host proxy. CAMM renames the existing file to
`<name>.original` before extracting your version, and restores the
rename on uninstall. Without this, uninstall would leave your user
with a missing engine DLL and an unplayable game:

```csharp
new ModPayload(
    Name: "engine",
    FolderName: "engine",
    SentinelFileName: "Engine.dll",
    DefaultDestination: () => @"C:\Game\Engine")
{
    OverwriteStrategy = OverwriteStrategy.BackupAndReplace,
},
```

**`PostInstallHook`** on the manifest. Use this when your install
isn't really complete after files-on-disk: RimWorld's
`ModsConfig.xml` needs your mod added to the active-mods list,
BepInEx might need a plugin enable list updated, certain engines
require a separate ModInfo registration. The hook runs after all
payloads extract and Apps & Features registers, but before the
wizard's "install complete" page. Throwing fails the install:

```csharp
PostInstallHook = async installed =>
{
    await MyGameSpecificConfigEditor.EnableMod(...);
},
```

For both fields, behavior is opt-in. If you don't set them, you get
the same behavior CAMM had before v0.3.0.

### Optional v0.4.0 fields

Two more opt-in fields for the cases that v0.3 didn't quite cover.

**`Dependencies`** on the manifest. Use this when your mod needs an
external bootstrap layer that lives in its own mod folder — Harmony
for RimWorld, BepInEx for Unity-based games, MelonLoader for Mono
games, IPA for Beat Saber. Each dependency declares where to fetch
it from on GitHub Releases, where it should land on disk, and a
sentinel file that proves it's already installed:

```csharp
Dependencies = new[]
{
    new ModDependency(
        Name: "brrainz.harmony",
        DisplayName: "Harmony",
        GitHubReleasesOwner: "pardeike",
        GitHubReleasesRepo: "HarmonyRimWorld",
        AssetNamePattern: "Harmony-{0}.zip",
        InstallPath: () => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "AppData", "LocalLow", "Ludeon Studios",
            "RimWorld by Ludeon Studios", "Mods", "Harmony"),
        SentinelFileName: "About/About.xml")
    {
        ZipRootStripPrefix = "*",
    },
},
```

At install time CAMM checks the sentinel; if the dependency is
missing, the user gets a TaskDialog prompt ("Install Harmony / Skip
/ Cancel"), and with consent CAMM downloads the latest release from
GitHub and extracts it. Dependencies survive your mod's uninstall
(they're shared resources another mod may need) and don't
auto-update (CAMM updates your mod; the dependency stays at whatever
version was installed). `{0}` in the asset pattern substitutes with
the release's tag with any leading `v` stripped.

`ZipRootStripPrefix` handles the common case where a GitHub release
zip wraps its content in a top-level directory named after the tag
(`HarmonyRimWorld-2.3.3/...`). `"*"` strips whatever the first
directory turns out to be; a literal string strips that exact
prefix; `null` extracts as-is.

**`PreInstallHook`** on the manifest. The symmetric partner to
`PostInstallHook` — runs before payload extraction and before the
dependency check. Use it for arbitrary scripted work CAMM doesn't
model declaratively: migrating from a pre-CAMM deployed state,
fetching a non-GitHub-Releases dependency, transforming a config
file before payloads land:

```csharp
PreInstallHook = async () =>
{
    // Migrate from a pre-CAMM deploy.ps1 install: detect the old
    // backup naming and restore vanilla before CAMM's
    // BackupAndReplace runs.
    var staleBackup = Path.Combine(GameDir, "lua51_original.dll");
    if (File.Exists(staleBackup))
    {
        File.Move(staleBackup,
            Path.Combine(GameDir, "lua51_Win32.dll"),
            overwrite: true);
    }
},
```

Like `PostInstallHook`, throwing fails the install. Idempotency is
your responsibility — the hook will run on every install pass.

---

## Step 5 (launcher mode only): implement the seam interfaces

If you're in launcher mode, your manifest references one to three
small classes that you implement. Each one is 20 to 50 lines of
code; together they describe how your specific game maps onto CAMM's
generic launch flow.

### `IGameInstance` — the game's identity

CAMM needs to know where the game's executable lives, where it
writes its log file, and what to say before and after launching it.
Those four methods are `IGameInstance`:

```csharp
using Camm;

public sealed class YourGameInstance : IGameInstance
{
    public string? FindGameExe() => @"C:\Program Files\YourGame\YourGame.exe";

    public string GetLogFilePath() => Path.Join(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "YourGame", "Logs", "game.log");

    public string GetLaunchAnnouncement() =>
        "Launching Your Game.";  // or read first-launch detection state

    public string GetClosedAnnouncement() => "Your Game closed.";
}
```

Look at `CivViAccess/CivViGameInstance.cs` for a real example,
including the EULA-aware first-launch announcement pattern — it
reads the game's user-options file to detect whether the user has
accepted the EULA, and gives a longer, more orienting greeting on
the first run.

### `IMessageSanitizer` — strip in-engine markup before speech

CAMM's log-tail feeds every line through your sanitizer before
sending it to Tolk. Your job is to strip out the in-engine markup
your game uses — `[ICON_FOO]`, `[COLOR:Red]`, `[/COLOR]`, and so on —
plus any line-prefix your mod adds to its log output:

```csharp
using System.Text.RegularExpressions;
using Camm.Speech;

public sealed class YourGameMessageSanitizer : IMessageSanitizer
{
    private static readonly Regex Markup = new(
        @"\[ICON_\w+\]|\[COLOR:\w+\]|\[ENDCOLOR\]|\[NEWLINE\]",
        RegexOptions.Compiled);

    public string Sanitize(string raw) => Markup.Replace(raw, " ");
}
```

`CivViAccess/Speech/CivViMessageSanitizer.cs` is the reference —
it handles a richer set of Civ VI markup including the line-prefix
strip.

### `IScreenReaderMarkerProtocol` — decide which lines speak

Not every line in the game's log file should be spoken aloud — only
the ones your mod tagged as screen-reader content. The marker
protocol decides which lines qualify and parses any in-line options
(interrupt vs. queue, for example):

```csharp
using Camm.Speech;

public sealed class YourGameScreenReaderMarkerProtocol : IScreenReaderMarkerProtocol
{
    public string MarkerPrefix => "#SCREENREADER";

    public bool ContainsMarker(string line) => line.Contains(MarkerPrefix);

    public SpeechOptions ParseOptions(string line)
    {
        // Look for [NOINTERRUPT] inside the marker etc. Return default
        // for lines with no options.
        return new SpeechOptions(NoInterrupt: line.Contains("[NOINTERRUPT]"));
    }
}
```

`CivViAccess/Speech/CivViScreenReaderMarkerProtocol.cs` is the
reference.

---

## Step 6: build and smoke-test

With `Program.cs` and your seam classes in place, you should be able
to build and run:

```
dotnet build YourModLauncher.csproj -c Debug
dotnet run --project YourModLauncher -- --version
dotnet run --project YourModLauncher -- --wizard-test
```

`--version` prints your launcher's identity, install state, and
update channel. The same line gets spoken aloud via Tolk if you have
a screen reader running — a useful first sanity check that the Tolk
sidecar DLLs were embedded and extracted correctly.

`--wizard-test` opens the install wizard in dry-run mode. The
Install button fires a two-second simulation instead of actually
installing anything. Walk through every page — Welcome, Update
channel, Ready, Installing, Done — and verify that every visible
string substitutes your manifest's `DisplayName`, `Publisher`, and
`TargetGameDisplayName` correctly. If something looks wrong here,
your manifest probably has a typo.

---

## Step 7: install for real and ship

When the dry-run wizard looks right, run the real install:

```
dotnet run --project YourModLauncher -- --install
```

This goes through UAC and writes to Program Files for real, so have
`--uninstall` ready in case anything goes wrong. The worst case is a
minute of restoring your machine state — the install is
deterministic and the uninstall reverses it.

Once the local install works, the release pipeline is a one-time
setup. Copy `.github/workflows/release.yml` from civ-vi-access,
swap in your own Azure Trusted Signing account names, push a tag,
and CI produces a signed exe attached to the GitHub Release. From
then on, every tag you push ships a signed binary your users can
download.

### `dotnet publish` for the release

CI runs `dotnet publish` to produce the single AOT-native exe your
end users actually download. Locally, the same command works:

```
dotnet publish YourModLauncher.csproj ^
    -c Release -r win-x64 ^
    -p:Version=0.1.0 ^
    -p:PublishAot=true
```

The output lands at
`bin\Release\net10.0-windows\win-x64\publish\YourModLauncher.exe` as
a single file. Tolk DLLs and your mod payload are embedded inside;
nothing else needs to ship alongside it.

One gotcha if your payload is assembled from a sibling project's
build outputs: `<EmbeddedResource Include="..\bin\$(Configuration)\net472\...">`
inherits `$(Configuration)` from your launcher's publish, not the
sibling's. To embed a Release-built Harmony DLL, either build the
sibling in Release first (`dotnet build ..\rimworld_access.csproj -c Release`)
or hard-code `Debug` / `Release` in the embed path. The release.yml
workflow template handles this by building Release across the whole
solution before publishing the launcher.

---

## Adopting CAMM for a mod with an existing build pipeline

Many mods already have a working MSBuild flow that produces
artifacts in `bin/`, plus a deploy script (`deploy.ps1`, an MSBuild
target, etc.) that copies those artifacts to the game's mod folder
during development. RimWorld Access has `rimworld_access.csproj`
producing a Harmony DLL with a `DeployMod` target. Civ V Access has
a multi-project Avalonia installer plus `deploy.ps1`. For mods like
these, the CAMM adopter is a **second project** that sits alongside
your existing build, not a replacement for it.

The shape:

```
<repo root>/
├── <existing-mod>.csproj    (produces the DLL — unchanged)
├── About/                   (existing payload contents)
├── bin/Release/...          (existing build output)
├── installer/               (new — the CAMM launcher project)
│   ├── installer.csproj
│   ├── Program.cs           (manifest + RunAsync)
│   └── app.manifest
└── camm/                    (new — git submodule)
```

The new project's csproj uses `<EmbeddedResource>` items to slurp
your existing build outputs (see the multi-source single-payload
pattern in Step 2). Day to day, you keep using your existing deploy
script for fast iteration. At ship time, the CAMM project's
`dotnet publish` produces the installer your end users download.
The two flows don't touch each other.

**What you typically delete** from your pre-CAMM tree:

- A custom installer project (Avalonia, WPF, or WinForms wizard).
  CAMM provides this.
- Your existing Apps & Features registration code. CAMM provides
  this.
- Any upload-and-self-update logic. CAMM provides this too, against
  GitHub Releases.

**What you keep:**

- Your existing primary csproj — the one that produces the in-game
  DLL or mod payload.
- Anything that generates per-developer config
  (`GamePaths.props.template` and friends).
- Post-build deploy targets that aren't superseded by the installer —
  they're still the fastest dev iteration path.
- Your `deploy.ps1` if you have one. The CAMM installer is for
  end-user distribution; `deploy.ps1` is for the edit-build-launch
  loop you actually live in while writing code.

The migration is genuinely small. The new launcher project plus
zero to four small seam-interface classes is typically less than
200 lines of code total.

---

## Bitness: x64 launcher with a 32-bit game

CAMM's launcher exe is always x64. The embedded Tolk DLLs and the
AOT-native code path target x64 specifically, and that's not
configurable.

This matters if your **target game is 32-bit** — Civ V is x86, and
several other older titles are too. The good news is that this just
works: a 64-bit launcher process spawning a 32-bit game via
`DEBUG_PROCESS` is fine, with no DLL bitness mismatch inside the
launcher's address space. The IFEO redirect intercepts the 32-bit
game by exe filename regardless of bitness, and
`CAMM.ProcessLauncher` handles the cross-bitness spawn cleanly. The
launcher and the game are separate processes; the only thing they
share is the log file the launcher tails, which is just bytes.

The catch is that your mod payload may itself need 32-bit DLLs to
load inside the 32-bit game — a Lua proxy DLL alongside a 32-bit
Tolk runtime, for example. Make that a separate `ModPayload` whose
contents target the game's bitness, embedded from a separate source
directory in your csproj. Civ V Access does exactly this: the
launcher is x64, the `proxy/` payload is x86, the `dlc/` payload is
bitness-neutral, and the `engine/` payload is x86.

---

## Common questions

**My update check is failing because `GitHubReleasesOwner` and
`Repo` aren't set yet.**
Leave all three GitHub fields null. CAMM detects that auto-update is
disabled and skips the check entirely. Once your release pipeline is
up, fill the fields in and it starts working — no other code changes
needed.

**I have multiple destinations to deploy to (a DLC payload here, a
proxy DLL there, an asset bundle somewhere else).**
Use multiple `ModPayload` entries in the list. Each gets its own
embed-resource prefix (`<LogicalName>name/...`) in your csproj and
its own `DefaultDestination`. CAMM tracks each payload independently,
so uninstall removes each one's files separately — even when
payloads share a destination directory, you won't accidentally
delete the wrong files.

**My mod's payload is a single DLL that drops into the game root.**
That works. Make a payload directory containing just that DLL, point
`DefaultDestination` at the game root, and CAMM extracts your one
file. Uninstall removes only the files CAMM wrote — it won't
recursive-delete the destination, which is exactly the behavior you
want when the destination is a shared directory like the game
install root.

**My mod doesn't use `#SCREENREADER`-prefixed log lines. Speech
happens in-process via my own Tolk binding.**
You're in installer-only mode, or in launcher-mode-without-log-tail
if you still want CAMM to intercept the game launch. Either way,
leave `Sanitizer` and `MarkerProtocol` null in your manifest. CAMM
gives you the wizard, Apps & Features registration, and auto-update;
speech routing stays your mod's responsibility, which is what you
want when speech happens inside the game process.

**Dev-mode source-discovery isn't finding my source folder.**
CAMM's dev-mode walk is parent-walk plus one-step-down. It checks
the launcher exe's containing directory, then its parent, then its
grandparent, and so on. At each step it looks for an immediate child
directory matching `FolderName` (and the sentinel file inside it).
It does **not** recurse deeply. If your source lives two-plus levels
below the launcher exe (`repo/src/dlc/<your folder>`, say), dev-mode
discovery silently no-ops and the launcher falls through to
embedded-resource extraction. That's fine — install and update flows
always read from embedded resources regardless. You just lose the
faster dev edit-build-launch loop.

**I want to localize CAMM's visible strings into German (or any
other language).**
Copy `camm/Camm/lang/en.json` to your launcher project's
`lang/de.json` (or `de-DE.json` for region-specific German),
translate the values, and leave the keys plus the `__*__`
substitution tokens untouched. Add it as an EmbeddedResource, or
ship it as a loose file next to your launcher exe — both work. CAMM
auto-detects `CultureInfo.CurrentUICulture` at startup and uses
whichever language file matches.

**Can I customize the install wizard pages?**
Not without forking CAMM. The wizard's per-page UI is fixed; only
the strings (via the locale catalog) and `IGameInstance.GetLaunchAnnouncement`
are customizable, and the latter never actually appears in the
wizard. If your install genuinely needs custom UI, that's a CAMM
limitation worth raising as an issue.

---

## Reference adopter

When in doubt,
[civ-vi-access](https://github.com/nromey/civ-vi-access) is the
canonical, production-tested example. The relevant files:

- `CivViAccess/Program.cs` — manifest + `RunAsync`
- `CivViAccess/CivViGameInstance.cs` — `IGameInstance` implementation
- `CivViAccess/Speech/CivViMessageSanitizer.cs` — `IMessageSanitizer`
  implementation
- `CivViAccess/Speech/CivViScreenReaderMarkerProtocol.cs` —
  `IScreenReaderMarkerProtocol` implementation
- `CivViAccess/CivViAccess.csproj` — csproj boilerplate
- `CivViAccess/app.manifest` — Common Controls v6 + trustInfo
- `.github/workflows/release.yml` — Azure Trusted Signing release
  pipeline

If you get stuck on something this guide doesn't address, those
files are the next place to look.
