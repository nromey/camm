using Camm.Localization;

namespace Camm;

// Pulls down a release from GitHub and applies it locally.
//
// SINGLE-ARTIFACT MODEL: the launcher .exe contains the mod tree as
// embedded resources, so a release ships ONE artifact, the launcher
// binary. Asset name pattern comes from CammHost.Manifest.LauncherAssetNamePattern
// ("{0}" gets the version string).
//
// The launcher can't replace itself while running, so the downloaded
// exe lands as "<live exe path>.pending". On next launcher startup
// (before any other init), ApplyPendingSelfUpdateAndRelaunchIfNeeded
// swaps it in. The new launcher then rehydrates its embedded mod into
// the deploy destination on startup (consumer Program.cs reads the
// redeploy-marker that the swap step wrote).
//
// Why a single artifact: keeping launcher + mod in lockstep is
// structurally simpler than coordinating two artifacts with matching
// versions. The launcher is the canonical source of truth for both
// itself and the mod files it should deploy.
public sealed class Updater
{
    private readonly HttpClient _http;
    private readonly Action<string> _log;
    private readonly Action<string> _speak;

    public Updater(HttpClient http, Action<string> log, Action<string> speak)
    {
        _http = http;
        _log = log;
        _speak = speak;
    }

    public async Task<UpdateResult> ApplyAsync(
        ReleaseInfo release,
        CancellationToken ct = default)
    {
        var launcherAssetName = string.Format(
            CammHost.Manifest.LauncherAssetNamePattern, release.Version);
        var launcherAsset = release.FindAsset(launcherAssetName);

        if (launcherAsset is null)
        {
            _log($"Release {release.TagName} has no asset named {launcherAssetName}; nothing to apply.");
            return UpdateResult.NothingToDo;
        }

        _speak(Strings.Get("Speech.UpdateToVersionPrefix") + release.Version +
            Strings.Get("Speech.UpdateToVersionSuffix"));
        _log($"Staging launcher self-update from {launcherAsset.Name}.");
        await StageLauncherUpdateAsync(launcherAsset, ct).ConfigureAwait(false);
        return UpdateResult.LauncherStagedOnly;
    }

    private async Task StageLauncherUpdateAsync(AssetDto asset, CancellationToken ct)
    {
        var currentExe = Environment.ProcessPath
            ?? throw new InvalidOperationException("Cannot determine current launcher exe path.");
        var pendingPath = currentExe + ".pending";
        var tempPath = pendingPath + ".part";

        try { File.Delete(tempPath); } catch { }
        try { File.Delete(pendingPath); } catch { }

        await DownloadAsync(asset.DownloadUrl, tempPath, ct).ConfigureAwait(false);
        File.Move(tempPath, pendingPath);
        _log($"Launcher update staged at {pendingPath}; will apply on next launch.");
    }

    private async Task DownloadAsync(string url, string destPath, CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var http = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = File.Create(destPath);
        await http.CopyToAsync(file, ct).ConfigureAwait(false);
    }

    // Path the swap step writes when it applies a .pending update.
    // The new launcher reads this on startup and rehydrates the mod
    // before continuing, then deletes the marker. Lives in
    // %LocalAppData%\<CammHost.Manifest.LocalAppDataFolderName>\ (user-writable,
    // matches launcher.ini and launcher.log location).
    public static string RedeployMarkerPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                CammHost.Manifest.LocalAppDataFolderName);
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, ".mod-redeploy-needed");
        }
    }

    // Called on launcher startup before anything else. If a .pending exe
    // sits next to us, swap it in and re-launch. Side effect: kills the
    // current process and exits with whatever the spawned child returns.
    public static void ApplyPendingSelfUpdateAndRelaunchIfNeeded()
    {
        var currentExe = Environment.ProcessPath;
        if (currentExe is null) return;

        var pendingPath = currentExe + ".pending";
        if (!File.Exists(pendingPath)) return;

        var backupPath = currentExe + ".old";
        try
        {
            try { File.Delete(backupPath); } catch { }
            File.Move(currentExe, backupPath);
            File.Move(pendingPath, currentExe);
            // Best-effort: drop the backup so it doesn't accumulate. On
            // Windows the old .exe is the very file the OS is executing
            // right now, which is why the rename works but the delete
            // often won't until we exit. Try once, ignore.
            try { File.Delete(backupPath); } catch { }

            // Signal to the just-swapped-in launcher: rehydrate mod
            // from your embedded resources on startup. This is how the
            // single-artifact update model keeps the deployed mod in
            // sync with the launcher version that just installed.
            try { File.WriteAllText(RedeployMarkerPath, DateTime.UtcNow.ToString("O")); }
            catch { /* best effort; worst case is stale mod files until next reinstall */ }

            // Relaunch self with the same args so the user gets the
            // freshly-swapped launcher this run, not next run.
            var args = Environment.GetCommandLineArgs();
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = currentExe,
                UseShellExecute = false,
            };
            for (int i = 1; i < args.Length; i++) psi.ArgumentList.Add(args[i]);
            System.Diagnostics.Process.Start(psi);
            Environment.Exit(0);
        }
        catch
        {
            // Rollback if the swap half-completed. If we got past the
            // first Move, restore the old exe so the user isn't left
            // without a launcher.
            try
            {
                if (!File.Exists(currentExe) && File.Exists(backupPath))
                {
                    File.Move(backupPath, currentExe);
                }
            }
            catch { /* best effort */ }
        }
    }
}

public enum UpdateResult
{
    NothingToDo,
    AppliedModOnly,    // legacy enum value; unused since single-artifact model
    LauncherStagedOnly,
    AppliedBoth,       // legacy enum value; unused since single-artifact model
}
