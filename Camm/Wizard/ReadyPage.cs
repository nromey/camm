using System.Runtime.Versioning;
using System.Windows.Forms;
using Camm.Localization;

namespace Camm.Wizard;

[SupportedOSPlatform("windows")]
public sealed class ReadyPage : UserControl, IWizardPage
{
    private readonly Label _note;
    private readonly Label _summary;
    private string _spokenSummary = string.Empty;

    public string Title => Strings.Get("Wizard.Ready.Title");
    public bool CanGoNext => true;
    public Control? InitialFocusControl => null;
    public string NextButtonText => Strings.Get("Wizard.Buttons.Install");
    public event EventHandler? CanGoNextChanged { add { } remove { } }
    public event EventHandler? AdvanceRequested { add { } remove { } }

    public string AnnouncementText =>
        Strings.Get("Wizard.Ready.Heading") + ". " + _spokenSummary + " " + _note.Text;

    public ReadyPage()
    {
        Dock = DockStyle.Fill;

        var headingText = Strings.Get("Wizard.Ready.Heading");
        var heading = new Label
        {
            Text = headingText,
            Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold),
            AutoSize = true,
            Location = new System.Drawing.Point(24, 24),
            AccessibleName = headingText,
            AccessibleRole = AccessibleRole.StaticText,
        };

        // Summary block: install location + chosen channel. Populated
        // in OnEnter from the InstallContext so a Back-then-forward
        // round-trip reflects any channel change the user just made.
        _summary = new Label
        {
            AutoSize = false,
            Location = new System.Drawing.Point(24, 80),
            Size = new System.Drawing.Size(500, 80),
            AccessibleRole = AccessibleRole.StaticText,
        };

        var noteText = Strings.Get("Wizard.Ready.Note");
        _note = new Label
        {
            Text = noteText,
            AutoSize = false,
            Location = new System.Drawing.Point(24, 180),
            Size = new System.Drawing.Size(500, 120),
            AccessibleName = noteText,
            AccessibleRole = AccessibleRole.StaticText,
        };

        Controls.Add(heading);
        Controls.Add(_summary);
        Controls.Add(_note);
    }

    public void OnEnter(InstallContext context)
    {
        // Visible summary is two lines; the spoken version flattens
        // them into one phrase for Tolk so AnnouncementText reads
        // naturally end-to-end.
        var dir = Installer.DefaultInstallDir;
        var channel = context.SelectedChannel;
        var locationLine = Strings.Get("Wizard.Ready.SummaryInstallLocation");
        var channelLine = Strings.Get("Wizard.Ready.SummaryUpdateChannel") + channel;
        _summary.Text = locationLine + "\r\n" + channelLine;
        _summary.AccessibleName = locationLine + ". " + channelLine + ".";
        _spokenSummary = _summary.AccessibleName;
    }

    public void OnLeave(InstallContext context) { }
}
