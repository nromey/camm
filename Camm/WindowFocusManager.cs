using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Camm;

// Two pieces of window management a CAMM-built launcher needs that
// Process.Start doesn't give:
//
//   1. After spawning the target game, half the time Windows refuses
//      to hand foreground over to the new process. The user is left
//      staring at the launcher console (or worse, the foreground stays
//      on whatever they had open before launching) and keystrokes miss
//      the game. EnsureForeground polls the game window into existence,
//      then forces it foreground using the AttachThreadInput workaround
//      for up to the supplied budget.
//
//   2. While the game is running, the launcher console serves no UI
//      purpose — it's a passive log tail and Tolk bridge.
//      StartFollowFocus runs a background poll: when the game has
//      foreground, minimize the console; when the console has
//      foreground (user alt-tabbed back), minimize the game. Exactly
//      one of the two is on-screen at a time, with Alt+Tab as the
//      natural switcher.
//
// AOT-safe: only LibraryImport P/Invokes, no reflection, no delegate
// marshalling. Polls GetForegroundWindow rather than SetWinEventHook —
// SetWinEventHook with OUTOFCONTEXT requires a message pump in the
// hooking thread, which a console app doesn't have by default. A 250ms
// poll is 4 syscalls / sec, indistinguishable from background noise.
//
// Process names come from CammConfig.GameProcessNames — set by the
// consumer at startup.
public static partial class WindowFocusManager
{
    private const int SW_MINIMIZE = 6;
    private const int SW_RESTORE  = 9;

    // Returns true if the game window was successfully brought to the
    // foreground within the budget. False on timeout — caller may choose
    // to tell the user to Alt+Tab manually.
    public static bool EnsureForeground(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            var hWnd = FindGameWindow();
            if (hWnd != IntPtr.Zero)
            {
                ForceForeground(hWnd);
                if (GetForegroundWindow() == hWnd)
                {
                    return true;
                }
            }
            Thread.Sleep(200);
        }
        return false;
    }

    // Starts a thread-pool poll that minimizes the inactive window
    // whenever foreground swaps between the game and the launcher
    // console. Fire-and-forget — the task is a background thread-pool
    // task and dies when the process exits.
    public static void StartFollowFocus(IntPtr consoleHwnd)
    {
        if (consoleHwnd == IntPtr.Zero)
        {
            return;
        }
        Task.Run(() => FollowFocusLoop(consoleHwnd));
    }

    public static IntPtr GetConsoleWindowHandle() => GetConsoleWindow();

    // Walks running processes named in CammConfig.GameProcessNames and
    // returns the first visible top-level window we find. Games' launcher
    // EXE may spawn a child (e.g. DX12 path), so the process returned by
    // Process.Start isn't necessarily the one with the visible window —
    // searching by name sidesteps that.
    private static IntPtr FindGameWindow()
    {
        foreach (var name in CammConfig.GameProcessNames)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try
                {
                    p.Refresh();
                    var hWnd = p.MainWindowHandle;
                    if (hWnd != IntPtr.Zero && IsWindowVisible(hWnd))
                    {
                        return hWnd;
                    }
                }
                catch
                {
                    // Process may have exited between enumeration and access.
                }
            }
        }
        return IntPtr.Zero;
    }

    // The AttachThreadInput trick: Windows refuses SetForegroundWindow
    // from a thread that doesn't own the current foreground unless we
    // briefly attach our thread's input state to the foreground
    // thread's. Attach, force, detach. Restore from minimized first if
    // needed.
    private static void ForceForeground(IntPtr hWnd)
    {
        if (IsIconic(hWnd))
        {
            ShowWindow(hWnd, SW_RESTORE);
        }

        var foregroundHwnd = GetForegroundWindow();
        var foregroundThread = GetWindowThreadProcessId(foregroundHwnd, out _);
        var currentThread    = GetCurrentThreadId();

        var attached = false;
        if (foregroundThread != 0 && foregroundThread != currentThread)
        {
            attached = AttachThreadInput(currentThread, foregroundThread, true);
        }

        SetForegroundWindow(hWnd);
        BringWindowToTop(hWnd);

        if (attached)
        {
            AttachThreadInput(currentThread, foregroundThread, false);
        }
    }

    private static void FollowFocusLoop(IntPtr consoleHwnd)
    {
        IntPtr lastForeground = IntPtr.Zero;
        bool firstIteration = true;
        while (true)
        {
            var gameHwnd = FindGameWindow();
            // If the game window vanished (process exited / crashed),
            // the main thread is responsible for tearing down the
            // launcher. Sleep a bit and keep checking — when the
            // process truly exits we get torn down with it.
            if (gameHwnd == IntPtr.Zero)
            {
                Thread.Sleep(500);
                continue;
            }

            var foreground = GetForegroundWindow();

            if (firstIteration)
            {
                // On startup, only minimize the console if the game is
                // already foreground. The reverse (minimizing the game
                // because the console is foreground) would punish the
                // focus-force-failed case — caller is responsible for
                // not starting follow-focus in that scenario, but
                // defending here is cheap insurance.
                firstIteration = false;
                lastForeground = foreground;
                if (foreground == gameHwnd && !IsIconic(consoleHwnd))
                {
                    ShowWindow(consoleHwnd, SW_MINIMIZE);
                }
            }
            else if (foreground != lastForeground)
            {
                lastForeground = foreground;
                if (foreground == gameHwnd && !IsIconic(consoleHwnd))
                {
                    ShowWindow(consoleHwnd, SW_MINIMIZE);
                }
                else if (foreground == consoleHwnd && !IsIconic(gameHwnd))
                {
                    ShowWindow(gameHwnd, SW_MINIMIZE);
                }
                // Foreground landed on some third window (Discord,
                // NVDA settings, etc.). Leave both alone; user can
                // Alt+Tab to either when they want it.
            }
            Thread.Sleep(250);
        }
    }

    // ===========================================================================
    //  P/Invokes (LibraryImport — AOT-clean, source-generated marshalling)
    // ===========================================================================

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint idAttach, uint idAttachTo, [MarshalAs(UnmanagedType.Bool)] bool fAttach);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hWnd);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetConsoleWindow();

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();
}
