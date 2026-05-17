namespace Camm;

// File-based logging for post-mortem diagnosis when the launcher's
// Console.WriteLine output is invisible (IFEO-spawned launches from
// Steam inherit Steam's stdin/stdout, which goes nowhere). Writes to
// %LocalAppData%\<configured folder>\launcher.log so we can read it
// after a session to see what the launcher actually did vs what we
// expected.
//
// Append-only, single shared file, best-effort. Failures to write are
// swallowed — logging should never prevent the launcher from running.
//
// On launcher startup, the log is truncated (start of session) so
// each launch produces a clean record rather than an ever-growing file.
// Each entry: ISO-8601 timestamp + level + message.
//
// The LocalAppData folder name is per-mod state — the consumer passes
// it on StartSession. Until StartSession is called, log writes land
// at %LocalAppData%\Camm\launcher.log as a fallback (so early-startup
// logging before configuration has somewhere to go).
public static class Logger
{
    private static readonly object _lock = new();
    private static string _folderName = "Camm";
    private static string? _logPath;

    public static string LogPath
    {
        get
        {
            if (_logPath is not null) return _logPath;
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                _folderName);
            try { Directory.CreateDirectory(dir); } catch { }
            return _logPath = Path.Combine(dir, "launcher.log");
        }
    }

    // Call once at process startup to begin a fresh session log. The
    // localAppDataFolderName argument names the per-user state folder
    // (e.g. "CivVIAccess", "FactorioAccess") — same name a consuming
    // mod uses for launcher.ini and any other per-user state.
    public static void StartSession(string mode, string localAppDataFolderName)
    {
        lock (_lock)
        {
            _folderName = localAppDataFolderName;
            _logPath = null;  // re-resolve LogPath on next access
            try
            {
                File.WriteAllText(LogPath, "");
                Write("INFO", $"=== launcher session start, mode={mode}, pid={Environment.ProcessId}, exe={Environment.ProcessPath} ===");
                Write("INFO", $"  AppContext.BaseDirectory={AppContext.BaseDirectory}");
                Write("INFO", $"  Args={string.Join(" ", Environment.GetCommandLineArgs())}");
            }
            catch { }
        }
    }

    public static void Info(string msg) => Write("INFO", msg);
    public static void Warn(string msg) => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    public static void Exception(string context, Exception ex)
    {
        Write("ERROR", $"{context}: {ex.GetType().Name}: {ex.Message}");
        if (ex.StackTrace is not null) Write("ERROR", $"  {ex.StackTrace}");
    }

    private static void Write(string level, string msg)
    {
        lock (_lock)
        {
            try
            {
                var line = $"{DateTime.Now:HH:mm:ss.fff} {level,-5} {msg}{Environment.NewLine}";
                File.AppendAllText(LogPath, line);
            }
            catch { /* logging must never crash the launcher */ }
        }
    }
}
