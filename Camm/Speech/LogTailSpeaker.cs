using System.Text;

namespace Camm.Speech;

// Tail a game-side log file and feed each new chunk to the Mediator
// (which routes via AccessibleOutputHandler + the manifest's
// MarkerProtocol + Sanitizer). Designed for per-launch lifetime — the
// caller passes the file's size captured BEFORE the game was spawned
// so the watcher knows what's "new" vs replay-from-history.
//
// File-path resolution is the consumer's responsibility — CAMM doesn't
// know where the target game writes its log. CivViAccess uses
// %LocalAppData%\Firaxis Games\Sid Meier's Civilization VI\Logs\Lua.log;
// a Factorio Access adopter would point at factorio-current.log; etc.
public sealed class LogTailSpeaker
{
    private readonly Mediator _mediator;

    public LogTailSpeaker(Mediator mediator)
    {
        _mediator = mediator;
    }

    public Task WaitForLogFileToExist(string logFilePath, CancellationToken? cancellationToken = null)
    {
        // Poll at 250ms so a brief game-lifecycle event (the engine
        // sometimes deletes and recreates the log on first-launch /
        // clean-install paths) doesn't leave the launcher parked in
        // "File no longer found, waiting..." for up to 10 seconds while
        // the file is actually already back. 250ms is responsive enough
        // that the user hears at most one beat of silence before speech
        // resumes.
        while (cancellationToken?.IsCancellationRequested != true)
        {
            if (File.Exists(logFilePath))
            {
                return Task.CompletedTask;
            }

            Thread.Sleep(250);
        }

        return Task.FromException(new ApplicationException(
            $"Unable to load log file {logFilePath}"));
    }

    public async void WatchLogFile(string filePath, long preLaunchSize)
    {
        // Choose the read cursor so we replay nothing from prior
        // sessions (Tolk firing every #SCREENREADER line in the last
        // 1KB on attach was deafening). preLaunchSize is the file's
        // size captured at launcher startup, before the game was
        // spawned:
        //   * If the file is now SMALLER, the game truncated it on
        //     boot — a fresh session. Start at 0 so we catch every
        //     line this run.
        //   * Otherwise the engine appended; start at preLaunchSize
        //     so we replay only this session's writes.
        var currentSize = new FileInfo(filePath).Length;
        var lastReadLength = currentSize < preLaunchSize ? 0L : preLaunchSize;

        while (true)
        {
            try
            {
                var fileSize = new FileInfo(filePath).Length;
                if (fileSize > lastReadLength)
                {
                    using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        fs.Seek(lastReadLength, SeekOrigin.Begin);
                        var buffer = new byte[1024];

                        while (true)
                        {
                            var bytesRead = fs.Read(buffer, 0, buffer.Length);
                            lastReadLength += bytesRead;

                            if (bytesRead == 0)
                                break;

                            var text = ASCIIEncoding.ASCII.GetString(buffer, 0, bytesRead);
                            _mediator.Output(text);
                        }
                    }
                }
            }
            catch (FileNotFoundException)
            {
                _mediator.OutputText("File no longer found: Waiting for file to exist again...");
                await WaitForLogFileToExist(filePath);
                _mediator.OutputText("Log file found. Watching...");
                // File reappeared mid-session (rare — manual delete,
                // log rotation, etc.). Reset to read from the start
                // since whatever's there is new.
                lastReadLength = 0L;
            }
            catch (Exception e)
            {
                _mediator.OutputTextError("Error: " + e.Message);
            }

            Thread.Sleep(200);
        }
    }
}
