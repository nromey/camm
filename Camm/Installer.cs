using System.Diagnostics;
using System.Runtime.Versioning;
using Camm.Localization;

namespace Camm;

// First-time install: copy the launcher exe + its sidecar DLLs (Tolk)
// to a stable Program Files location, deploy each ModPayload to its
// destination, and (if running in launcher mode) register the IFEO
// redirect.
//
// CAMM installs to a per-machine path (Program Files\<DisplayName>\)
// rather than per-user (LocalAppData). The IFEO entry the installer
// registers is HKLM-only, so a per-user install path would create a
// mismatch where the redirect points to a launcher that any other
// user account on the machine couldn't read. Keep the binary and its
// activation symmetric. (For installer-only mods with no IFEO entry,
// the Program Files location still works fine — it's just the
// canonical place a Windows app installs to.)
[SupportedOSPlatform("windows")]
public static class Installer
{
    public static string InstallDirName => CammHost.Manifest.DisplayName;

    public static string DefaultInstallDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            InstallDirName);

    public static string LauncherExeName => CammHost.Manifest.LauncherExeName;

    // Open the install wizard.
    public static void Install(Action<string> log, Action<string> speak)
    {
        log("Opening install wizard...");
        var context = new Wizard.InstallContext
        {
            IsDryRun = false,
            IsFirstInstall = !Directory.Exists(DefaultInstallDir),
        };
        Wizard.InstallWizardForm.Run(context);
        if (context.InstallError is null)
        {
            log("Wizard closed.");
        }
        else
        {
            log($"Wizard closed with install error: {context.InstallError}");
        }
    }

    // The post-elevation work, factored out so both the wizard flow
    // (via the launcher's --install-from-wizard entry point) and any
    // future direct-invocation caller can reuse it. MUST be called
    // from an elevated process — the caller is responsible for
    // elevation handling and for any pre-install UI.
    //
    // Steps: copy launcher exe + Tolk DLLs to install dir, deploy each
    // ModPayload to its destination (writing per-payload install
    // manifests as it goes), register IFEO redirect (launcher mode
    // only), register Apps & Features.
    public static void ApplyInstall(Action<string> log, Action<string> speak)
    {
        var manifest = CammHost.Manifest;
        var destDir = DefaultInstallDir;
        Directory.CreateDirectory(destDir);

        var sourceExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine current launcher exe path.");
        var installedLauncher = Path.Combine(destDir, LauncherExeName);

        // Two-step copy of the launcher itself: file copy + Tolk
        // sidecar extraction. Do NOT iterate AppContext.BaseDirectory
        // — when the user runs a freshly-downloaded loose .exe, that
        // directory is typically Downloads, full of unrelated files.
        // Targeted copy only.

        var sameAsRunning = string.Equals(
            Path.GetFullPath(sourceExe),
            Path.GetFullPath(installedLauncher),
            StringComparison.OrdinalIgnoreCase);

        if (sameAsRunning)
        {
            log($"Already running from install location {installedLauncher}; skipping exe copy.");
        }
        else
        {
            try
            {
                File.Copy(sourceExe, installedLauncher, overwrite: true);
                log($"Copied launcher to {installedLauncher}.");
            }
            catch (IOException)
            {
                File.Copy(sourceExe, installedLauncher + ".pending", overwrite: true);
                log($"Existing launcher in use; staged update at {installedLauncher}.pending.");
            }
        }

        TolkBootstrap.ExtractTo(destDir);
        log($"Tolk sidecars present in {destDir}.");

        if (!File.Exists(installedLauncher))
        {
            throw new FileNotFoundException(
                $"Expected launcher exe at {installedLauncher} after install. " +
                "Did the assembly name change without updating manifest.LauncherExeName?");
        }

        // Deploy each ModPayload to its destination, replacing any
        // prior install (read the per-payload manifest from a prior
        // run and clean up its files first so we don't leave orphans
        // when the payload's file set shrinks between versions).
        var installedPayloads = new Dictionary<string, PayloadInstallManifest>(StringComparer.Ordinal);
        foreach (var payload in manifest.ModPayloads)
        {
            // Clean prior install of this payload (if any) before
            // dropping new files. RemoveByManifest also restores any
            // BackupAndReplace .original files; subsequent ExtractTo
            // will re-create them on top of the restored vanilla files,
            // keeping the backup-tracking accurate across reinstalls.
            var prior = ModFiles.ReadManifestForPayload(payload);
            if (prior is not null)
            {
                ModFiles.RemoveByManifest(prior);
                log($"Cleaned prior install of payload '{payload.Name}' ({prior.Files.Count} files).");
            }

            try
            {
                var installed = ModFiles.ExtractTo(payload);
                installedPayloads[payload.Name] = installed;
                log($"Deployed payload '{payload.Name}': {installed.Files.Count} files to {installed.DestinationRoot}.");
            }
            catch (Exception ex)
            {
                log($"Payload '{payload.Name}' deploy failed: {ex.Message}. " +
                    "Install continues with remaining payloads.");
            }
        }

        // Launcher mode: register IFEO redirect so the target-game
        // exe gets intercepted by our launcher. Installer-only mods
        // have no IFEO targets — skip.
        if (manifest.IfeoTargetExeNames is { Length: > 0 } ifeoTargets)
        {
            IfeoInstaller.Install(installedLauncher);
            log($"Registered IFEO redirect for {string.Join(" + ", ifeoTargets)}.");
        }
        else
        {
            log("No IFEO targets configured (installer-only mode); skipping IFEO redirect.");
        }

        try
        {
            AppsAndFeaturesRegistration.Register(
                installDir: destDir,
                launcherExePath: installedLauncher,
                version: SemVer.Current().ToString());
            log("Registered in Apps & Features.");
        }
        catch (Exception ex)
        {
            log($"Apps & Features registration failed: {ex.Message}. " +
                "Uninstall via terminal will still work.");
        }

        // Post-install hook for adopters that need to do game-side
        // config work after payload extraction (RimWorld's
        // ModsConfig.xml edit, BepInEx plugin enable list, etc.).
        // Hook runs after Apps & Features registration but before the
        // "installed" announcement; throwing fails the install.
        if (manifest.PostInstallHook is not null)
        {
            log("Running post-install hook...");
            try
            {
                manifest.PostInstallHook(installedPayloads).GetAwaiter().GetResult();
                log("Post-install hook completed.");
            }
            catch (Exception ex)
            {
                Logger.Exception("Post-install hook threw", ex);
                log($"Post-install hook failed: {ex.Message}.");
                throw;
            }
        }

        speak($"{manifest.DisplayName} installed.");
    }

    public static void Uninstall(Action<string> log, Action<string> speak)
    {
        var manifest = CammHost.Manifest;
        if (!IfeoInstaller.IsRunningElevated())
        {
            const int ID_UNINSTALL = 1;
            const int ID_CANCEL = 2;
            var confirm = Dialogs.ShowChoice(
                title: Strings.Get("Installer.Uninstall.ConfirmTitle"),
                mainInstruction: Strings.Get("Installer.Uninstall.ConfirmInstruction"),
                content: Strings.Get("Installer.Uninstall.ConfirmContent"),
                choices: new[]
                {
                    new Dialogs.ChoiceButton(ID_UNINSTALL,
                        Strings.Get("Installer.Uninstall.ConfirmButton.Heading"),
                        Strings.Get("Installer.Uninstall.ConfirmButton.Note")),
                    new Dialogs.ChoiceButton(ID_CANCEL,
                        Strings.Get("Installer.Uninstall.CancelButton.Heading"),
                        Strings.Get("Installer.Uninstall.CancelButton.Note")),
                },
                defaultChoiceId: ID_CANCEL,
                warningIcon: true);
            if (confirm != ID_UNINSTALL)
            {
                speak("Uninstall cancelled.");
                log("Uninstall cancelled by user at confirm dialog.");
                return;
            }

            // Stage a copy of ourselves out of the install dir if we
            // would otherwise lock it during the elevated rerun.
            var currentExe = Environment.ProcessPath
                ?? throw new InvalidOperationException("Cannot determine current exe path.");
            var installedExe = Path.Combine(DefaultInstallDir, LauncherExeName);
            bool runningFromInstallDir = string.Equals(
                Path.GetFullPath(currentExe),
                Path.GetFullPath(installedExe),
                StringComparison.OrdinalIgnoreCase);

            string exeToRelaunch = currentExe;
            if (runningFromInstallDir)
            {
                try
                {
                    var tempDir = Path.Combine(
                        Path.GetTempPath(),
                        manifest.LocalAppDataFolderName + "Uninstall");
                    Directory.CreateDirectory(tempDir);
                    var tempExe = Path.Combine(tempDir, LauncherExeName);
                    File.Copy(currentExe, tempExe, overwrite: true);
                    log($"Staged uninstaller copy to {tempExe} so install dir can be cleaned up.");
                    exeToRelaunch = tempExe;
                }
                catch (Exception ex)
                {
                    log($"Could not stage uninstaller copy: {ex.Message}. " +
                        "Falling back to in-place elevation; install dir will not be removed.");
                }
            }

            RelaunchElevated(exeToRelaunch, "--uninstall");
            Environment.Exit(0);
        }

        if (manifest.IfeoTargetExeNames is { Length: > 0 } ifeoTargets)
        {
            IfeoInstaller.Uninstall();
            log($"Removed IFEO redirect for {string.Join(" + ", ifeoTargets)}.");
        }

        try
        {
            AppsAndFeaturesRegistration.Unregister();
            log("Removed Apps & Features registration.");
        }
        catch (Exception ex)
        {
            log($"Apps & Features unregister failed: {ex.Message}. " +
                "Entry may remain in Settings but won't function.");
        }

        // Remove each payload's deployed files by reading its
        // install-time manifest. Per-file deletion is safe across
        // shared destinations (where deleting the whole dir would
        // nuke other content); for mod-owned destinations the
        // manifest still produces the right result.
        foreach (var payload in manifest.ModPayloads)
        {
            var installed = ModFiles.ReadManifestForPayload(payload);
            if (installed is null)
            {
                log($"No install manifest for payload '{payload.Name}'; nothing to clean up.");
                continue;
            }
            try
            {
                ModFiles.RemoveByManifest(installed);
                ModFiles.DeleteManifestFile(payload);
                log($"Removed {installed.Files.Count} files for payload '{payload.Name}' from {installed.DestinationRoot}.");
            }
            catch (Exception ex)
            {
                log($"Could not remove payload '{payload.Name}': {ex.Message}.");
            }
        }

        bool installDirCleaned = false;
        try
        {
            if (Directory.Exists(DefaultInstallDir))
            {
                Directory.Delete(DefaultInstallDir, recursive: true);
                log($"Removed install directory {DefaultInstallDir}.");
                installDirCleaned = true;
            }
            else
            {
                installDirCleaned = true;
            }
        }
        catch (Exception ex)
        {
            log($"Could not remove install dir {DefaultInstallDir}: {ex.Message}. " +
                "Files may remain; delete the folder manually if desired.");
        }

        speak($"{manifest.DisplayName} uninstalled.");

        var leftInPlaceLine = installDirCleaned
            ? ""
            : "\nNot removed (could not be deleted):\n" +
              "  • Launcher files at " + DefaultInstallDir + "\n" +
              "    (delete that folder manually to finish cleanup)\n";

        Dialogs.ShowInfo(
            Strings.Get("Installer.Uninstall.CompleteTitle"),
            Strings.Get("Installer.Uninstall.CompleteBody") + "\n" +
            (installDirCleaned ? "  • Launcher files at " + DefaultInstallDir + "\n" : "") +
            leftInPlaceLine + "\n" +
            "Click OK to finish.");
    }

    private static void RelaunchElevated(string exe, string arg)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = true,
            Verb = "runas",
            Arguments = arg,
        };
        try { Process.Start(psi); }
        catch (System.ComponentModel.Win32Exception)
        {
            // User declined the UAC prompt.
        }
    }
}
