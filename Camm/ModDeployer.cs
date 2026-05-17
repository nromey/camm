namespace Camm;

// Copies the mod source tree from a dev checkout into the target
// game's mod/DLC directory before the launcher spawns the game. Lets
// the dev loop be "edit source -> launch -> retest" instead of "edit
// source -> manually copy -> launch -> retest", which gets painful
// within the first hour of multi-iteration testing.
//
// In a shipped installer the source tree won't sit next to the launcher
// exe; FindModSourceDir returns null in that case and the consumer's
// Program.cs skips the deploy step, falling through to "use whatever
// the installer put in the destination dir."
//
// Path discovery: walks parent directories from the launcher exe
// looking for a folder named CammConfig.ModPayloadFolderName with a
// CammConfig.ModPayloadSentinelFileName file inside. That double-check
// (folder name + sentinel file) avoids confusing a random matching
// folder name elsewhere on disk.
public static class ModDeployer
{
    // Default deploy destination computed from CammConfig at call time
    // (rather than cached) so consumers that compute it from
    // Environment.GetFolderPath get fresh values each call.
    public static string DefaultDestination => CammConfig.ModPayloadDefaultDestination();

    public static string? FindModSourceDir()
    {
        var modDirName = CammConfig.ModPayloadFolderName;
        var sentinelFileName = CammConfig.ModPayloadSentinelFileName;
        if (string.IsNullOrEmpty(modDirName)) return null;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, modDirName);
            if (string.IsNullOrEmpty(sentinelFileName))
            {
                // No sentinel configured — accept any folder with the
                // right name. Less safe but supports adopters whose mod
                // payload has no fixed manifest file.
                if (Directory.Exists(candidate)) return candidate;
            }
            else if (File.Exists(Path.Combine(candidate, sentinelFileName)))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }

    // Copy semantics: overwrite-only, never delete. Doing a true mirror
    // (delete dest-only files) would be tempting for cleanliness but
    // operates on a path inside Program Files (or the game's mod dir);
    // a bug in the path discovery + a delete pass is how user data gets
    // shredded. Stale files left behind are typically harmless — the
    // game's mod loader only loads what the manifest references — so
    // leaving them is the safer default.
    public static int Deploy(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);
        var copied = 0;
        foreach (var src in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, src);
            var dest = Path.Combine(destDir, relative);
            var destFolder = Path.GetDirectoryName(dest);
            if (destFolder is not null)
            {
                Directory.CreateDirectory(destFolder);
            }
            File.Copy(src, dest, overwrite: true);
            copied++;
        }
        return copied;
    }
}
