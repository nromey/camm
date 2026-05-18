using System.Diagnostics;
using Camm.Localization;
using Camm.Speech;
using Camm.Wizard;
using DavyKager;

namespace Camm;

// Static entry point for CAMM-built launchers. The consumer's
// Program.cs is typically a manifest construct + RunAsync call:
//
//     return await CammHost.RunAsync(args, BuildManifest());
//
// RunAsync handles two operating modes selected by the manifest:
//
//   Launcher mode (GameInstance non-null) — full chameleon launcher:
//     pending self-update, payload rehydrate, Tolk bootstrap, args
//     dispatch, transparent-invocation detection, bare-exe install
//     trigger, Already-Installed dialog, update check, game launch
//     via the configured IGameInstance, log-tail speech, lifecycle
//     watch, closed announcement.
//
//   Installer-only mode (GameInstance null) — install / update /
//     uninstall only. Used by mods whose runtime lives inside the
//     game's process (Harmony DLL, BepInEx plugin, etc.) where
//     there's no launcher exe to IFEO-redirect through. CAMM exits
//     cleanly after handling args + install + update, never trying
//     to spawn the game.
//
// Per-mod knowledge enters via CammModManifest's fields.
public static class CammHost
{
    private static CammModManifest? _manifest;

    public static CammModManifest Manifest =>
        _manifest ?? throw new InvalidOperationException(
            "CammHost not initialized. Call CammHost.Initialize(manifest) " +
            "(or use the unified RunAsync entry point) before invoking any " +
            "other CAMM module.");

    public static void Initialize(CammModManifest manifest)
    {
        if (_manifest is not null)
        {
            throw new InvalidOperationException(
                "CammHost.Initialize has already been called this process. " +
                "Manifest can only be set once.");
        }
        _manifest = manifest;
    }

