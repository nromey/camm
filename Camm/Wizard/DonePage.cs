using System.Runtime.Versioning;
using System.Windows.Forms;
using Camm.Localization;

namespace Camm.Wizard;

[SupportedOSPlatform("windows")]
public sealed class DonePage : UserControl, IWizardPage
{
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
    public string NextButtonText => Strings.Get("Wizard.Buttons.Finish");

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
        // read whichever state the install actually landed in.
        if (context.InstallError is null)
        {
            var heading = Strings.Get("Wizard.Done.SuccessHeading");
            var body = Strings.Get("Wizard.Done.SuccessBody");
            _heading.Text = heading;
            _heading.AccessibleName = heading;
            _body.Text = body;
            _body.AccessibleName = body;
            _announcement = heading + ". " + body;
        }
        else
        {
            var heading = Strings.Get("Wizard.Done.FailureHeading");
            var body = Strings.Get("Wizard.Done.FailureBodyPrefix") +
                       context.InstallError +
                       Strings.Get("Wizard.Done.FailureBodySuffix");
            _heading.Text = heading;
            _heading.AccessibleName = heading;
            _body.Text = body;
            _body.AccessibleName = body;
            _announcement = heading + ". " + body;
        }
    }

    public void OnLeave(InstallContext context) { }
}
