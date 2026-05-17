namespace Camm;

// Copies a payload's mod source tree from a dev checkout into the
// payload's destination directory before the launcher spawns the
// game. Lets the dev edit-build-launch loop avoid manual copies.
//
// In a shipped installer the source tree isn't next to the launcher
// exe; FindModSourceDir returns null and CammHost skips the deploy
// step, falling through to "use whatever the embedded extraction
// put in the destination dir."
//
// Each ModPayload has its own dev-mode source discovery: walks parent
// directories from the launcher exe looking for a folder named
// `payload.FolderName` with `payload.SentinelFileName` inside (when
// the sentinel is non-empty). Multi-payload consumers may resolve
// only some payloads in dev mode if the dev tree doesn't contain
// all of them.
public static class ModDeployer
{
    // Walk up from AppContext.BaseDirectory looking for a folder
    // named payload.FolderName. If SentinelFileName is non-empty,
    // the candidate also has to contain that file. Returns the
    // first matching path, or null.
    public static string? FindModSourceDir(ModPayload payload)
    {
        if (string.IsNullOrEmpty(payload.FolderName)) return null;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, payload.FolderName);
            if (Directory.Exists(candidate))
            {
                if (string.IsNullOrEmpty(payload.SentinelFileName))
                {
                    return candidate;
                }
                var sentinel = Path.Combine(candidate, payload.SentinelFileName);
                if (File.Exists(sentinel)) return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }

    // Convenience for callers that need all payloads' source dirs at
    // once. Returns a dictionary keyed by payload name; entries are
    // null for payloads with no discoverable source dir.
    public static Dictionary<string, string?> FindAllSourceDirs()
    {
        var result = new Dictionary<string, string?>();
        foreach (var p in CammHost.Manifest.ModPayloads)
        {
            result[p.Name] = FindModSourceDir(p);
        }
        return result;
    }

    // Copy semantics: overwrite-only, never delete. Doing a true
    // mirror (delete dest-only files) would be tempting for
    // cleanliness but operates on a path inside Program Files or the
    // game's mod dir — a bug in the path discovery + a delete pass
    // is how user data gets shredded. Stale files left behind are
    // typically harmless (game's mod loader only loads what the
    // manifest references), so leaving them is the safer default.
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