    // Unified entry point. Wraps the entire launcher lifecycle.
    // Returns process exit code: 0 on normal completion, non-zero on
    // failure conditions specific to the mode (game-not-found,
    // game-startup-timeout, install-failed, etc.).
    public static async Task<int> RunAsync(string[] args, CammModManifest manifest)
    {
        Initialize(manifest);
        Logger.StartSession("startup");
        if (manifest.ModPayloads is null || manifest.ModPayloads.Count == 0)
        {
            throw new InvalidOperationException(
                "CammModManifest.ModPayloads must contain at least one ModPayload.");
        }

        try { Console.Title = Strings.Get("Console.Title"); }
        catch { /* console may be redirected */ }

        // Step 1: pending self-update.
        Logger.Info("Step 1: ApplyPendingSelfUpdateAndRelaunchIfNeeded");
        Updater.ApplyPendingSelfUpdateAndRelaunchIfNeeded();

        // Step 1b: rehydrate each payload from embedded resources if
        // the redeploy marker is present (self-update applied).
        if (File.Exists(Updater.RedeployMarkerPath))
        {
            Logger.Info("Step 1b: redeploy-mod marker present, rehydrating payloads");
            try
            {
                foreach (var payload in manifest.ModPayloads)
                {
                    var prior = ModFiles.ReadManifestForPayload(payload);
                    if (prior is not null)
                    {
                        ModFiles.RemoveByManifest(prior);
                        Logger.Info($"  Cleaned prior install of payload '{payload.Name}' ({prior.Files.Count} files)");
                    }
                    var installed = ModFiles.ExtractTo(payload);
                    Logger.Info($"  Rehydrated payload '{payload.Name}': {installed.Files.Count} files to {installed.DestinationRoot}");
                }
                File.Delete(Updater.RedeployMarkerPath);
            }
            catch (Exception ex)
            {
                Logger.Exception("Mod rehydrate failed (will retry on next launch)", ex);
            }
        }

        // Step 2: Tolk bootstrap.
        Logger.Info("Step 2: TolkBootstrap.PrepareRuntime");
        try { TolkBootstrap.PrepareRuntime(); Logger.Info("  PrepareRuntime returned"); }
        catch (Exception ex) { Logger.Exception("PrepareRuntime threw", ex); throw; }

        Logger.Info("Step 3: AccessibleOutputHandler init");
        AccessibleOutputHandler accessibleOutput;
        try
        {
            accessibleOutput = new AccessibleOutputHandler();
            try
            {
                var reader = Tolk.DetectScreenReader();
                Logger.Info($"  Tolk.DetectScreenReader: '{reader ?? "(null - no screen reader detected)"}'");
                Logger.Info($"  Tolk.HasSpeech: {Tolk.HasSpeech()}");
                Logger.Info($"  Tolk.IsLoaded: {Tolk.IsLoaded()}");
                Logger.Info($"  Tolk.HasBraille: {Tolk.HasBraille()}");
            }
            catch (Exception detectEx) { Logger.Exception("Tolk detection probes threw", detectEx); }
        }
        catch (Exception ex)
        {
            Logger.Exception("AccessibleOutputHandler construction threw", ex);
            throw;
        }
        var textOutput = new TextOutputHandler();
        var mediator = new Mediator(accessibleOutput, textOutput);

        void Log(string msg) { Console.WriteLine(msg); Logger.Info($"LOG: {msg}"); }
        void Speak(string msg)
        {
            Log(msg);
            Logger.Info($"SPEAK call: {msg}");
            try { accessibleOutput.Speak(msg); Logger.Info("  Speak returned"); }
            catch (Exception ex) { Logger.Exception("Speak threw", ex); }
        }

        // ---- Args dispatch (mode-agnostic) ----

        if (HasFlag(args, "--install")) { Installer.Install(Log, Speak); return 0; }
        if (HasFlag(args, "--uninstall")) { Installer.Uninstall(Log, Speak); return 0; }
        if (HasFlag(args, "--version") || HasFlag(args, "--about")) { PrintAbout(Speak); return 0; }
        if (HasFlag(args, "--config")) { return DoConfig(Log); }
        if (HasFlag(args, "--wizard-test"))
        {
            if (OperatingSystem.IsWindows())
            {
                Log("Opening install wizard (--wizard-test mode)...");
                InstallWizardForm.Run();
                Log("Wizard closed.");
            }
            return 0;
        }
        if (HasFlag(args, "--install-from-wizard"))
        {
            try { Installer.ApplyInstall(Log, Speak); return 0; }
            catch (Exception ex)
            {
                Logger.Exception("ApplyInstall (--install-from-wizard) threw", ex);
                return 1;
            }
        }

        // ---- Transparent invocation (launcher mode only) ----
        bool transparentInvocation = false;
        string transparentGamePath = "";
        string[] passthroughGameArgs = Array.Empty<string>();
        if (manifest.IfeoTargetExeNames is { Length: > 0 })
        {
            transparentInvocation = IfeoInstaller.TryGetTransparentInvocationTarget(
                args, out transparentGamePath);
            if (transparentInvocation && args.Length > 1)
            {
                passthroughGameArgs = args[1..];
            }
        }
        Logger.Info($"transparentInvocation={transparentInvocation}, transparentGamePath={transparentGamePath}");

        // ---- Dev-mode detection (any payload's source dir found?) ----
        var devSourceDirs = ModDeployer.FindAllSourceDirs();
        bool isDevCheckout = devSourceDirs.Values.Any(v => v is not null);

        // ---- Bare-exe ----
        if (args.Length == 0 && !isDevCheckout)
        {
            var bareExeResult = HandleBareExe(Log, Speak);
            if (bareExeResult is int code) return code;
        }

        Logger.Info("Past install/uninstall/version routing");
        Console.WriteLine(Strings.Get("Console.Initializing"));

        // ---- Update check ----
        var settings = LauncherSettings.LoadOrCreate(LauncherSettings.DefaultPath);
        if (manifest.AutoUpdateEnabled && settings.UpdateChannel != UpdateChannel.Off && !isDevCheckout)
        {
            try
            {
                Log("Checking for updates...");
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                var client = new GitHubReleasesClient(http);
                var release = await client.GetLatestForChannelAsync(settings.UpdateChannel);

                if (release is not null && release.Version.CompareTo(SemVer.Current()) > 0)
                {
                    var updater = new Updater(http, Log, Speak);
                    var result = await updater.ApplyAsync(release);
                    switch (result)
                    {
                        case UpdateResult.AppliedModOnly:
                        case UpdateResult.AppliedBoth:
                            Speak($"Update to {release.Version} complete.");
                            break;
                        case UpdateResult.LauncherStagedOnly:
                            Speak($"Launcher update to {release.Version} staged. It will take effect next launch.");
                            break;
                        case UpdateResult.NothingToDo:
                            Log($"Release {release.TagName} had no applicable assets.");
                            break;
                    }
                }
                else
                {
                    Log("Mod is up to date.");
                }
            }
            catch (Exception ex)
            {
                var msg = $"Update check failed: {ex.Message}. Continuing with installed version.";
                Console.Error.WriteLine(msg);
                Logger.Warn(msg);
            }
        }
        else if (!manifest.AutoUpdateEnabled)
        {
            Logger.Info("Auto-update disabled in manifest (no GitHub releases owner/repo configured); skipping update check.");
        }

        // ---- Dev-mode payload deploy ----
        foreach (var payload in manifest.ModPayloads)
        {
            var src = devSourceDirs[payload.Name];
            if (src is null) continue;
            try
            {
                var copied = ModDeployer.Deploy(src, payload.DefaultDestination());
                Console.WriteLine($"Deployed payload '{payload.Name}': {copied} file(s) {src} -> {payload.DefaultDestination()}");
            }
            catch (Exception ex)
            {
                var msg = $"Payload '{payload.Name}' dev-mode deploy failed: {ex.Message}.";
                Console.Error.WriteLine(msg);
                accessibleOutput.Speak(msg);
            }
        }
        if (!isDevCheckout)
        {
            Console.WriteLine("No dev-mode source dir found near launcher exe; using installed payload as-is.");
        }

        // ---- Installer-only mode exits here. No game launch. ----
        if (manifest.IsInstallerOnly)
        {
            Log($"{manifest.DisplayName} install / update flow complete. " +
                "(Installer-only mode — no game-launch step.)");
            return 0;
        }

        // ---- Launcher mode below this line. GameInstance is non-null. ----
        var gameInstance = manifest.GameInstance!;

        var gameExePath = transparentInvocation
            ? transparentGamePath
            : gameInstance.FindGameExe();

        if (string.IsNullOrEmpty(gameExePath) || !File.Exists(gameExePath))
        {
            var msg = Strings.Get("Speech.GameNotFound");
            Console.Error.WriteLine($"{msg} (resolved path: {gameExePath ?? "(null)"})");
            accessibleOutput.Speak(msg);
            return 1;
        }

        // Log-tail bridge: only configured when both seam interfaces
        // are set. Adopters whose mod speaks in-process via their own
        // Tolk binding (Civ V Access pattern) leave Sanitizer +
        // MarkerProtocol null and CAMM skips the log-tail loop while
        // still doing the rest of launcher mode (spawn + lifecycle).
        string? logFilePath = null;
        long preLaunchLogSize = 0L;
        if (manifest.LogTailEnabled)
        {
            logFilePath = gameInstance.GetLogFilePath();
            preLaunchLogSize = File.Exists(logFilePath)
                ? new FileInfo(logFilePath).Length
                : 0L;
        }

        var launchAnnouncement = gameInstance.GetLaunchAnnouncement();
        Console.WriteLine($"Launching {gameExePath}...");
        Speak(launchAnnouncement);

        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAllGameProcesses();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            KillAllGameProcesses();
        };

