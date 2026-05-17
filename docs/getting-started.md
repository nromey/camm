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
         app.manifest from civ-vi-access. -->
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

Copy `civ-vi-access`'s `CivViAccess/app.manifest` verbatim — it
declares the Common Controls v6 dependency required by
`TaskDialogIndirect` (CAMM's `ChannelPickerDialog` and several
wizard dialogs). Without this, the first TaskDialog call throws
`EntryPointNotFoundException` at runtime.

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

    // Launcher mode: speech bridge
    Sanitizer = new YourGameMessageSanitizer(),
    MarkerProtocol = new YourGameScreenReaderMarkerProtocol(),
    GameInstance = new YourGameInstance(),
```

If you don't fill those in, CAMM detects "installer-only mode"
(`GameInstance is null`) and skips IFEO redirect, log-tail speech,
and game-launch lifecycle entirely. Adopter ships a launcher exe
that's just install + update + uninstall.

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
