namespace Camm;

// File-based logging for post-mortem diagnosis when the launcher's
// Console.WriteLine output is invisible (IFEO-spawned launches from
// Steam inherit Steam's stdin/stdout, which goes nowhere). Writes to
// %LocalAppData%\<CammHost.Manifest.LocalAppDataFolderName>\launcher.log
// so we can read it after a session to see what the launcher actually
// did vs what we expected.
//
// Append-only, single shared file, best-effort. Failures to write are
// swallowed — logging should never prevent the launcher from running.
//
// On launcher startup, the log is truncated (start of session) so
// each launch produces a clean record rather than an ever-growing
// file. Each entry: ISO-8601 timestamp + level + message.
//
// Caller must call CammHost.Initialize(manifest) before any logging
// happens; the LogPath getter reads CammHost.Manifest each time
// (the throw-on-uninitialized is what surfaces config-ordering bugs).
public static class Logger
{
    private static readonly object _lock = new();

    public static string LogPath
    {
        get
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                CammHost.Manifest.LocalAppDataFolderName);
            try { Directory.CreateDirectory(dir); } catch { }
            return Path.Combine(dir, "launcher.log");
        }
    }

    // Call once at process startup, after CammHost.Initialize, to begin
    // a new session in the log.
    //
    // Appends rather than truncates: when launcher A is mid-session and
    // launcher B starts (IFEO-redirected child for an internal exe
    // spawn — see CammHost transparent-invocation short-circuit), B's
    // StartSession used to wipe A's log. The interesting log was always
    // A's (it's the one with the actual game lifecycle, log-tail, and
    // user-facing speech) and getting it overwritten by B's tiny pre-
    // exit session made diagnostic work impossible. We now banner each
    // new session inline and let the file grow. Size is bounded by
    // user re-launching the game so it doesn't grow without limit; if
    // it ever becomes a problem we'll rotate.
    public static void StartSession(string mode)
    {
        lock (_lock)
        {
            try
            {
                var path = LogPath;
                // Ensure the file exists. AppendAllText creates if missing.
                if (!File.Exists(path)) File.WriteAllText(path, "");
                Write("INFO", "");
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