        Logger.Info($"Calling ProcessLauncher.LaunchBypassingIfeo({gameExePath})");
        try
        {
            var spawnedPid = ProcessLauncher.LaunchBypassingIfeo(gameExePath, passthroughGameArgs);
            Logger.Info($"  LaunchBypassingIfeo returned pid={spawnedPid}");
        }
        catch (Exception ex)
        {
            Logger.Exception("LaunchBypassingIfeo threw", ex);
            throw;
        }

        var consoleHwnd = WindowFocusManager.GetConsoleWindowHandle();
        if (WindowFocusManager.EnsureForeground(TimeSpan.FromSeconds(15)))
        {
            WindowFocusManager.StartFollowFocus(consoleHwnd);
        }
        else
        {
            accessibleOutput.Speak(Strings.Get("Speech.ForegroundFailed"));
        }

        if (manifest.LogTailEnabled && logFilePath is not null)
        {
            Logger.Info($"Waiting for {manifest.TargetGameDisplayName} log file to appear");
            Console.WriteLine($"Waiting for {manifest.TargetGameDisplayName} log file...");

            var logFileWatcher = new LogTailSpeaker(mediator);
            var startupTimeout = TimeSpan.FromMinutes(2);
            var startupStart = DateTime.UtcNow;
            bool seenAlive = false;

            while (!File.Exists(logFilePath))
            {
                if (AnyGameProcessRunning())
                {
                    seenAlive = true;
                }
                else if (seenAlive)
                {
                    Console.WriteLine($"{manifest.TargetGameDisplayName} exited before creating a log file. Launcher exiting.");
                    return 0;
                }
                else if (DateTime.UtcNow - startupStart > startupTimeout)
                {
                    var msg = Strings.Get("Speech.GameStartupTimeout");
                    Console.Error.WriteLine(msg);
                    accessibleOutput.Speak(msg);
                    return 2;
                }

                Thread.Sleep(2000);
            }

            Logger.Info($"Log file appeared at {logFilePath}, starting WatchLogFile from offset {preLaunchLogSize}");
            Console.WriteLine("Log file found. Watching...");
            _ = Task.Run(() =>
            {
                try { logFileWatcher.WatchLogFile(logFilePath, preLaunchLogSize); }
                catch (Exception ex) { Logger.Exception("WatchLogFile threw", ex); }
            });
        }
        else
        {
            Logger.Info("Log-tail bridge disabled (Sanitizer/MarkerProtocol null); skipping log file watch.");
            Console.WriteLine($"Waiting for {manifest.TargetGameDisplayName} to exit...");
        }

