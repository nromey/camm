using System.Runtime.Versioning;
using System.Windows.Forms;
using DavyKager;

namespace Camm.Wizard;

// Host Form for the install wizard. Stays open through the entire
// install flow; pages swap in/out of _pageHost via UserControl
// replacement. The bottom button bar is owned here; pages only
// influence button state via CanGoNext + CanGoNextChanged.
//
// Architecture decision: single Form + UserControl swap, not sequential
// Form objects opening/closing. Title bar stays put, page contents
// shift inside the host panel — feels stable, no flicker.
//
// Visible strings (form title, page content, dialog text) come from
// CammHost.Manifest at construction time, so per-mod identity comes
// from one configuration source.
[SupportedOSPlatform("windows")]
public sealed class InstallWizardForm : Form
{
    private readonly Panel _pageHost;
    private readonly Button _btnBack;
    private readonly Button _btnNext;
    private readonly Button _btnCancel;
    private readonly List<IWizardPage> _pages = new();
    private readonly InstallContext _context;
    private int _index = -1;
    private System.Windows.Forms.Timer? _speakTimer;

    // True once the user has confirmed cancel via the TaskDialog, so
    // OnFormClosing knows to let Close() through without re-prompting.
    // Also true when a programmatic close happens (post-install Finish
    // click, etc.) so those don't trigger a spurious confirm.
    private bool _cancelConfirmed;

    public InstallWizardForm(InstallContext context)
    {
        _context = context;
        Text = $"{CammHost.Manifest.DisplayName} Setup";
        ClientSize = new System.Drawing.Size(560, 420);
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimizeBox = false;
        MaximizeBox = false;

        _pageHost = new Panel { Dock = DockStyle.Fill };
        Controls.Add(_pageHost);

        // Button bar pinned bottom. Order Back / Next / Cancel left-
        // to-right matches Windows InstallShield convention.
        var buttonBar = new Panel { Dock = DockStyle.Bottom, Height = 48 };

        // CAMM attribution footer. Mandatory across all CAMM-built
        // installers — the branding pays for the reusable framework.
        // Lives on the host form so every page inherits it without
        // per-page work.
        var footer = new Label
        {
            Text = "Powered by CAMM — Chameleon Access Mod Manager",
            Dock = DockStyle.Bottom,
            Height = 22,
            TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
            Font = new System.Drawing.Font("Segoe UI", 8F, System.Drawing.FontStyle.Italic),
            ForeColor = System.Drawing.SystemColors.GrayText,
            AccessibleName = "Powered by CAMM, the Chameleon Access Mod Manager",
        };
        // TabIndex makes Tab cycle Next → Cancel → Back (Back becomes
        // reachable when enabled on page 2+). Next gets the lowest
        // index because it's the primary action on every page; Cancel
        // is always second so users can dismiss quickly from keyboard.
        _btnCancel = new Button
        {
            Text = "&Cancel",
            Width = 90,
            Top = 10,
            Left = 450,
            TabIndex = 1,
            DialogResult = DialogResult.Cancel,
            AccessibleName = "Cancel",
        };
        _btnNext = new Button
        {
            Text = "&Next",
            Width = 90,
            Top = 10,
            Left = 354,
            TabIndex = 0,
            AccessibleName = "Next",
        };
        _btnBack = new Button
        {
            Text = "&Back",
            Width = 90,
            Top = 10,
            Left = 258,
            TabIndex = 2,
            AccessibleName = "Back",
        };
        buttonBar.Controls.Add(_btnBack);
        buttonBar.Controls.Add(_btnNext);
        buttonBar.Controls.Add(_btnCancel);
        // Docking order: WinForms processes Dock.Bottom controls in
        // reverse Z-order (highest Z first against the edge). Adding
        // footer BEFORE buttonBar gives buttonBar the very bottom edge
        // (last-added → highest Z → docks first) and pushes footer up
        // to sit just above the buttons.
        Controls.Add(footer);
        Controls.Add(buttonBar);

        CancelButton = _btnCancel;
        AcceptButton = _btnNext;

        _btnBack.Click += (_, _) => Navigate(-1);
        _btnNext.Click += (_, _) => Navigate(+1);
        // Cancel click routes through HandleCancel so the confirm
        // dialog fires. Close() then triggers OnFormClosing again,
        // which sees the _cancelConfirmed flag and skips re-prompting.
        _btnCancel.Click += (_, _) => HandleCancel();

        AddPage(new WelcomePage());
        AddPage(new ChannelPage());
        AddPage(new ReadyPage());
        AddPage(new InstallingPage());
        AddPage(new DonePage());
        // UI-only show during construction. ActivatePage (in OnShown
        // and Navigate) drives OnEnter + focus + Tolk speak — keeping
        // those out of the constructor avoids speaking before the
        // form is visible AND avoids racing NVDA's own focus event.
        ShowPageUi(0);
    }

