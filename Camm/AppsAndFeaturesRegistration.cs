using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Camm;

// Register / unregister a CAMM-built mod in Windows' Add/Remove Programs
// (Settings → Apps → Installed Apps in Win10/11). This is the standard
// place users look to remove software — much more discoverable than
// "run the launcher exe with --uninstall from a terminal."
//
// Mechanism: a key under
// HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\<KeyName>
// with documented value names. Windows reads this key at "Installed
// Apps" rendering time. When the user clicks Uninstall in Settings,
// Windows runs whatever UninstallString points to — we point it at
// the launcher's `--uninstall` flag, so the same code path that handles
// terminal-invoked uninstall handles Settings-invoked uninstall.
//
// HKLM is admin-only (matches IFEO). Both Register and Unregister
// are called from elevated Install/Uninstall paths.
//
// All per-mod values (KeyName, DisplayName, Publisher, ProjectUrl)
// come from CammConfig — set by the consumer at startup.
[SupportedOSPlatform("windows")]
public static class AppsAndFeaturesRegistration
{
    private static string UninstallKeyPath =>
        @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\" +
        CammConfig.AppsAndFeaturesKeyName;

    public static void Register(string installDir, string launcherExePath, string version)
    {
        using var key = Registry.LocalMachine.CreateSubKey(UninstallKeyPath, writable: true)
            ?? throw new UnauthorizedAccessException(
                "Failed to open HKLM Uninstall subkey. Are we elevated?");

        key.SetValue("DisplayName", CammConfig.DisplayName, RegistryValueKind.String);
        key.SetValue("DisplayVersion", version, RegistryValueKind.String);
        key.SetValue("Publisher", CammConfig.Publisher, RegistryValueKind.String);
        key.SetValue("InstallLocation", installDir, RegistryValueKind.String);
        key.SetValue("DisplayIcon", launcherExePath, RegistryValueKind.String);

        // UninstallString runs when the user clicks Uninstall in Settings.
        // Quoting matters: install path is in Program Files, has spaces.
        key.SetValue("UninstallString",
            $"\"{launcherExePath}\" --uninstall",
            RegistryValueKind.String);

        // Modify button → launches our --config dialog. Windows runs
        // ModifyPath when the user clicks "Modify" in Apps & Features.
        // This is the canonical place users look to change settings of
        // an installed app, so it's the discovery point for update-
        // channel changes. No NoModify=1 — we want the button.
        key.SetValue("ModifyPath",
            $"\"{launcherExePath}\" --config",
            RegistryValueKind.String);

        // No Repair button — repair isn't a distinct operation; install
        // is idempotent and acts as repair if anything's corrupted.
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

        if (!string.IsNullOrEmpty(CammConfig.ProjectUrl))
        {
            // Project URL for the "Visit website" link in Settings.
            key.SetValue("URLInfoAbout",
                CammConfig.ProjectUrl,
                RegistryValueKind.String);
        }

        // Approximate footprint, in KB. Settings displays this. Best-
        // effort — if directory enumeration fails (permissions, race),
        // skip the EstimatedSize value rather than fail the install.
        try
        {
            long bytes = 0;
            foreach (var file in new DirectoryInfo(installDir).EnumerateFiles("*", SearchOption.AllDirectories))
            {
                bytes += file.Length;
            }
            key.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
        }
        catch { /* best effort */ }
    }

    public static void Unregister()
    {
        try
        {
            Registry.LocalMachine.DeleteSubKeyTree(UninstallKeyPath, throwOnMissingSubKey: false);
        }
        catch { /* best effort — leaves a stale entry, harmless */ }
    }
}
