using System.IO.Compression;

namespace Camm;

// Archive game-written log files (e.g. Civ VI's Lua.log, Database.log,
// Modding.log) on each launcher startup so we can post-mortem prior
// sessions after the game has truncated them on the next launch. Auto-
// prunes archives older than RetentionDays to bound disk usage.
//
// Adopters opt in by implementing IGameInstance.GetArchivableLogPaths();
// the default returns an empty enumerable so existing adopters that
// don't need archiving don't have to change.
//
// Compression: gzip via System.IO.Compression — built-in, AOT-safe,
// universally readable (every archive utility on every platform
// handles .gz). LZMA2 has marginally better compression for log text
// but requires a NuGet dep that doesn't play nicely with Native AOT;
// gzip is the right trade-off for our needs.
//
// Archives live alongside the launcher.log under
// %LocalAppData%\<mod>\logs-archive\<name>-<yyyy-MM-dd_HH-mm-ss>.<ext>.gz
// e.g. Lua-2026-05-24_21-15-00.log.gz.
public static class LogArchiver
{
    // Keep archives this long. Older files are deleted on each call to
    // ArchiveAndPrune. Adopters can override before calling.
    public static int RetentionDays { get; set; } = 7;

    // Called once during launcher startup, before the game is spawned.
    // Copies the source log files to the archive dir compressed, then
    // prunes any files older than RetentionDays.
    //
    // Robust to source files missing, in use by another process, or
    // unreadable for any reason — each individual archive attempt is
    // independently try/catch'd. Failures log and continue; archiving
    // is a diagnostic-aid feature, not load-bearing.
    public static void ArchiveAndPrune(IGameInstance gameInstance)
    {
        var archiveDir = ArchiveDir;
        try
        {
            Directory.CreateDirectory(archiveDir);
        }
        catch (Exception ex)
        {
            Logger.Exception($"LogArchiver: could not create archive dir '{archiveDir}'", ex);
            return;
        }

        var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        var paths = gameInstance.GetArchivableLogPaths();
        var count = 0;
        foreach (var srcPath in paths)
        {
            if (ArchiveOne(srcPath, archiveDir, timestamp)) count++;
        }
        Logger.Info($"LogArchiver: archived {count} log file(s) with timestamp {timestamp}");

        Prune(archiveDir);
    }

    private static bool ArchiveOne(string srcPath, string archiveDir, string timestamp)
    {
        if (!File.Exists(srcPath))
        {
            Logger.Info($"LogArchiver: source '{srcPath}' does not exist, skipping");
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(srcPath);
        var ext = Path.GetExtension(srcPath);
        var destName = $"{name}-{timestamp}{ext}.gz";
        var destPath = Path.Combine(archiveDir, destName);

        try
        {
            // FileShare.ReadWrite so we can read even if the game is
            // still holding the file open for write. The game also may
            // truncate during this read (rare, only if it boots before
            // we finish); the gzip output may be partial but that's
            // strictly better than no archive at all.
            using var src = new FileStream(
                srcPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var dest = File.Create(destPath);
            using var gz = new GZipStream(dest, CompressionLevel.Optimal);
            src.CopyTo(gz);
            Logger.Info($"LogArchiver: archived '{srcPath}' -> '{destPath}'");
            return true;
        }
        catch (Exception ex)
        {
            Logger.Exception($"LogArchiver: archive failed for '{srcPath}'", ex);
            // Clean up partial output so we don't leave broken .gz files.
            try { if (File.Exists(destPath)) File.Delete(destPath); } catch { }
            return false;
        }
    }

    private static void Prune(string archiveDir)
    {
        var cutoff = DateTime.Now.AddDays(-RetentionDays);
        var pruned = 0;
        try
        {
            foreach (var file in Directory.EnumerateFiles(archiveDir, "*.gz"))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTime < cutoff)
                    {
                        File.Delete(file);
                        Logger.Info(
                            $"LogArchiver: pruned '{Path.GetFileName(file)}' " +
                            $"(modified {info.LastWriteTime:yyyy-MM-dd})");
                        pruned++;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Exception($"LogArchiver: prune skipped '{file}'", ex);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Exception("LogArchiver: prune enumeration failed", ex);
            return;
        }
        if (pruned > 0)
        {
            Logger.Info($"LogArchiver: pruned {pruned} archive(s) older than {RetentionDays} days");
        }
    }

    private static string ArchiveDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CammHost.Manifest.LocalAppDataFolderName,
        "logs-archive");
}
