using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Camm;

// CAMM-built launchers ship as a single .exe (Native AOT). Tolk needs
// several native DLLs (Tolk.dll plus screen-reader-specific clients
// like NVDAControllerClient64.dll, SAAPI64.dll, dolapi32.dll, etc.)
// which must be loadable by P/Invoke at runtime — P/Invoke can't pull
// DLLs out of an embedded resource directly.
//
// The CONSUMING exe (not Camm.dll itself) embeds the DLLs as resources
// with logical name `tolk/<filename>`. CAMM's TolkBootstrap reads them
// from `Assembly.GetEntryAssembly()` — the consuming exe — and extracts
// them somewhere the Win32 DLL loader can find. Two distinct cases:
//
//   1. RUNTIME LOADING (PrepareRuntime): Pre-install, the user is
//      running the launcher from a temp / Downloads / Desktop location.
//      Extracting Tolk DLLs *next to the exe* in those locations would
//      leave stray .dll files in the user's folders — confusing and
//      messy. Instead we extract to a per-launch temp directory and
//      call SetDllDirectory to point the loader at it. No pollution of
//      user-visible folders, OS cleans up temp dirs over time.
//
//   2. INSTALL EXTRACTION (ExtractTo): During install, drop Tolk DLLs
//      alongside the installed launcher (e.g., Program Files\<mod>\).
//      Explicit, deliberate extraction to a specific target dir. Files
//      stay there until uninstall.
//
// Post-install runs hit case 1 first; PrepareRuntime sees Tolk.dll
// already next to the exe (in Program Files) and skips extraction.
// SetDllDirectory becomes a no-op since the loader's default search
// path already includes AppContext.BaseDirectory.
//
// Native AOT note: GetManifestResourceNames / GetManifestResourceStream
// are AOT-safe (no reflection over user types).
public static partial class TolkBootstrap
{
    private const string ResourcePrefix = "tolk/";
    private static string? _tempExtractDir;

    [LibraryImport("kernel32.dll", EntryPoint = "SetDllDirectoryW",
        StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [SupportedOSPlatform("windows")]
    private static partial bool SetDllDirectory(string? lpPathName);

    // Make sure Tolk DLLs are loadable from somewhere by the time any
    // P/Invoke call to them happens. Decides between "they're already
    // next to the exe (post-install)" and "extract to temp + redirect
    // loader (pre-install)" cases automatically.
    //
    // Per-launch temp dir name uses CammHost.Manifest.LocalAppDataFolderName
    // as a prefix (e.g. "CivViAccess" → %TEMP%\CivViAccess_<pid>\) so
    // two CAMM-built launchers running side-by-side don't fight over
    // the same temp path.
    public static void PrepareRuntime()
    {
        var exeDir = AppContext.BaseDirectory;
        if (File.Exists(Path.Combine(exeDir, "Tolk.dll")))
        {
            // Post-install / pre-extracted case: DLLs already adjacent
            // to the running exe. The Win32 loader finds them via the
            // default DLL search order. Nothing to do.
            return;
        }

        // Pre-install case: extract to a per-launch temp dir and point
        // the DLL loader at it via SetDllDirectory. Avoids polluting
        // Downloads / Desktop with stray DLL files.
        var pid = System.Diagnostics.Process.GetCurrentProcess().Id;
        var prefix = CammHost.Manifest.LocalAppDataFolderName;
        _tempExtractDir = Path.Combine(Path.GetTempPath(), $"{prefix}_{pid}");
        ExtractTo(_tempExtractDir);

        if (OperatingSystem.IsWindows())
        {
            SetDllDirectory(_tempExtractDir);
        }

        // Best-effort cleanup of the temp dir on process exit. DLLs
        // currently mapped into our process can't be deleted while
        // we're alive, so this only succeeds for files Win32 has
        // released — but missing cleanup just means temp accumulates
        // a small dir until %TEMP% gets swept, not a correctness bug.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => CleanupTemp();
    }

    // Explicit extraction to a specific directory. Used at install
    // time to populate the install dir with persistent Tolk sidecars.
    public static void ExtractTo(string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        // GetEntryAssembly() is the CONSUMING exe (the CAMM-built
        // launcher). That's the assembly that embeds the Tolk DLLs as
        // resources, not Camm.dll itself. typeof(TolkBootstrap).Assembly
        // would return Camm.dll, which has no `tolk/*` resources and
        // would silently extract nothing.
        var asm = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException(
                "No entry assembly — TolkBootstrap.ExtractTo must be " +
                "called from a CAMM-built launcher exe that embeds the " +
                "Tolk DLLs as resources with logical name 'tolk/<file>'.");

        foreach (var resourceName in asm.GetManifestResourceNames())
        {
            if (!IsTolkResource(resourceName)) continue;
            var fileName = Path.GetFileName(NormalizeSeparators(resourceName));
            if (string.IsNullOrEmpty(fileName)) continue;
            var destPath = Path.Combine(targetDir, fileName);

            // Always overwrite. A previous same-size-skip optimization
            // matched across builds when Tolk versions happened to ship
            // identical byte counts, leaving stale DLLs in the install
            // dir after a launcher upgrade and silently breaking speech
            // (the new launcher loaded against an older Tolk that
            // reported HasSpeech=True but did nothing audible on Output).
            // For PrepareRuntime's temp-dir case the dir is per-pid and
            // fresh, so always-overwrite costs nothing. For the
            // Installer case it's the whole point.
            using var stream = asm.GetManifestResourceStream(resourceName);
            if (stream is null) continue;
            try
            {
                using var dest = File.Create(destPath);
                stream.CopyTo(dest);
            }
            catch (IOException)
            {
                // File in use (probably loaded by our own P/Invoke
                // already, which means contents already match). Ignore.
            }
        }
    }

    private static void CleanupTemp()
    {
        if (_tempExtractDir is null) return;
        try { Directory.Delete(_tempExtractDir, recursive: true); }
        catch { /* DLLs may still be locked or already deleted; harmless */ }
    }

    private static bool IsTolkResource(string resourceName)
    {
        // MSBuild on Windows can emit either forward- or backslash
        // separators in LogicalName depending on version, so accept both.
        return resourceName.StartsWith(ResourcePrefix, StringComparison.OrdinalIgnoreCase)
            || resourceName.StartsWith("tolk\\", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSeparators(string s) =>
        s.Replace('\\', '/');
}
