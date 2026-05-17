using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Camm.Wizard;

[SupportedOSPlatform("windows")]
public sealed class WelcomePage : UserControl, IWizardPage
{
    private static string HeadingText() =>
        $"Install {CammHost.Manifest.DisplayName}";

    private static string BodyText() =>
        "This installer will copy the launcher to Program Files " +
        $"and register {CammHost.Manifest.DisplayName} with Windows.\r\n\r\n" +
        "Windows will prompt for administrator permission later " +
        "in this installer.";

    private static string SubheadText() =>
        $"by {CammHost.Manifest.Publisher}, version " + SemVer.Current();

    private readonly Label _subhead;
    private bool _subheadVisible;

    public string Title => "Welcome";
    public bool CanGoNext => true;
    public string AnnouncementText
    {
        get
        {
            var sub = _subheadVisible ? SubheadText() + ". " : "";
            return HeadingText() + ". " + sub + BodyText();
        }
    }

    // null = host uses its default (Next button). Welcome has no
    // input control to focus, so Next is the right initial target.
    public Control? InitialFocusControl => null;

    // No-op accessors: this page never raises these events, but the
    // interface requires the members. Empty add/remove satisfies the
    // contract without triggering CS0067 on an unraised field event.
    public event EventHandler? CanGoNextChanged { add { } remove { } }
    public event EventHandler? AdvanceRequested { add { } remove { } }

    public WelcomePage()
    {
        Dock = DockStyle.Fill;

        var headingText = HeadingText();
        var heading = new Label
        {
            Text = headingText,
            Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold),
            AutoSize = true,
            Location = new System.Drawing.Point(24, 24),
            AccessibleName = headingText,
            AccessibleRole = AccessibleRole.StaticText,
        };

        // Subhead: "by <Publisher>, version X.Y.Z". Only shown on a
        // genuine first install — hidden on reinstall/update because
        // those users already know who built this. OnEnter flips
        // _subhead.Visible based on context.IsFirstInstall.
        var subText = SubheadText();
        _subhead = new Label
        {
            Text = subText,
            Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Italic),
            ForeColor = System.Drawing.SystemColors.GrayText,
            AutoSize = true,
            Location = new System.Drawing.Point(24, 60),
            AccessibleName = subText,
            AccessibleRole = AccessibleRole.StaticText,
            Visible = false,
        };

        var bodyText = BodyText();
        var body = new Label
        {
            Text = bodyText,
            AutoSize = false,
            Location = new System.Drawing.Point(24, 100),
            Size = new System.Drawing.Size(500, 200),
            AccessibleName = bodyText,
            AccessibleRole = AccessibleRole.StaticText,
        };

        Controls.Add(heading);
        Controls.Add(_subhead);
        Controls.Add(body);
    }

    public void OnEnter(InstallContext context)
    {
        _subheadVisible = context.IsFirstInstall;
        _subhead.Visible = _subheadVisible;
    }

    public void OnLeave(InstallContext context) { }
}
