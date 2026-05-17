using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Camm;

// Native Win32 MessageBox + TaskDialog wrapper. Used for installer UX
// (welcome, confirm, completion) rather than printing to a hidden
// console window that the user never sees.
//
// Why MessageBox specifically:
//   - AOT-friendly (raw P/Invoke, no reflection)
//   - Screen-reader accessible out of the box — Windows itself announces
//     MessageBox title + content + button labels to assistive tech, so
//     blind users get install/uninstall feedback through their existing
//     screen reader instead of needing Tolk-side speech
//   - No UI framework dependency (no WinForms / WPF / Avalonia weight)
//
// Discovered 2026-05-15: an unowned MessageBox (hWnd = IntPtr.Zero)
// with just MB_TOPMOST does NOT reliably surface on Windows 11 — the
// dialog gets created and waits for input but never visually appears
// or gets announced by screen readers. Win11's foreground-stealing
// protection is stricter than earlier Windows versions.
//
// Fix: pass the console window's HWND as the dialog owner. This:
//   - Gives the dialog a proper parent window in the user's session
//   - Makes screen readers (NVDA, JAWS) reliably announce the content
//   - Forces the dialog to appear above the console rather than
//     hidden in the Z-order
//   - Lets MB_SYSTEMMODAL behave as "stay on top of everything in this
//     user session," which is what we want for installer prompts
//
// Callers running a real UI (e.g. an install wizard form) pass their
// form's HWND via the ownerHwnd parameter on ShowChoice; that parents
// the TaskDialog on the form instead of the console.
[SupportedOSPlatform("windows")]
public static partial class Dialogs
{
    [Flags]
    private enum MBFlags : uint
    {
        OK = 0x00000000,
        OKCancel = 0x00000001,
        YesNoCancel = 0x00000003,
        YesNo = 0x00000004,
        IconInformation = 0x00000040,
        IconQuestion = 0x00000020,
        IconWarning = 0x00000030,
        IconError = 0x00000010,
        DefaultButton1 = 0x00000000,
        SystemModal = 0x00001000,
        Topmost = 0x00040000,
        SetForeground = 0x00010000,
    }

    private const int IDOK = 1;
    private const int IDCANCEL = 2;
    private const int IDYES = 6;
    private const int IDNO = 7;

    // Common flags applied to every dialog. SystemModal + Topmost +
    // SetForeground is the belt-and-suspenders combination that
    // empirically gets dialogs visible + screen-reader-announced on
    // Windows 11.
    private const uint VisibilityFlags =
        (uint)(MBFlags.SystemModal | MBFlags.Topmost | MBFlags.SetForeground);

