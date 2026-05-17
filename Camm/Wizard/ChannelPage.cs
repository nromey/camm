using System.Runtime.Versioning;
using System.Windows.Forms;
using Camm.Localization;
using DavyKager;

namespace Camm.Wizard;

[SupportedOSPlatform("windows")]
public sealed class ChannelPage : UserControl, IWizardPage
{
    private readonly ComboBox _combo;
    private readonly Label _description;

    public string Title => Strings.Get("Wizard.Channel.Title");
    public bool CanGoNext => true;
    public Control? InitialFocusControl => _combo;
    public event EventHandler? CanGoNextChanged { add { } remove { } }
    public event EventHandler? AdvanceRequested { add { } remove { } }

    public string AnnouncementText =>
        Strings.Get("Wizard.Channel.AnnouncementPrefix") +
        SelectedChannel + ". " + DescriptionFor(SelectedChannel);

    private UpdateChannel SelectedChannel =>
        _combo.SelectedItem is UpdateChannel ch ? ch : UpdateChannel.Stable;

    private static string DescriptionFor(UpdateChannel channel) => channel switch
    {
        UpdateChannel.Stable => Strings.Get("Wizard.Channel.Stable.Description"),
        UpdateChannel.Latest => Strings.Get("Wizard.Channel.Latest.Description"),
        UpdateChannel.Off => Strings.Get("Wizard.Channel.Off.Description"),
        _ => "",
    };

    public ChannelPage()
    {
        Dock = DockStyle.Fill;

        var headingText = Strings.Get("Wizard.Channel.Heading");
        var heading = new Label
        {
            Text = headingText,
            Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold),
            AutoSize = true,
            Location = new System.Drawing.Point(24, 24),
            AccessibleName = headingText,
            AccessibleRole = AccessibleRole.StaticText,
        };

        // Mnemonic & in the label makes Alt+U focus the combobox.
        // Plain Label preceding the ComboBox is the standard WinForms
        // "label-for-this-control" pattern; tab order excludes the
        // label so it doesn't steal focus from Tab navigation.
        var label = new Label
        {
            Text = Strings.Get("Wizard.Channel.Label"),
            AutoSize = true,
            Location = new System.Drawing.Point(24, 80),
            TabStop = false,
        };

        _combo = new ComboBox
        {
            // DropDownList = read-only; user can only pick from items.
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new System.Drawing.Point(24, 105),
            Width = 200,
            TabIndex = 0,
            AccessibleName = Strings.Get("Wizard.Channel.Heading"),
            AccessibleRole = AccessibleRole.ComboBox,
        };
        _combo.Items.Add(UpdateChannel.Stable);
        _combo.Items.Add(UpdateChannel.Latest);
        _combo.Items.Add(UpdateChannel.Off);
        _combo.SelectedItem = UpdateChannel.Stable;
        _combo.SelectedIndexChanged += OnSelectionChanged;

        var initialDesc = DescriptionFor(UpdateChannel.Stable);
        _description = new Label
        {
            Text = initialDesc,
            AutoSize = false,
            Location = new System.Drawing.Point(24, 145),
            Size = new System.Drawing.Size(500, 100),
            AccessibleName = initialDesc,
            AccessibleRole = AccessibleRole.StaticText,
        };

        Controls.Add(heading);
        Controls.Add(label);
        Controls.Add(_combo);
        Controls.Add(_description);
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        var channel = SelectedChannel;
        var desc = DescriptionFor(channel);
        _description.Text = desc;
        _description.AccessibleName = desc;
        // Explicit "<mode>. <description>" with interrupt=true gives
        // deterministic output regardless of NVDA verbosity settings;
        // fast arrowing cleanly cancels the previous readout instead
        // of stacking a queue.
        Tolk.Output(channel + ". " + desc, true);
    }

    public void OnEnter(InstallContext context)
    {
        // Restore previously-saved selection if user navigated Back
        // and forward through this page.
        _combo.SelectedItem = context.SelectedChannel;
    }

    public void OnLeave(InstallContext context)
    {
        context.SelectedChannel = SelectedChannel;
    }
}
