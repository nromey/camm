using System.Reflection;
using System.Text.Json;

namespace Camm;

// Extract embedded mod files (the consuming launcher's mod payload)
// into a target directory. Companion to TolkBootstrap.ExtractTo —
// same embedded-resource pattern, configurable prefix per payload.
//
// Each ModPayload defines its own embed prefix via ModPayload.Name.
// A payload `Name="dlc"` reads resources whose logical name starts
// with `dlc/` and extracts them under the payload's destination.
// A consumer with multiple payloads (Civ V Access: dlc + proxy +
// engine) embeds three separate roots in its csproj and ExtractTo
// processes them one at a time.
//
// Why per-payload manifests: Installer.Uninstall (and Updater
// rehydrate) needs to know exactly which files this payload wrote so
// it can clean them up. Tracking per-file is the only safe option
// when a payload's destination is a shared dir (game root with a
// dropped DLL, an existing engine-DLL folder, etc.). For
// mod-folder-owned destinations the manifest is overkill but not
// harmful.
public static class ModFiles
{
    // Extract the named payload's files into its DefaultDestination
    // directory. Returns the install manifest (also persisted to
    // disk for uninstall + update to consult).
    //
    // Overwrites existing files (install-over-install reliably
    // refreshes content).
    public static PayloadInstallManifest ExtractTo(ModPayload payload)
    {
        var targetDir = payload.DefaultDestination();
        Directory.CreateDirectory(targetDir);

        // Read from the consuming exe's assembly — it embeds the mod
        // resources via its csproj <EmbeddedResource> glob. Camm.dll
        // itself has no `<payload>/*` resources, so reading from
        // typeof(ModFiles).Assembly would silently extract nothing.
        var asm = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException(
                "No entry assembly — ModFiles.ExtractTo must be called " +
                "from a CAMM-built launcher exe that embeds the mod " +
                $"payload as resources with logical name '{payload.Name}/<path>'.");

        var written = new List<string>();
        var backups = new List<BackupEntry>();
        var prefix = payload.Name + "/";
        var altPrefix = payload.Name + "\\";

        foreach (var resourceName in asm.GetManifestResourceNames())
        {
            if (!IsPayloadResource(resourceName, prefix, altPrefix)) continue;

            var relative = StripPrefix(resourceName, prefix, altPrefix).Replace('\\', '/');
            if (string.IsNullOrEmpty(relative)) continue;

            var destPath = Path.Combine(
                targetDir,
                relative.Replace('/', Path.DirectorySeparatorChar));

            var destFolder = Path.GetDirectoryName(destPath);
            if (destFolder is not null) Directory.CreateDirectory(destFolder);

            // BackupAndReplace: if a non-CAMM file already exists at
            // destPath, rename it to <destPath>.original so uninstall
            // can restore the user's pre-CAMM state. Skip if the
            // backup already exists (don't clobber a real backup with
            // a CAMM-installed file from a previous install).
            if (payload.OverwriteStrategy == OverwriteStrategy.BackupAndReplace
                && File.Exists(destPath))
            {
                var backupPath = destPath + ".original";
                if (!File.Exists(backupPath))
                {
                    try
                    {
                        File.Move(destPath, backupPath);
                        backups.Add(new BackupEntry
                        {
                            InstalledPath = Path.GetFullPath(destPath),
                            BackupPath = Path.GetFullPath(backupPath),
                        });
                    }
                    catch (IOException ex)
                    {
                        Logger.Warn(
                            $"Could not back up existing file {destPath}: {ex.Message}. " +
                            $"Will overwrite; uninstall will leave this file deleted.");
                    }
                }
                else
                {
                    // Backup from a prior CAMM install already exists.
                    // The current destPath is the CAMM-installed file
                    // from that prior install — just delete it so the
                    // new content can land, and preserve the existing
                    // backup-entry record for uninstall.
                    backups.Add(new BackupEntry
                    {
                        InstalledPath = Path.GetFullPath(destPath),
                        BackupPath = Path.GetFullPath(backupPath),
                    });
                    try { File.Delete(destPath); } catch { /* best effort */ }
                }
            }

            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is null) continue;
            try
            {
                using var dest = File.Create(destPath);
                stream.CopyTo(dest);
                written.Add(destPath);
            }
            catch (IOException)
            {
                // File in use — rare for mod assets. Tolerate without
                // aborting the whole install.
            }
        }

