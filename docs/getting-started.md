# Getting started with CAMM

This is the canonical adopter walkthrough. Read [the README](../README.md)
first to decide whether CAMM fits your mod (launcher mode vs.
installer-only mode vs. not a fit). This guide assumes you've made
that call and want to wire CAMM in.

The reference adopter throughout is [civ-vi-access](https://github.com/nromey/civ-vi-access).
Its `CivViAccess/` directory is ~200 lines of glue against CAMM and
shows every pattern below in production code.

---

## Step 1: add CAMM as a submodule

From your mod's repo root:

```
git submodule add https://github.com/nromey/camm.git camm
cd camm && git checkout v0.2.0 && cd ..
git add camm .gitmodules
git commit -m "Add CAMM v0.2.0 as a submodule"
```

CAMM is consumed via git submodule + `<ProjectReference>`, not
NuGet. Update by checking out a newer CAMM tag inside the submodule
and committing the SHA bump.

## Step 2: create your launcher project

Make a subdirectory for your launcher project (don't put it at the
repo root if other `.cs` files live there — the SDK's default glob
will slurp them in). Civ VI Access uses `CivViAccess/`; pick
something similar.

Inside that directory create your `.csproj`:

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

### Multi-source single-payload pattern

If your payload's contents are *assembled* from multiple source
locations (e.g. one DLL from `bin/Debug/net472/`, an XML file from
`../About/`, a native lib from `../../shared/`), you don't have to
stage everything into one source folder. Just use multiple
`<EmbeddedResource>` items — explicit globs or per-file Include —
all sharing the same `LogicalName` prefix:

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

All three resources live under the `mod/` prefix and extract into
that payload's `DefaultDestination` at install time, preserving the
relative path after `mod/`. This is how RimWorld Access adopts CAMM
without restructuring its source tree.

Multi-glob per prefix is also supported: two separate
`<EmbeddedResource Include="...">` globs, both with
`<LogicalName>proxy/...`, will both feed into the `proxy/` payload.
Civ V Access uses this to pull a proxy DLL from one directory and
x86 Tolk runtime DLLs from a sibling directory, all into one payload
destined for the game root.

If any per-file source path might not exist at build time (e.g.
the DLL hasn't been built yet, the native lib hasn't been
bootstrapped), wrap with `Condition="Exists('...')"` so the missing
file silently drops out rather than failing the build. Useful in
CI bootstrap order or when the source-of-the-source is downloaded
on first build. Just remember to verify the final embedded resource
list before shipping — a silently-skipped resource produces a
broken install.

### Non-flat-repo gotcha

If your repo has other `.cs` files at the same level as your
launcher project (a `tests/` folder with C#, an `installer/`
folder with old install scripts in C#, etc.), the .NET SDK's
default glob (`**/*.cs`) will compile them all into your launcher.
This produces conflicts with CAMM's own types (CS0436 warnings) and
often outright build errors.

Fix with `<DefaultItemExcludes>` in your csproj's PropertyGroup:

```xml
<DefaultItemExcludes>$(DefaultItemExcludes);..\tests\**;..\installer\**;..\third_party\**</DefaultItemExcludes>
```

List every sibling directory you want the glob to ignore.

## Step 3: copy `app.manifest`

Copy `camm/templates/app.manifest` into your launcher project's
directory. **If you edit the template's comments**, watch out for
the XML rule that `--` may not appear inside a `<!-- ... -->` block
(only the closing `-->` may contain it). MSBuild embeds malformed
manifests silently; Windows side-by-side activation rejects them at
process startup with a useless "side-by-side configuration is
incorrect" error. To catch this before the binary fails to run, you
can XML-parse the manifest as a build-time pre-step (PowerShell:
`[xml]$x = Get-Content app.manifest`).

The template declares:

- The Common Controls v6 dependency required by `TaskDialogIndirect`
  (CAMM's `ChannelPickerDialog` and several wizard dialogs). Without
  this, the first TaskDialog call throws `EntryPointNotFoundException`
  at runtime.
- An `asInvoker` `<trustInfo>` block that prevents Windows' installer-
  detection heuristic from auto-elevating the exe at startup. **This
  matters if your `LauncherExeName` contains the strings `install`,
  `setup`, `update`, or `patch`** — Windows otherwise demands UAC on
  every launch of such exes (and `dotnet run` will fail with "the
  requested operation requires elevation"). The block is harmless when
  not needed, so it ships in the template by default. CAMM elevates
  internally via UAC reinvocation when it actually needs to write to
  Program Files / HKLM; day-to-day operation stays unprivileged.
- PerMonitorV2 DPI awareness so the wizard renders crisply on
  high-DPI displays.
- Windows 10 / 11 in the supportedOS list.

## Step 4: write `Program.cs`

This is your entry point. The full file for an
installer-only adopter:

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

For a **launcher-mode** adopter you also fill in:

```csharp
    // Launcher mode: target-game lifecycle
    IfeoTargetExeNames = new[] { "YourGame.exe" },
    GameProcessNames = new[] { "YourGame" },
    GameInstance = new YourGameInstance(),
```

If your mod uses CAMM's log-tail speech bridge (Civ VI Access shape:
the game emits `#SCREENREADER`-prefixed log lines that CAMM forwards
to Tolk), also set:

```csharp
    Sanitizer = new YourGameMessageSanitizer(),
    MarkerProtocol = new YourGameScreenReaderMarkerProtocol(),
```

If your mod speaks in-process via its own Tolk binding (Civ V Access
shape: a Lua proxy DLL exposes Tolk as a global inside the game's
Lua context, no log channel involved), leave `Sanitizer` and
`MarkerProtocol` null. CAMM still does everything else launcher
mode does — IFEO redirect, game spawn, lifecycle wait — just without
the log-tail loop. (`manifest.LogTailEnabled` returns true only
when both are set.)

If you don't fill in `GameInstance` at all, CAMM detects
"installer-only mode" and skips IFEO redirect, log-tail speech, AND
game-launch lifecycle entirely. Adopter ships a launcher exe that's
just install + update + uninstall.

### Optional fields

```csharp
    // For mods that overwrite vanilla game files (engine DLL,
    // scripting-host DLL). CAMM renames the existing file to
    // <name>.original before extracting, restores on uninstall.
    // Set on individual ModPayload entries via the with-init syntax:
    ModPayloads = new[]
    {
        new ModPayload(
            Name: "engine",
            FolderName: "engine",
            SentinelFileName: "Engine.dll",
            DefaultDestination: () => @"C:\Game\Engine")
        {
            OverwriteStrategy = OverwriteStrategy.BackupAndReplace,
        },
    },

    // Optional post-install hook. Runs after all payloads are
    // extracted and Apps & Features is registered, before the
    // wizard's "install complete" page. Use for game-side config
    // CAMM doesn't model (RimWorld ModsConfig.xml, BepInEx plugin
    // enable list, ModInfo registration). Throw to fail the install.
    PostInstallHook = async installed =>
    {
        await MyGameSpecificConfigEditor.EnableMod(...);
    },
```

## Step 5 (launcher mode only): implement the seam interfaces

Three small classes the manifest needs:

### `IGameInstance`

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

Look at `CivViAccess/CivViGameInstance.cs` for the EULA-aware
first-launch-announcement pattern (reads game's user-options file
to detect whether to give the verbose first-launch greeting).

### `IMessageSanitizer`

CAMM's log-tail feeds every line through your sanitizer before
speech. Strip your game's in-engine markup ([ICON_FOO], [COLOR:Red],
[/COLOR], etc.) and the line prefix your mod uses.

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
handles a richer set of Civ VI markup including line-prefix strip.

### `IScreenReaderMarkerProtocol`

CAMM's log-tail watcher reads every line. The marker protocol
decides which lines are speech-bound (vs ignored as debug-only) and
parses any in-marker options.

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

## Step 6: build + smoke-test

```
dotnet build YourModLauncher.csproj -c Debug
dotnet run --project YourModLauncher -- --version
dotnet run --project YourModLauncher -- --wizard-test
```

`--version` prints identity + install state + update channel. Should
read out via Tolk if NVDA / JAWS / SAPI is running.

`--wizard-test` opens the install wizard in dry-run mode (no real
install — the Install button fires a 2-second simulation). Walk
through Welcome → Channel → Ready → Installing → Done. Verify
strings substitute your manifest's `DisplayName` / `Publisher` /
`TargetGameDisplayName` correctly.

## Step 7: ship

When the wizard works in dry-run, run for real:

```
dotnet run --project YourModLauncher -- --install
```

This goes through UAC and writes to Program Files for real. Be
ready to uninstall (`--uninstall`) if you find issues.

The release pipeline (signed binaries via Azure Trusted Signing)
is a separate setup. Copy `.github/workflows/release.yml` from
civ-vi-access, replace the signing account names with your own
Trusted Signing setup, push a tag, you get a signed exe on the
GitHub Release.

### `dotnet publish` for the release

The release workflow runs `dotnet publish` to produce the single
AOT-native exe shipped on GitHub Releases. Locally, the same:

```
dotnet publish YourModLauncher.csproj ^
    -c Release -r win-x64 ^
    -p:Version=0.1.0 ^
    -p:PublishAot=true
```

The output is a single `.exe` at
`bin\Release\net10.0-windows\win-x64\publish\YourModLauncher.exe`.
The Tolk and payload resources are embedded inside; nothing else
ships next to it.

If your payload is assembled from a sibling build's outputs (see
multi-source single-payload above), be aware that
`<EmbeddedResource Include="..\bin\$(Configuration)\net472\...">`
inherits `$(Configuration)` from *your launcher's* publish, not
the sibling's. To embed the Release-built Harmony DLL from a sibling
project, either build that sibling in Release first
(`dotnet build ..\rimworld_access.csproj -c Release`) or hard-code
`Debug` / `Release` in the embed path. The release.yml workflow
template handles this by building Release across the whole solution
before publishing the launcher.

## Adopting CAMM for a mod with an existing build pipeline

Many mods already have a working MSBuild flow that produces
artifacts in `bin/` or a staging dir, plus a deploy script
(`deploy.ps1`, an MSBuild target, etc.) that copies those to the
game's mod folder during dev. RimWorld Access has `rimworld_access.csproj`
producing a Harmony DLL with a `DeployMod` target. Civ V Access has
a multi-project Avalonia installer plus `deploy.ps1`. The CAMM
adopter for these mods is a **second project** that sits alongside
the existing build, NOT a replacement for it.

The shape:

```
<repo root>/
├── <existing-mod>.csproj    (produces the DLL — unchanged)
├── About/                   (existing payload contents)
├── bin/Release/...          (existing build output)
├── installer/               (new — the CAMM launcher project)
│   ├── installer.csproj
│   ├── Program.cs           (manifest + RunAsync, no other code if installer-only)
│   └── app.manifest
└── camm/                    (new — git submodule)
```

The new project's csproj uses `<EmbeddedResource>` items to slurp
the existing build outputs (see multi-source single-payload
pattern above). At dev-time you keep using your existing deploy
script for fast iteration; at ship-time the CAMM project's
`dotnet publish` produces the installer that end users actually
download. The two flows don't interact.

What you typically delete from the pre-CAMM tree:

- A custom installer project (Avalonia / WPF / WinForms wizards),
  the Apps & Features registration code, and the
  upload-and-self-update logic. CAMM provides all three.
- A `deploy.ps1` is **kept** — it's still the fastest path for dev
  iteration once you stop having to run the full installer to test
  every change.

What you keep:

- Your existing primary csproj (the one producing the in-game DLL
  / mod payload).
- Whatever generates per-developer config (`GamePaths.props.template`,
  etc.).
- Any post-build deploy targets that aren't superseded by the
  installer (they're useful in dev).

The migration is genuinely small: the installer project + 0–4
small seam-interface classes is typically less than 200 LOC
total.

## Bitness: x64 launcher + 32-bit game

CAMM's launcher exe is always x64 (the embedded x64 Tolk DLLs and
the AOT-native code path target x64 specifically). If your **target
game is 32-bit** (Civ V is x86, several older titles too), this is
fine — there's no DLL bitness mismatch within the launcher process.

The IFEO redirect intercepts the 32-bit game by exe filename
regardless of bitness, and `CAMM.ProcessLauncher` spawns the game
via `DEBUG_PROCESS` which crosses bitness without trouble. The
launcher and the game are separate processes; the only point of
overlap is the launcher tailing the game's log file, which is just
bytes.

The mod payload may itself need to contain 32-bit DLLs for the game
to load (e.g. a Lua proxy DLL alongside a 32-bit Tolk runtime). Make
that a separate `ModPayload` whose contents target the game's
bitness, and embed it from a separate source directory in your
csproj. Civ V Access does exactly this: launcher x64, `proxy/`
payload x86, `dlc/` payload bitness-neutral, `engine/` payload x86.

## Common questions

**"My update check is failing — `GitHubReleasesOwner` / `Repo`
aren't set yet."** Leave all three GitHub fields null. CAMM detects
auto-update is disabled and skips the check entirely. Stand up the
release pipeline when you're ready; fill in the fields then.

**"I have multiple destinations to deploy to (DLC payload here,
proxy DLL there, an asset bundle somewhere else)."** Use multiple
`ModPayload` entries in the list. Each gets its own embed-resource
prefix (`<LogicalName>name/...`) in your csproj and its own
destination directory.