    private void AddPage(IWizardPage page)
    {
        _pages.Add(page);
        // Subscribe once per page lifetime, then dispatch only to the
        // active page. Avoids double-subscription when the user goes
        // Back and we re-show an existing page.
        page.CanGoNextChanged += (s, _) =>
        {
            if (_index >= 0 && ReferenceEquals(_pages[_index], s)) UpdateButtons();
        };
        // AdvanceRequested lets pages that do async work (Installing)
        // tell the host "advance now" without holding a reference to
        // the host. Same active-page guard as CanGoNextChanged.
        page.AdvanceRequested += (s, _) =>
        {
            if (_index >= 0 && ReferenceEquals(_pages[_index], s)) Navigate(+1);
        };
    }

    private void Navigate(int delta)
    {
        if (_index < 0) return;
        _pages[_index].OnLeave(_context);
        var target = _index + delta;
        if (target < 0 || target >= _pages.Count)
        {
            // Past the last page = Finish on the Done page. Programmatic
            // close — skip the cancel-confirm path because there's
            // nothing left to cancel.
            _cancelConfirmed = true;
            Close();
            return;
        }
        ShowPageUi(target);
        ActivatePage();
    }

    // Cancel-confirm dialog. Pops a TaskDialog parented on the wizard
    // form (not the console — passing our HWND keeps Z-order sane).
    // Returns true if user confirms cancel, false to stay on the
    // current page.
    //
    // Wired to both _btnCancel.Click and (via OnFormClosing) the
    // title-bar X / Esc-as-cancel-shortcut.
    private bool ConfirmCancel()
    {
        const int ID_CONTINUE = 1;
        const int ID_CANCEL = 2;
        var choice = Dialogs.ShowChoice(
            title: "Cancel installation?",
            mainInstruction: "Are you sure you want to cancel?",
            content:
                "Nothing has been installed yet. You can come back to " +
                "the installer any time.",
            choices: new[]
            {
                new Dialogs.ChoiceButton(ID_CONTINUE, "Continue installing",
                    "Return to the wizard."),
                new Dialogs.ChoiceButton(ID_CANCEL, "Yes, cancel and exit",
                    "Close the installer without making any changes."),
            },
            defaultChoiceId: ID_CONTINUE,
            warningIcon: true,
            ownerHwnd: Handle);
        return choice == ID_CANCEL;
    }

    private void HandleCancel()
    {
        if (ConfirmCancel())
        {
            _cancelConfirmed = true;
            Close();
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && !_cancelConfirmed && _index >= 0)
        {
            var page = _pages[_index];
            if (!page.ButtonsEnabled)
            {
                // Installing page — block close entirely. Aborting
                // mid-install isn't safe once UAC has been granted
                // and files are mid-copy.
                e.Cancel = true;
            }
            else if (page.ShowCancelButton)
            {
                if (!ConfirmCancel()) e.Cancel = true;
                else _cancelConfirmed = true;
            }
            // ShowCancelButton == false (Done page) — let close proceed.
        }
        base.OnFormClosing(e);
    }

    // UI-only swap: install the page UserControl, update the button bar,
    // and place initial focus. Does NOT call OnEnter or fire the Tolk
    // announcement — those run later in ActivatePage so the form is
    // already visible and NVDA's own focus event has fired first.
    private void ShowPageUi(int newIndex)
    {
        _index = newIndex;
        var page = _pages[_index];
        _pageHost.Controls.Clear();
        if (page is UserControl uc)
        {
            uc.Dock = DockStyle.Fill;
            _pageHost.Controls.Add(uc);
        }
        UpdateButtons();
        ApplyNextButtonText(page);
        SetPageFocus();
    }

