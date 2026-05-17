using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Camm.Wizard;

[SupportedOSPlatform("windows")]
public sealed class DonePage : UserControl, IWizardPage
{
    private const string SuccessHeading = "Install complete";
    private const string FailureHeading = "Install failed";

    private static string SuccessBody()
    {
        var m = CammHost.Manifest;
        return
            $"{m.DisplayName} is installed.\r\n\r\n" +
            $"Launch {m.TargetGameDisplayName} from {m.TargetGameLauncherName} — " +
            "the accessibility mod activates automatically.\r\n\r\n" +
            $"Per-user settings live at %LocalAppData%\\{m.LocalAppDataFolderName}\\launcher.ini.";
    }

    private readonly Label _heading;
    private readonly Label _body;
    private string _announcement = string.Empty;

    public string Title => "Done";
    public bool CanGoNext => true;
    public Control? InitialFocusControl => null;

    // Done page is the terminal step: no Back (the install already
    // happened — going back is meaningless), no Cancel (nothing left
    // to cancel). Finish is the only action.
    public bool ShowBackButton => false;
    public bool ShowCancelButton => false;
    public string NextButtonText => "&Finish";

    public string AnnouncementText => _announcement;

    public event EventHandler? CanGoNextChanged { add { } remove { } }
    public event EventHandler? AdvanceRequested { add { } remove { } }

    public DonePage()
    {
        Dock = DockStyle.Fill;

        _heading = new Label
        {
            Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold),
            AutoSize = true,
            Location = new System.Drawing.Point(24, 24),
            AccessibleRole = AccessibleRole.StaticText,
        };

        _body = new Label
        {
            AutoSize = false,
            Location = new System.Drawing.Point(24, 80),
            Size = new System.Drawing.Size(500, 240),
            AccessibleRole = AccessibleRole.StaticText,
        };

        Controls.Add(_heading);
        Controls.Add(_body);
    }

    public void OnEnter(InstallContext context)
    {
        // Variant selection happens at activation time so OnEnter can
        // read whichever state the install actually landed in. The
        // heading + body Labels are dynamic; AnnouncementText also
        // tracks via _announcement so the spoken text matches what's
        // on screen.
        if (context.InstallError is null)
        {
            var successBody = SuccessBody();
            _heading.Text = SuccessHeading;
            _heading.AccessibleName = SuccessHeading;
            _body.Text = successBody;
            _body.AccessibleName = successBody;
            _announcement = SuccessHeading + ". " + successBody;
        }
        else
        {
            var bodyText =
                "The installer could not complete.\r\n\r\n" +
                "Reason: " + context.InstallError + "\r\n\r\n" +
                "Nothing has been changed permanently. You can close " +
                "this window and run the installer again to retry.";
            _heading.Text = FailureHeading;
            _heading.AccessibleName = FailureHeading;
            _body.Text = bodyText;
            _body.AccessibleName = bodyText;
            _announcement = FailureHeading + ". " + bodyText;
        }
    }

    public void OnLeave(InstallContext context) { }
}