**"My mod's payload contains a single DLL that drops into the game
root."** That's fine. Make a payload directory containing just that
DLL, `DefaultDestination` points at the game root directory, CAMM
extracts your one file. (Note: uninstall removes only the files
CAMM wrote, not the whole destination dir — safe even when the
destination is a shared dir.)

**"My mod doesn't use `#SCREENREADER`-prefixed log lines — speech
happens in-process via my own Tolk binding."** That's installer-
only mode. Don't set `Sanitizer` / `MarkerProtocol` / `GameInstance`
/ `IfeoTargetExeNames` / `GameProcessNames`. CAMM gives you the
wizard + Apps & Features + auto-update; speech routing stays your
mod's responsibility.

**"Dev-mode source-discovery isn't finding my source folder."**
CAMM's dev-mode walk is parent-walk + one-step-down — it checks
the launcher exe's containing dir, its parent, its grandparent,
etc., and at each step looks for an immediate child directory
matching `FolderName` (plus the sentinel inside it). It does NOT
recurse deeply. If your source is two-plus segments below the
launcher exe (e.g. `repo/src/dlc/<your folder>`), dev-mode
discovery silently no-ops and the launcher falls through to
embedded-resource extraction. That's fine — install/update flows
read from embedded resources regardless. You only lose the
faster dev edit-build-launch loop (rebuild the launcher and embed
resources update, vs. just rebuilding the mod source).