    // Pages that commit irreversible actions (Ready → Install) relabel
    // the Next button so the user reading by keyboard knows the next
    // click is meaningful. The mnemonic prefix `&` is stripped for the
    // AccessibleName so screen readers say "Install button" not
    // "ampersand Install button".
    private void ApplyNextButtonText(IWizardPage page)
    {
        var text = page.NextButtonText;
        _btnNext.Text = text;
        _btnNext.AccessibleName = text.Replace("&", "");
    }

    // Fires the page lifecycle: OnEnter + delayed Tolk announcement.
    // Called from OnShown for the first page and from Navigate for
    // subsequent transitions, AFTER the form is visible.
    private void ActivatePage()
    {
        if (_index < 0) return;
        var page = _pages[_index];
        page.OnEnter(_context);
        DelayedSpeak(page.AnnouncementText);
    }

    private void UpdateButtons()
    {
        var page = _pages[_index];
        var globallyEnabled = page.ButtonsEnabled;
        _btnBack.Visible = page.ShowBackButton;
        _btnBack.Enabled = globallyEnabled && _index > 0;
        _btnNext.Visible = !string.IsNullOrEmpty(page.NextButtonText);
        _btnNext.Enabled = globallyEnabled && page.CanGoNext;
        _btnCancel.Visible = page.ShowCancelButton;
        _btnCancel.Enabled = globallyEnabled;
    }

    // Initial focus per page. Page-specific override (combobox on
    // Channel, Install button on Ready, etc.) via InitialFocusControl;
    // falls back to Next when enabled, Cancel otherwise. Set
    // ActiveControl when the form isn't visible yet (constructor
    // path) and Focus() once it is.
    private void SetPageFocus()
    {
        var fallback = _btnNext.Enabled ? _btnNext : (Control)_btnCancel;
        var target = (_index >= 0 ? _pages[_index].InitialFocusControl : null) ?? fallback;
        if (IsHandleCreated && Visible) target.Focus();
        else ActiveControl = target;
    }

    // Delayed speak via a UI Timer. The 250ms gap lets NVDA process
    // its own focus / window-shown announcements first; our
    // interrupt=true call then wipes those and speaks the page
    // content cleanly. Without the delay, NVDA's focus event fires
    // AFTER our Tolk.Output and the user only hears "Next button".
    //
    // Reusing one timer instance means a fast page change (Back-then-
    // Next) cancels the pending speak instead of stacking two.
    private void DelayedSpeak(string text)
    {
        _speakTimer?.Stop();
        _speakTimer?.Dispose();
        _speakTimer = new System.Windows.Forms.Timer { Interval = 250 };
        _speakTimer.Tick += (_, _) =>
        {
            _speakTimer?.Stop();
            _speakTimer?.Dispose();
            _speakTimer = null;
            try
            {
                var ok = Tolk.Output(text, true);
                Logger.Info($"InstallWizardForm.DelayedSpeak: " +
                    $"Tolk.Output returned {ok}, len={text.Length}");
            }
            catch (Exception ex)
            {
                Logger.Exception("InstallWizardForm.DelayedSpeak: Tolk.Output threw", ex);
            }
        };
        _speakTimer.Start();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // Form is now visible and focus is on Next (via ActiveControl
        // from the constructor's SetPageFocus). Re-Focus to refresh
        // the system's focus event, then fire OnEnter + delayed Tolk
        // for the first page.
        SetPageFocus();
        ActivatePage();
    }

    // Entry point for callers. WinForms requires an STA thread; the
    // launcher's main thread is MTA because top-level statements
    // can't carry [STAThread]. Spawn a dedicated UI thread, run the
    // message loop there, and join when the form closes.
    //
    // Parameterless overload constructs a default InstallContext
    // (IsDryRun = true) — appropriate for wizard-test dev iteration.
    // Real install flow constructs the context with IsDryRun=false.
    public static void Run() => Run(new InstallContext());

    public static void Run(InstallContext context)
    {
        var ui = new Thread(() =>
        {
            // Manual configuration in lieu of ApplicationConfiguration.Initialize()
            // — that source-generated helper only emits in WindowsApplication
            // outputs, but CAMM is a library. Replicate what Initialize would
            // have called.
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.Run(new InstallWizardForm(context));
        });
        ui.SetApartmentState(ApartmentState.STA);
        ui.Start();
        ui.Join();
    }
}
