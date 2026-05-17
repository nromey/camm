using System.Reflection;

namespace Camm;

// Extract embedded mod files (the consuming launcher's mod payload)
// into a target directory. Companion to TolkBootstrap.ExtractTo — same
// embedded-resource pattern, different prefix.
//
// Why this exists: the installer needs to put the mod's files in the
// target game's mod/DLC dir at install time, but the installer doesn't
// have access to the source repo at run-time on a user's machine.
// Bundling the mod payload as embedded resources in the consuming
// launcher exe solves it — one .exe contains everything needed to
// install both the launcher AND the mod files.
//
// Resource convention: names look like `mod/Assets/UI/Foo.lua`,
// preserving the on-disk directory structure. The consuming launcher's
// csproj embeds its mod payload with LogicalName `mod/%(RecursiveDir)%(Filename)%(Extension)`;
// ModFiles reads them from Assembly.GetEntryAssembly() so it finds the
// consumer's resources, not Camm.dll's (which has none).
//
// On uninstall, the caller is responsible for removing the deployed
// directory — there's no symmetric ModFiles.RemoveFrom.
public static class ModFiles
{
    private const string ResourcePrefix = "mod/";

    // Extract all embedded mod files into targetDir, preserving the
    // source tree's directory layout. Overwrites existing files (so
    // install-over-install reliably refreshes mod content).
    //
    // Returns the count of files written. Caller can log it for sanity.
    public static int ExtractTo(string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        // Read from the consuming exe's assembly — it embeds the mod
        // resources via its csproj <EmbeddedResource> glob. Camm.dll
        // itself has no `mod/*` resources, so typeof(ModFiles).Assembly
        // would silently extract nothing.
        var asm = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException(
                "No entry assembly — ModFiles.ExtractTo must be called " +
                "from a CAMM-built launcher exe that embeds the mod " +
                "payload as resources with logical name 'mod/<path>'.");
        int written = 0;

        foreach (var resourceName in asm.GetManifestResourceNames())
        {
            if (!IsModResource(resourceName)) continue;

            // Strip the "mod/" prefix and normalize separators to OS-
            // native. Resource names use forward-slash from the
            // LogicalName MSBuild metadata, but Windows file APIs work
            // with either — Path.Combine handles both fine.
            var relative = StripPrefix(resourceName).Replace('\\', '/');
            if (string.IsNullOrEmpty(relative)) continue;

            var destPath = Path.Combine(
                targetDir,
                relative.Replace('/', Path.DirectorySeparatorChar));

            var destFolder = Path.GetDirectoryName(destPath);
            if (destFolder is not null) Directory.CreateDirectory(destFolder);

            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is null) continue;
            try
            {
                using var dest = File.Create(destPath);
                stream.CopyTo(dest);
                written++;
            }
            catch (IOException)
            {
                // File in use — extremely rare for mod assets (they're
                // not P/Invoke targets), but tolerate it without
                // aborting the whole install.
            }
        }
        return written;
    }

    private static bool IsModResource(string name) =>
        name.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase)
        || name.StartsWith("mod\\", StringComparison.OrdinalIgnoreCase);

    private static string StripPrefix(string name)
    {
        if (name.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase))
            return name.Substring(ResourcePrefix.Length);
        if (name.StartsWith("mod\\", StringComparison.OrdinalIgnoreCase))
            return name.Substring(4);
        return name;
    }
}