**"I want to localize CAMM's visible strings into German."** Copy
`camm/Camm/lang/en.json` to your launcher project's
`lang/de.json` (or `de-DE.json`), translate the values, leave the
keys + `__*__` substitution tokens untouched. Add it as an
EmbeddedResource (or ship it as a loose file next to your launcher
exe — both work). CAMM auto-detects `CultureInfo.CurrentUICulture`
at startup.

**"Can I customize the install wizard pages?"** Not without forking
CAMM. The wizard's per-page UI is fixed; only the strings + the
`IGameInstance.GetLaunchAnnouncement` (which never appears in the
wizard) are adopter-customizable. If your install needs custom UI,
this is a CAMM limitation worth raising as an issue.

## Reference adopter

[civ-vi-access](https://github.com/nromey/civ-vi-access) is the
canonical example. The relevant files:

- `CivViAccess/Program.cs` — manifest + RunAsync
- `CivViAccess/CivViGameInstance.cs` — IGameInstance impl
- `CivViAccess/Speech/CivViMessageSanitizer.cs` — IMessageSanitizer impl
- `CivViAccess/Speech/CivViScreenReaderMarkerProtocol.cs` — IScreenReaderMarkerProtocol impl
- `CivViAccess/CivViAccess.csproj` — csproj boilerplate
- `CivViAccess/app.manifest` — Common Controls v6 declaration
- `.github/workflows/release.yml` — Azure Trusted Signing release pipeline

If you're stuck, compare against these.
