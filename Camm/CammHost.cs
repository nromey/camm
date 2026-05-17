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
// RunAsync handles: pending self-update, Tolk bootstrap, args
// dispatch (--install / --uninstall / --version / --config /
// --install-from-wizard / --wizard-test), transparent invocation
// detection, bare-exe install trigger, Already-Installed dialog,
// update check + apply, game launch via the configured
// IGameInstance, log-tail speech, lifecycle watch.
//
// Per-game knowledge enters via CammModManifest's IGameInstance,
// IMessageSanitizer, IScreenReaderMarkerProtocol fields.
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
    // Returns process exit code: 0 on normal completion, non-zero
    // on failure conditions specific to the mode (game-not-found,
    // game-startup-timeout, install-failed, etc.).
    public static async Task<int> RunAsync(string[] args, CammModManifest manifest)
    {
        Initialize(manifest);
        Logger.StartSession("startup");

        try { Console.Title = $"{manifest.DisplayName} Launcher"; }
        catch { /* console may be redirected */ }

        // Step 1: complete any pending self-update from a previous
        // run. If a swap happens, we re-launch ourselves and
        // Environment.Exit, so anything below this line runs against
        // the newest launcher version.
        Logger.Info("Step 1: ApplyPendingSelfUpdateAndRelaunchIfNeeded");
        Updater.ApplyPendingSelfUpdateAndRelaunchIfNeeded();

        // Step 1b: if a self-update just happened (or the marker was
        // left by the previous launcher version's update flow), the
        // deployed mod is stale relative to our newly-current
        // version. Rehydrate from embedded resources, then delete the
        // marker.
        if (File.Exists(Updater.RedeployMarkerPath))
        {
            Logger.Info("Step 1b: redeploy-mod marker present, rehydrating mod from embedded resources");
            try
            {
                var count = ModFiles.ExtractTo(ModDeployer.DefaultDestination);
                Logger.Info($"  Rehydrated {count} mod files to {ModDeployer.DefaultDestination}");
                File.Delete(Updater.RedeployMarkerPath);
            }
            catch (Exception ex)
            {
                Logger.Exception("Mod rehydrate failed (will retry on next launch)", ex);
            }
        }

        // Step 2: make Tolk's native sidecars loadable by P/Invoke.
        Logger.Info("Step 2: TolkBootstrap.PrepareRuntime");
        try { TolkBootstrap.PrepareRuntime(); Logger.Info("  PrepareRuntime returned"); }
        catch (Exception ex) { Logger.Exception("PrepareRuntime threw", ex); throw; }

        Logger.Info("Step 3: AccessibleOutputHandler init");
        AccessibleOutputHandler accessibleOutput;
        try
        {
            accessibleOutput = new AccessibleOutputHandler();
            Logger.Info("  AccessibleOutputHandler constructed");
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

        // ---- Args dispatch ----

        if (HasFlag(args, "--install"))
        {
            Installer.Install(Log, Speak);
            return 0;
        }
        if (HasFlag(args, "--uninstall"))
        {
            Installer.Uninstall(Log, Speak);
            return 0;
        }
        if (HasFlag(args, "--version") || HasFlag(args, "--about"))
        {
            PrintAbout(Speak);
            return 0;
        }
        if (HasFlag(args, "--config"))
        {
            return DoConfig(Log);
        }
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
            try
            {
                Installer.ApplyInstall(Log, Speak);
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Exception("ApplyInstall (--install-from-wizard) threw", ex);
                return 1;
            }
        }

        // ---- Transparent invocation: Windows IFEO prepended us to the
        //      game-exe launch, so args[0] is the path to the real game
        //      binary and args[1..] are the original args Steam/shortcut
        //      passed. ----
        bool transparentInvocation = IfeoInstaller.TryGetTransparentInvocationTarget(
            args, out var transparentGamePath);
        string[] passthroughGameArgs = transparentInvocation && args.Length > 1
            ? args[1..]
            : Array.Empty<string>();
        Logger.Info($"transparentInvocation={transparentInvocation}, transparentGamePath={transparentGamePath}");

        // ---- Bare-exe: no args, not in dev checkout = user double-clicked
        //      the downloaded installer. Offer install (if not installed)
        //      or the Already-Installed dialog. ----
        if (args.Length == 0 && ModDeployer.FindModSourceDir() is null)
        {
            var bareExeResult = HandleBareExe(Log, Speak);
            if (bareExeResult is int code) return code;
            // null = fall through to main launch flow (running from
            // install dir with no args is fine).
        }

        Logger.Info("Reaching main launch flow (past install/uninstall/version routing)");
        Console.WriteLine($"{manifest.DisplayName} Launcher initializing...");

        // ---- Update check (respects UpdateChannel = stable / latest / off) ----
        //
        // Skipped when running from a dev checkout. The dev launcher's
        // version is typically ahead of any release.
        var settings = LauncherSettings.LoadOrCreate(LauncherSettings.DefaultPath);
        var modSourceDir = ModDeployer.FindModSourceDir();
        bool isDevCheckout = modSourceDir is not null;

        if (settings.UpdateChannel != UpdateChannel.Off && !isDevCheckout)
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

        // ---- Dev-mode mod deploy (no-op in shipped installs) ----
        if (modSourceDir is not null)
        {
            try
            {
                var copied = ModDeployer.Deploy(modSourceDir, ModDeployer.DefaultDestination);
                Console.WriteLine($"Deployed {copied} mod file(s): {modSourceDir} -> {ModDeployer.DefaultDestination}");
            }
            catch (Exception ex)
            {
                var msg = $"Mod deploy failed: {ex.Message}. Launching with whatever is currently in the mod folder.";
                Console.Error.WriteLine(msg);
                accessibleOutput.Speak(msg);
            }
        }
        else
        {
            Console.WriteLine("No mod source dir found near launcher exe; using mod folder as-is.");
        }

        // ---- Locate game binary ----
        var gameExePath = transparentInvocation
            ? transparentGamePath
            : manifest.GameInstance.FindGameExe();

        if (string.IsNullOrEmpty(gameExePath) || !File.Exists(gameExePath))
        {
            var msg = Strings.Get("Speech.GameNotFound");
            Console.Error.WriteLine($"{msg} (resolved path: {gameExePath ?? "(null)"})");
            accessibleOutput.Speak(msg);
            return 1;
        }

        // ---- Pre-launch log size capture ----
        // Used by LogTailSpeaker so we replay nothing from prior sessions.
        var logFilePath = manifest.GameInstance.GetLogFilePath();
        long preLaunchLogSize = File.Exists(logFilePath)
            ? new FileInfo(logFilePath).Length
            : 0L;

        // ---- Launch announcement (per-game, first-launch-aware) ----
        var launchAnnouncement = manifest.GameInstance.GetLaunchAnnouncement();
        Logger.Info($"About to speak launch announcement");
        Console.WriteLine($"Launching {gameExePath}...");
        Speak(launchAnnouncement);

        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAllGameProcesses();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            KillAllGameProcesses();
        };

        // ---- Spawn the game (IFEO-bypass) ----
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

        // ---- Foreground handoff + follow-focus ----
        var consoleHwnd = WindowFocusManager.GetConsoleWindowHandle();
        if (WindowFocusManager.EnsureForeground(TimeSpan.FromSeconds(15)))
        {
            WindowFocusManager.StartFollowFocus(consoleHwnd);
        }
        else
        {
            accessibleOutput.Speak(Strings.Get("Speech.ForegroundFailed"));
        }

        // ---- Wait for the game log to appear, then start tailing ----
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
                Console.WriteLine(
                    $"{manifest.TargetGameDisplayName} exited before creating a log file. Launcher exiting.");
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

        while (AnyGameProcessRunning())
        {
            Thread.Sleep(2000);
        }

        Console.WriteLine($"{manifest.TargetGameDisplayName} closed. Launcher exiting.");
        accessibleOutput.Speak(manifest.GameInstance.GetClosedAnnouncement());
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
                var reg = IfeoInstaller.GetRegisteredLauncherPath();
                if (reg is not null)
                {
                    installState = $"installed ({manifest.TargetGameLauncherName} routes through {reg.Trim('"')})";
                }
            }
            catch { /* HKLM read failed; leave default */ }
        }

        Console.WriteLine($"{manifest.DisplayName} Launcher {version}");
        Console.WriteLine($"  Running from: {exe}");
        Console.WriteLine($"  Install state: {installState}");
        Console.WriteLine($"  Update channel: {channel}");
        if (!string.IsNullOrEmpty(manifest.ProjectUrl))
        {
            Console.WriteLine($"  Project: {manifest.ProjectUrl}");
        }

        speak($"{manifest.DisplayName} Launcher version {version}. {installState}. Update channel: {channel}.");
    }

    private static int DoConfig(Action<string> log)
    {
        var manifest = Manifest;
        // Settings dialog mode. Reached from Apps & Features Modify
        // button, power users running --config, or the Already-
        // Installed dialog's "change settings" branch.
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

    // Bare-exe entry handler. Returns:
    //   - int: handled (caller should exit with this code)
    //   - null: fall through to main launch flow
    private static int? HandleBareExe(Action<string> log, Action<string> speak)
    {
        var manifest = Manifest;
        bool installed = false;
        bool readOk = false;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                installed = IfeoInstaller.GetRegisteredLauncherPath() is not null;
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

        // Already installed AND running from outside the install dir =
        // user double-clicked a downloaded copy of the launcher after
        // a previous install. Offer Reinstall / Uninstall / Settings /
        // Exit instead of silently falling through to launch.
        if (!OperatingSystem.IsWindows() || !readOk || !installed) return null;

        var currentExe = Environment.ProcessPath ?? "";
        var installedExe = Path.Combine(Installer.DefaultInstallDir, Installer.LauncherExeName);
        var runningFromInstallDir = string.Equals(
            Path.GetFullPath(currentExe),
            Path.GetFullPath(installedExe),
            StringComparison.OrdinalIgnoreCase);
        if (runningFromInstallDir) return null;

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
        foreach (var name in Manifest.GameProcessNames)
        {
            if (Process.GetProcessesByName(name).Length > 0) return true;
        }
        return false;
    }

    private static void KillAllGameProcesses()
    {
        foreach (var name in Manifest.GameProcessNames)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* best effort */ }
            }
        }
    }
}