        var manifest = new PayloadInstallManifest
        {
            PayloadName = payload.Name,
            DestinationRoot = Path.GetFullPath(targetDir),
            Files = written.Select(Path.GetFullPath).ToList(),
            Backups = backups,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            CammVersion = SemVer.Current().ToString(),
        };
        WriteManifestFile(payload, manifest);
        return manifest;
    }

    // Read the persisted manifest for the previous install of this
    // payload (if any). Used by uninstall + update-rehydrate to find
    // out what files to clean up. Returns null if no prior manifest
    // exists (clean machine, or manifest file got deleted).
    public static PayloadInstallManifest? ReadManifestForPayload(ModPayload payload)
    {
        var path = ManifestFilePath(payload);
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = File.OpenRead(path);
            return JsonSerializer.Deserialize(
                fs, PayloadInstallManifestJsonContext.Default.PayloadInstallManifest);
        }
        catch
        {
            // Corrupt manifest — log somewhere up the stack; treat as
            // missing.
            return null;
        }
    }

    // Delete the files listed in the manifest. Best-effort: missing /
    // locked files don't abort the loop. After deleting files, walks
    // empty subdirectories from the deepest leaf up to (but not
    // including) the destination root and removes them, leaving
    // foreign files in the destination intact.
    //
    // Then, for each backup entry, restore the .original file to its
    // pre-CAMM location (BackupAndReplace strategy). This must happen
    // AFTER the file delete so the rename can land on an empty slot.
    public static void RemoveByManifest(PayloadInstallManifest manifest)
    {
        foreach (var file in manifest.Files)
        {
            try { File.Delete(file); } catch { /* best effort */ }
        }

        foreach (var backup in manifest.Backups)
        {
            try
            {
                if (File.Exists(backup.BackupPath))
                {
                    if (File.Exists(backup.InstalledPath))
                    {
                        // CAMM-owned file wasn't deleted (Files list and
                        // Backups list disagree — shouldn't normally
                        // happen, but be safe).
                        File.Delete(backup.InstalledPath);
                    }
                    File.Move(backup.BackupPath, backup.InstalledPath);
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(
                    $"Could not restore backup {backup.BackupPath} -> {backup.InstalledPath}: {ex.Message}.");
            }
        }

        TryRemoveEmptyDirsBelow(manifest.DestinationRoot, manifest.Files);
    }

    // Delete the on-disk manifest file for a payload. Called after
    // an uninstall has cleaned up its files.
    public static void DeleteManifestFile(ModPayload payload)
    {
        var path = ManifestFilePath(payload);
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    private static string ManifestFilePath(ModPayload payload)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            CammHost.Manifest.LocalAppDataFolderName);
        try { Directory.CreateDirectory(dir); } catch { }
        return Path.Combine(dir, $"installed-{SafeFileName(payload.Name)}.json");
    }

    private static void WriteManifestFile(ModPayload payload, PayloadInstallManifest manifest)
    {
        try
        {
            var path = ManifestFilePath(payload);
            using var fs = File.Create(path);
            JsonSerializer.Serialize(
                fs, manifest, PayloadInstallManifestJsonContext.Default.PayloadInstallManifest);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to write install manifest for payload '{payload.Name}': {ex.Message}");
        }
    }

    private static void TryRemoveEmptyDirsBelow(string root, IEnumerable<string> files)
    {
        // Collect every parent directory of every file, sort deepest-
        // first, attempt RemoveDirectory on each empty one — stops
        // when it hits the root or a non-empty dir.
        var rootFull = Path.GetFullPath(root);
        var dirs = files
            .Select(Path.GetDirectoryName)
            .Where(d => !string.IsNullOrEmpty(d))
            .Select(d => Path.GetFullPath(d!))
            .Distinct()
            .OrderByDescending(d => d.Length)
            .ToList();

        foreach (var dir in dirs)
        {
            if (!dir.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase)) continue;
            // Walk from dir up to (but not including) rootFull, deleting
            // each empty step.
            var cur = dir;
            while (cur.Length > rootFull.Length && cur.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (Directory.Exists(cur) && !Directory.EnumerateFileSystemEntries(cur).Any())
                    {
                        Directory.Delete(cur);
                    }
                    else break;
                }
                catch { break; }
                var parent = Path.GetDirectoryName(cur);
                if (string.IsNullOrEmpty(parent)) break;
                cur = parent;
            }
        }

        // Finally try the root itself — only succeeds if every file
        // under it (CAMM-owned + foreign) has been removed. Usually
        // no-op when foreign content exists.
        try
        {
            if (Directory.Exists(rootFull) && !Directory.EnumerateFileSystemEntries(rootFull).Any())
            {
                Directory.Delete(rootFull);
            }
        }
        catch { /* best effort */ }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Create(name.Length, name, (span, src) =>
        {
            for (int i = 0; i < src.Length; i++)
            {
                span[i] = Array.IndexOf(invalid, src[i]) >= 0 ? '_' : src[i];
            }
        });
    }

    private static bool IsPayloadResource(string resourceName, string prefix, string altPrefix) =>
        resourceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
        || resourceName.StartsWith(altPrefix, StringComparison.OrdinalIgnoreCase);

    private static string StripPrefix(string name, string prefix, string altPrefix)
    {
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return name.Substring(prefix.Length);
        if (name.StartsWith(altPrefix, StringComparison.OrdinalIgnoreCase))
            return name.Substring(altPrefix.Length);
        return name;
    }
}