        while (AnyGameProcessRunning())
        {
            Thread.Sleep(2000);
        }

        Console.WriteLine($"{manifest.TargetGameDisplayName} closed. Launcher exiting.");
        accessibleOutput.Speak(gameInstance.GetClosedAnnouncement());
        return 0;
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static bool HasFlag(string[] args, string flag)
    {
        foreach (var a in args)
        {
            if (string.Equals(a, flag, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private static void PrintAbout(Action<string> speak)
    {
        var manifest = Manifest;
        var version = SemVer.Current();
        var exe = Environment.ProcessPath ?? "<unknown>";
        var channel = "(default)";
        try
        {
            var settings = LauncherSettings.LoadOrCreate(LauncherSettings.DefaultPath);
            channel = settings.UpdateChannel.ToString().ToLowerInvariant();
        }
        catch { /* fall through to default label */ }

        string installState = "not installed";
        if (OperatingSystem.IsWindows())
        {
            try
            {
                if (manifest.IfeoTargetExeNames is { Length: > 0 })
                {
                    var reg = IfeoInstaller.GetRegisteredLauncherPath();
                    if (reg is not null)
                    {
                        installState = $"installed ({manifest.TargetGameLauncherName} routes through {reg.Trim('"')})";
                    }
                }
                else
                {
                    // Installer-only mode: detect by checking for the
                    // installed launcher exe rather than the IFEO key.
                    var installedExe = Path.Combine(Installer.DefaultInstallDir, Installer.LauncherExeName);
                    if (File.Exists(installedExe))
                    {
                        installState = $"installed at {Installer.DefaultInstallDir}";
                    }
                }
            }
            catch { /* HKLM read failed; leave default */ }
        }

        // Use a mode-aware product label ("__DISPLAY_NAME__ Launcher"
        // for launcher mode, "__DISPLAY_NAME__ Installer" for
        // installer-only). Strings.Get auto-picks the InstallerOnly
        // variant when IsInstallerOnly is true.
        var productLabel = Strings.Get("About.ProductLabel");
        Console.WriteLine($"{productLabel} {version}");
        Console.WriteLine($"  Running from: {exe}");
        Console.WriteLine($"  Install state: {installState}");
        Console.WriteLine($"  Update channel: {channel}");
        if (!string.IsNullOrEmpty(manifest.ProjectUrl))
        {
            Console.WriteLine($"  Project: {manifest.ProjectUrl}");
        }

        speak($"{productLabel} version {version}. {installState}. Update channel: {channel}.");
    }

    private static int DoConfig(Action<string> log)
    {
        var manifest = Manifest;
        var configSettings = LauncherSettings.LoadOrCreate(LauncherSettings.DefaultPath);
        if (!OperatingSystem.IsWindows()) return 0;

        var picked = ChannelPickerDialog.Show(configSettings.UpdateChannel);
        if (picked is UpdateChannel choice)
        {
            configSettings.UpdateChannel = choice;
            try
            {
                configSettings.Save(LauncherSettings.DefaultPath);
                log($"Update channel saved: {choice}");
                Dialogs.ShowInfo(
                    Strings.Get("Settings.SavedTitle"),
                    Strings.Get("Settings.SavedBodyPrefix") + choice +
                    Strings.Get("Settings.SavedBodySuffix"));
            }
            catch (Exception ex)
            {
                log($"Failed to save settings: {ex.Message}");
                Dialogs.ShowError(
                    Strings.Get("Settings.ErrorTitle"),
                    Strings.Get("Settings.ErrorPrefix") + ex.Message);
            }
        }
        else
        {
            log("User cancelled channel change; settings unchanged.");
        }
        return 0;
    }

    // Bare-exe handler. Returns:
    //   - int: handled (caller should exit with this code)
    //   - null: fall through (running from install dir; main launch
    //           flow follows in launcher mode)
    private static int? HandleBareExe(Action<string> log, Action<string> speak)
    {
        var manifest = Manifest;
        bool installed = false;
        bool readOk = false;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                if (manifest.IfeoTargetExeNames is { Length: > 0 })
                {
                    installed = IfeoInstaller.GetRegisteredLauncherPath() is not null;
                }
                else
                {
                    // Installer-only mode: probe install dir for the
                    // launcher exe.
                    var installedExe = Path.Combine(Installer.DefaultInstallDir, Installer.LauncherExeName);
                    installed = File.Exists(installedExe);
                }
                readOk = true;
            }
        }
        catch { readOk = false; }

        if (readOk && !installed)
        {
            speak(Strings.Get("Speech.InstallStarting"));
            Installer.Install(log, speak);
            return 0;
        }

        if (!OperatingSystem.IsWindows() || !readOk || !installed) return null;

        var currentExe = Environment.ProcessPath ?? "";
        var installedExePath = Path.Combine(Installer.DefaultInstallDir, Installer.LauncherExeName);
        var runningFromInstallDir = string.Equals(
            Path.GetFullPath(currentExe),
            Path.GetFullPath(installedExePath),
            StringComparison.OrdinalIgnoreCase);

        // Launcher-mode adopters running from the install dir fall
        // through to the main launch flow. Installer-only adopters
        // running from the install dir have nothing to do (no game
        // to launch), so they get the Already-Installed dialog too.
        if (runningFromInstallDir && !manifest.IsInstallerOnly) return null;

        const int ID_REINSTALL = 101;
        const int ID_UNINSTALL = 102;
        const int ID_SETTINGS = 103;
        const int ID_EXIT = 104;
        var choice = Dialogs.ShowChoice(
            title: Strings.Get("AlreadyInstalled.Title"),
            mainInstruction: Strings.Get("AlreadyInstalled.Instruction"),
            content: Strings.Get("AlreadyInstalled.ContentPrefix") + Installer.DefaultInstallDir,
            choices: new[]
            {
                new Dialogs.ChoiceButton(ID_REINSTALL,
                    Strings.Get("AlreadyInstalled.Reinstall.Heading"),
                    Strings.Get("AlreadyInstalled.Reinstall.Note")),
                new Dialogs.ChoiceButton(ID_UNINSTALL,
                    Strings.Get("AlreadyInstalled.Uninstall.Heading"),
                    Strings.Get("AlreadyInstalled.Uninstall.Note")),
                new Dialogs.ChoiceButton(ID_SETTINGS,
                    Strings.Get("AlreadyInstalled.Settings.Heading"),
                    Strings.Get("AlreadyInstalled.Settings.Note")),
                new Dialogs.ChoiceButton(ID_EXIT,
                    Strings.Get("AlreadyInstalled.Exit.Heading"),
                    Strings.Get("AlreadyInstalled.Exit.Note")),
            },
            defaultChoiceId: ID_REINSTALL);

        switch (choice)
        {
            case ID_REINSTALL:
                Installer.Install(log, speak);
                return 0;
            case ID_UNINSTALL:
                Installer.Uninstall(log, speak);
                return 0;
            case ID_SETTINGS:
                var alreadyInstalledSettings = LauncherSettings.LoadOrCreate(LauncherSettings.DefaultPath);
                var picked = ChannelPickerDialog.Show(alreadyInstalledSettings.UpdateChannel);
                if (picked is UpdateChannel newChan)
                {
                    alreadyInstalledSettings.UpdateChannel = newChan;
                    try
                    {
                        alreadyInstalledSettings.Save(LauncherSettings.DefaultPath);
                        log($"Channel saved: {newChan}");
                    }
                    catch (Exception ex) { log($"Save failed: {ex.Message}"); }
                    Dialogs.ShowInfo(
                        Strings.Get("Settings.SavedTitle"),
                        Strings.Get("Settings.SavedBodyPrefix") + newChan +
                        Strings.Get("Settings.SavedFromAlreadyInstalledBodySuffix"));
                }
                else
                {
                    log("Channel change cancelled.");
                }
                return 0;
            default:
                log("User exited from already-installed dialog.");
                return 0;
        }
    }

    private static bool AnyGameProcessRunning()
    {
        var names = Manifest.GameProcessNames;
        if (names is null || names.Length == 0) return false;
        foreach (var name in names)
        {
            if (Process.GetProcessesByName(name).Length > 0) return true;
        }
        return false;
    }

    private static void KillAllGameProcesses()
    {
        var names = Manifest.GameProcessNames;
        if (names is null) return;
        foreach (var name in names)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
            }
        }
    }
}
