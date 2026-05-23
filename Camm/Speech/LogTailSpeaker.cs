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

        // Hold incomplete-line content across read iterations. Prior
        // to this buffer the tail decoded each 1024-byte chunk in
        // isolation and split on '\n' — anything spanning the chunk
        // boundary got cut in half, with the first half routed as a
        // "complete" partial line (then truncated and sometimes still
        // matching the screen-reader marker → Tolk spoke the half)
        // AND the second half arriving in the next chunk with no
        // marker → silently dropped. Burst writes (e.g. a multi-line
        // tutorial briefing arriving in one game frame, then read 200
        // ms later as ~1.5KB at once) reliably triggered this.
        //
        // Also: bytes are now decoded as UTF-8 instead of ASCII so
        // games whose log lines contain non-ASCII text (ellipses,
        // smart quotes, localized strings) keep their content intact.
        // ASCII silently mangled multi-byte sequences into '?' which
        // could pass the marker check but produce gibberish speech.
        var pending = new StringBuilder();
        var decoder = System.Text.Encoding.UTF8.GetDecoder();

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
                        var charBuffer = new char[1024];

                        while (true)
                        {
                            var bytesRead = fs.Read(buffer, 0, buffer.Length);
                            lastReadLength += bytesRead;

                            if (bytesRead == 0)
                                break;

                            // UTF-8 decoder is stateful — partial
                            // multi-byte sequences at the end of one
                            // read get held internally and combined
                            // with the start of the next read, so a
                            // codepoint split across a 1024-byte
                            // boundary still decodes correctly.
                            var charsDecoded = decoder.GetChars(
                                buffer, 0, bytesRead, charBuffer, 0);
                            pending.Append(charBuffer, 0, charsDecoded);
                        }
                    }

                    // Emit every complete line; keep the last
                    // partial (or empty if the read ended on a
                    // newline) for the next iteration.
                    FlushCompleteLines(pending);
                }
            }
            catch (FileNotFoundException)
            {
                _mediator.OutputText("File no longer found: Waiting for file to exist again...");
                await WaitForLogFileToExist(filePath);
                _mediator.OutputText("Log file found. Watching...");
                // File reappeared mid-session (rare — manual delete,
                // log rotation, etc.). Reset to read from the start
                // since whatever's there is new, and drop any
                // partial-line state from the prior file.
                lastReadLength = 0L;
                pending.Clear();
                decoder = System.Text.Encoding.UTF8.GetDecoder();
            }
            catch (Exception e)
            {
                _mediator.OutputTextError("Error: " + e.Message);
            }

            Thread.Sleep(200);
        }
    }

    // Pull complete lines (terminated by '\n', stripped of optional
    // trailing '\r') out of the pending buffer and route each one to
    // the mediator. Anything after the last '\n' stays in the buffer
    // for the next iteration.
    private void FlushCompleteLines(StringBuilder pending)
    {
        var text = pending.ToString();
        var lastNewline = text.LastIndexOf('\n');
        if (lastNewline < 0)
        {
            // No complete line yet; nothing to do.
            return;
        }

        var complete = text.Substring(0, lastNewline + 1);
        var remainder = (lastNewline + 1 < text.Length)
            ? text.Substring(lastNewline + 1)
            : string.Empty;

        pending.Clear();
        if (remainder.Length > 0)
        {
            pending.Append(remainder);
        }

        _mediator.Output(complete);
    }
}
