using System.Runtime.Versioning;
using System.Windows.Forms;
using Camm.Localization;

namespace Camm.Wizard;

[SupportedOSPlatform("windows")]
public sealed class WelcomePage : UserControl, IWizardPage
{
    private readonly Label _subhead;
    private bool _subheadVisible;

    public string Title => Strings.Get("Wizard.Welcome.Title");
    public bool CanGoNext => true;
    public string AnnouncementText
    {
        get
        {
            var heading = Strings.Get("Wizard.Welcome.Heading");
            var body = Strings.Get("Wizard.Welcome.Body");
            var sub = _subheadVisible
                ? Strings.Get("Wizard.Welcome.Subhead") + ". "
                : "";
            return heading + ". " + sub + body;
        }
    }

    // null = host uses its default (Next button). Welcome has no
    // input control to focus, so Next is the right initial target.
    public Control? InitialFocusControl => null;

    // No-op accessors: this page never raises these events.
    public event EventHandler? CanGoNextChanged { add { } remove { } }
    public event EventHandler? AdvanceRequested { add { } remove { } }

    public WelcomePage()
    {
        Dock = DockStyle.Fill;

        var headingText = Strings.Get("Wizard.Welcome.Heading");
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
        var subText = Strings.Get("Wizard.Welcome.Subhead");
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

        var bodyText = Strings.Get("Wizard.Welcome.Body");
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