    [LibraryImport("user32.dll", EntryPoint = "MessageBoxW",
        StringMarshalling = StringMarshalling.Utf16)]
    private static partial int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    [LibraryImport("kernel32.dll")]
    private static partial IntPtr GetConsoleWindow();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hWnd);

    private const int SW_RESTORE = 9;

    public enum Result { Ok, Cancel, Yes, No }

    // Find a usable owner HWND for our dialog. Prefers the console
    // window if our process has one; falls back to IntPtr.Zero (which
    // works less reliably but doesn't crash). Also pokes the console
    // to the foreground first so the dialog parents on a foreground
    // window, satisfying Win11's foreground-stealing rules.
    private static IntPtr GetOwnerHwnd()
    {
        var hwnd = GetConsoleWindow();
        if (hwnd != IntPtr.Zero)
        {
            try { ShowWindow(hwnd, SW_RESTORE); } catch { }
            try { SetForegroundWindow(hwnd); } catch { }
        }
        return hwnd;
    }

    public static Result ShowInfo(string title, string message)
    {
        var hwnd = GetOwnerHwnd();
        MessageBox(hwnd, message, title,
            VisibilityFlags | (uint)(MBFlags.OK | MBFlags.IconInformation));
        return Result.Ok;
    }

    public static Result Confirm(string title, string message)
    {
        var hwnd = GetOwnerHwnd();
        var rc = MessageBox(hwnd, message, title,
            VisibilityFlags | (uint)(MBFlags.OKCancel | MBFlags.IconQuestion));
        return rc == IDOK ? Result.Ok : Result.Cancel;
    }

    public static Result YesNo(string title, string message)
    {
        var hwnd = GetOwnerHwnd();
        var rc = MessageBox(hwnd, message, title,
            VisibilityFlags | (uint)(MBFlags.YesNo | MBFlags.IconQuestion));
        return rc == IDYES ? Result.Yes : Result.No;
    }

    // Three-button Yes/No/Cancel dialog. MessageBox doesn't let us
    // re-label native buttons, so the caller's message text must spell
    // out what Yes vs No mean. Returns Yes / No / Cancel mapped from
    // IDYES / IDNO / IDCANCEL.
    //
    // For custom button labels (e.g., "Reinstall" / "Uninstall" /
    // "Cancel" as actual button captions), use ShowChoice instead.
    public static Result YesNoCancel(string title, string message)
    {
        var hwnd = GetOwnerHwnd();
        var rc = MessageBox(hwnd, message, title,
            VisibilityFlags | (uint)(MBFlags.YesNoCancel | MBFlags.IconQuestion));
        return rc switch
        {
            IDYES => Result.Yes,
            IDNO => Result.No,
            _ => Result.Cancel,
        };
    }

    public static void ShowError(string title, string message)
    {
        var hwnd = GetOwnerHwnd();
        MessageBox(hwnd, message, title,
            VisibilityFlags | (uint)(MBFlags.OK | MBFlags.IconError));
    }

    // =====================================================================
    // TaskDialog (comctl32 v6+) for multi-action choice dialogs.
    //
    // MessageBox can't relabel its native buttons — they're always "OK" /
    // "Yes" / "No" / "Cancel" / "Retry" / "Ignore". For choice dialogs
    // where each button means a distinct action ("Reinstall" vs
    // "Uninstall" vs "Change settings"), that limitation matters because:
    //
    //   1. Screen readers announce the literal button label — "Yes
    //      button" is meaningless when it means Reinstall.
    //   2. Sighted users scanning the dialog have to read the body text
    //      to figure out which button is which action.
    //
    // TaskDialog with TDF_USE_COMMAND_LINKS renders each choice as a
    // full-width vertical button with a heading line + optional note,
    // and screen readers announce both.
    //
    // AOT-clean: raw P/Invoke + unsafe pointer arithmetic for the
    // TASKDIALOG_BUTTON array (Marshal.StructureToPtr is iffy under
    // AOT for packed structs). Native string allocations are paired
    // with FreeHGlobal in a try/finally so partial failures don't leak.
    //
    // Requires the process to have a manifest declaring a dependency
    // on Microsoft.Windows.Common-Controls v6.0.0.0 — without this,
    // the default comctl32 v5 is loaded and TaskDialogIndirect throws
    // EntryPointNotFoundException at first call. The consuming exe's
    // app.manifest needs the Common-Controls v6 dependency declaration.
    // =====================================================================

    public readonly record struct ChoiceButton(int Id, string Heading, string? Note = null);

    // Common return values for ShowChoice.
    public const int ChoiceCancelId = IDCANCEL;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct TASKDIALOGCONFIG
    {
        public uint cbSize;
        public IntPtr hwndParent;
        public IntPtr hInstance;
        public uint dwFlags;
        public uint dwCommonButtons;
        public IntPtr pszWindowTitle;
        // Union with hMainIcon — when TDF_USE_HICON_MAIN is NOT set, this
        // is a MAKEINTRESOURCE pointer to a stock icon (TD_INFORMATION_ICON
        // = -1, TD_WARNING_ICON = -2, TD_ERROR_ICON = -3, TD_SHIELD_ICON
        // = -4). We pass these as IntPtr(-1) etc.
        public IntPtr pszMainIcon;
        public IntPtr pszMainInstruction;
        public IntPtr pszContent;
        public uint cButtons;
        public IntPtr pButtons;
        public int nDefaultButton;
        public uint cRadioButtons;
        public IntPtr pRadioButtons;
        public int nDefaultRadioButton;
        public IntPtr pszVerificationText;
        public IntPtr pszExpandedInformation;
        public IntPtr pszExpandedControlText;
        public IntPtr pszCollapsedControlText;
        public IntPtr pszFooterIcon;
        public IntPtr pszFooter;
        public IntPtr pfCallback;
        public IntPtr lpCallbackData;
        public uint cxWidth;
    }

    // TASKDIALOG_BUTTON is also Pack=1 in commctrl.h. On x64 that's
    // 4-byte ID + 8-byte text pointer = 12 bytes, NOT 16. We allocate
    // and populate the button array via unsafe pointer writes to avoid
    // Marshal.StructureToPtr AOT quirks.
    private const int TaskDialogButtonSize = 12;

    private const uint TDF_USE_HICON_MAIN = 0x0002;
    private const uint TDF_ALLOW_DIALOG_CANCELLATION = 0x0008;
    private const uint TDF_USE_COMMAND_LINKS = 0x0010;
    private const uint TDF_POSITION_RELATIVE_TO_WINDOW = 0x1000;

    // Common-button flags. We avoid these for choice dialogs (we want
    // every action to be a custom command link) but TDCBF_CANCEL_BUTTON
    // is occasionally useful when Esc-to-cancel + a clickable Cancel
    // button are both wanted without a custom command link for Cancel.
    private const uint TDCBF_CANCEL_BUTTON = 0x0008;

    // Stock icon IDs. Windows' MAKEINTRESOURCEW macro casts the integer
    // to 16-bit unsigned WORD first, THEN extends to pointer width —
    // (WORD)(-1) is 0xFFFF, not 0xFFFFFFFFFFFFFFFF. Using new IntPtr(-1)
    // directly sign-extends to a kernel-space pointer that comctl32
    // then dereferences, causing an access violation. Use the 16-bit
    // unsigned form explicitly.
    private static readonly IntPtr TD_WARNING_ICON = (IntPtr)(uint)0xFFFF;       // MAKEINTRESOURCE(-1)
    private static readonly IntPtr TD_ERROR_ICON = (IntPtr)(uint)0xFFFE;         // MAKEINTRESOURCE(-2)
    private static readonly IntPtr TD_INFORMATION_ICON = (IntPtr)(uint)0xFFFD;   // MAKEINTRESOURCE(-3)
    private static readonly IntPtr TD_SHIELD_ICON = (IntPtr)(uint)0xFFFC;        // MAKEINTRESOURCE(-4)

    private enum DialogIcon { None, Information, Warning, Error }

    [LibraryImport("comctl32.dll", EntryPoint = "TaskDialogIndirect")]
    private static partial int TaskDialogIndirect(
        ref TASKDIALOGCONFIG pTaskConfig,
        out int pnButton,
        out int pnRadioButton,
        [MarshalAs(UnmanagedType.Bool)] out bool pfVerificationFlagChecked);

    // Show a multi-button choice dialog with command-link buttons.
    // Returns the ID of the chosen button, or ChoiceCancelId (2) if the
    // user pressed Esc or closed the dialog.
    //
    // Each ChoiceButton renders as a full-width vertical command link.
    // If Note is non-null, it appears below the Heading in smaller text;
    // screen readers announce both.
    //
    // The dialog gains Esc-to-cancel by default. To suppress (force
    // explicit choice), pass allowCancel: false.
    //
    // When called from inside a WinForms (or similar) UI, pass the host
    // form's HWND via ownerHwnd so the TaskDialog parents on the form
    // rather than the console window. Default null falls back to the
    // console-window-as-owner behavior that handles Win11's foreground-
    // stealing rules for console-only callers.
    public static int ShowChoice(
        string title,
        string mainInstruction,
        string content,
        ChoiceButton[] choices,
        bool allowCancel = true,
        int defaultChoiceId = 0,
        bool warningIcon = false,
        IntPtr? ownerHwnd = null)
    {
        if (choices is null || choices.Length == 0)
        {
            throw new ArgumentException("ShowChoice requires at least one choice button.", nameof(choices));
        }

        var effectiveOwner = ownerHwnd ?? GetOwnerHwnd();

        // Build the button text strings: TDF_USE_COMMAND_LINKS treats
        // the first \n as a heading-to-note separator, so concatenate
        // "Heading\nNote" when Note is non-null. Empty/null note → just
        // the heading.
        var buttonTextPtrs = new IntPtr[choices.Length];
        IntPtr titlePtr = IntPtr.Zero;
        IntPtr instructionPtr = IntPtr.Zero;
        IntPtr contentPtr = IntPtr.Zero;
        IntPtr buttonArrayPtr = IntPtr.Zero;

        try
        {
            titlePtr = Marshal.StringToHGlobalUni(title);
            instructionPtr = Marshal.StringToHGlobalUni(mainInstruction);
            contentPtr = Marshal.StringToHGlobalUni(content);

            for (int i = 0; i < choices.Length; i++)
            {
                var c = choices[i];
                var text = string.IsNullOrEmpty(c.Note) ? c.Heading : c.Heading + "\n" + c.Note;
                buttonTextPtrs[i] = Marshal.StringToHGlobalUni(text);
            }

            buttonArrayPtr = Marshal.AllocHGlobal(TaskDialogButtonSize * choices.Length);
            unsafe
            {
                byte* basePtr = (byte*)buttonArrayPtr;
                for (int i = 0; i < choices.Length; i++)
                {
                    byte* btnPtr = basePtr + i * TaskDialogButtonSize;
                    *(int*)btnPtr = choices[i].Id;
                    // IntPtr write at offset 4 — Pack=1 means no alignment
                    // padding. x64 handles unaligned 8-byte writes fine.
                    *(IntPtr*)(btnPtr + 4) = buttonTextPtrs[i];
                }
            }

            uint flags = TDF_USE_COMMAND_LINKS | TDF_POSITION_RELATIVE_TO_WINDOW;
            if (allowCancel) flags |= TDF_ALLOW_DIALOG_CANCELLATION;

            var config = new TASKDIALOGCONFIG
            {
                cbSize = (uint)Marshal.SizeOf<TASKDIALOGCONFIG>(),
                hwndParent = effectiveOwner,
                hInstance = IntPtr.Zero,
                dwFlags = flags,
                dwCommonButtons = 0,
                pszWindowTitle = titlePtr,
                pszMainIcon = warningIcon ? TD_WARNING_ICON : TD_INFORMATION_ICON,
                pszMainInstruction = instructionPtr,
                pszContent = contentPtr,
                cButtons = (uint)choices.Length,
                pButtons = buttonArrayPtr,
                nDefaultButton = defaultChoiceId,
                cRadioButtons = 0,
                pRadioButtons = IntPtr.Zero,
                nDefaultRadioButton = 0,
                pszVerificationText = IntPtr.Zero,
                pszExpandedInformation = IntPtr.Zero,
                pszExpandedControlText = IntPtr.Zero,
                pszCollapsedControlText = IntPtr.Zero,
                pszFooterIcon = IntPtr.Zero,
                pszFooter = IntPtr.Zero,
                pfCallback = IntPtr.Zero,
                lpCallbackData = IntPtr.Zero,
                cxWidth = 0,
            };

            int hr = TaskDialogIndirect(ref config, out int pressedButton, out _, out _);
            if (hr != 0)
            {
                // TaskDialogIndirect failed — fall back to returning
                // Cancel rather than throwing into a caller that's
                // mid-install-flow. Caller logs the unexpected return.
                return ChoiceCancelId;
            }
            return pressedButton;
        }
        finally
        {
            if (titlePtr != IntPtr.Zero) Marshal.FreeHGlobal(titlePtr);
            if (instructionPtr != IntPtr.Zero) Marshal.FreeHGlobal(instructionPtr);
            if (contentPtr != IntPtr.Zero) Marshal.FreeHGlobal(contentPtr);
            foreach (var p in buttonTextPtrs)
            {
                if (p != IntPtr.Zero) Marshal.FreeHGlobal(p);
            }
            if (buttonArrayPtr != IntPtr.Zero) Marshal.FreeHGlobal(buttonArrayPtr);
        }
    }
}
