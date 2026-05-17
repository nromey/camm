using System.Runtime.Versioning;
using System.Windows.Forms;
using DavyKager;

namespace Camm.Wizard;

[SupportedOSPlatform("windows")]
public sealed class ChannelPage : UserControl, IWizardPage
{
    private const string HeadingText = "Update channel";

    // Per-channel description shown live below the combobox AND spoken
    // through Tolk when the selection changes. Step 7 of the extraction
    // plan moves these into the locale catalog (lang/en.json) so the
    // wording can be translated.
    private static readonly Dictionary<UpdateChannel, string> Descriptions = new()
    {
        [UpdateChannel.Stable] =
            "Tested releases only. Safest; gets new features after they've been validated.",
        [UpdateChannel.Latest] =
            "Includes pre-release builds. Newer features but may be rougher. Good for testers.",
        [UpdateChannel.Off] =
            "Never check for updates. You will miss bug fixes and new screen support.",
    };

    private readonly ComboBox _combo;
    private readonly Label _description;

    public string Title => "Update channel";
    public bool CanGoNext => true;
    public Control? InitialFocusControl => _combo;
    public event EventHandler? CanGoNextChanged { add { } remove { } }
    public event EventHandler? AdvanceRequested { add { } remove { } }

    public string AnnouncementText =>
        HeadingText + ". Currently selected: " + SelectedChannel + ". " +
        Descriptions[SelectedChannel];

    private UpdateChannel SelectedChannel =>
        _combo.SelectedItem is UpdateChannel ch ? ch : UpdateChannel.Stable;

    public ChannelPage()
    {
        Dock = DockStyle.Fill;

        var heading = new Label
        {
            Text = HeadingText,
            Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold),
            AutoSize = true,
            Location = new System.Drawing.Point(24, 24),
            AccessibleName = HeadingText,
            AccessibleRole = AccessibleRole.StaticText,
        };

        // Mnemonic & makes Alt+U focus the combobox directly. Plain
        // Label preceding the ComboBox is the standard WinForms
        // "label-for-this-control" pattern; tab order puts the label
        // last so it doesn't steal focus from Tab navigation.
        var label = new Label
        {
            Text = "&Update channel:",
            AutoSize = true,
            Location = new System.Drawing.Point(24, 80),
            TabStop = false,
        };

        _combo = new ComboBox
        {
            // DropDownList = read-only; user can only pick from items,
            // not type free text. Eliminates a class of invalid input
            // and gives NVDA cleaner combobox semantics.
            DropDownStyle = ComboBoxStyle.DropDownList,
            Location = new System.Drawing.Point(24, 105),
            Width = 200,
            TabIndex = 0,
            AccessibleName = "Update channel",
            AccessibleRole = AccessibleRole.ComboBox,
        };
        _combo.Items.Add(UpdateChannel.Stable);
        _combo.Items.Add(UpdateChannel.Latest);
        _combo.Items.Add(UpdateChannel.Off);
        _combo.SelectedItem = UpdateChannel.Stable;
        _combo.SelectedIndexChanged += OnSelectionChanged;

        _description = new Label
        {
            Text = Descriptions[UpdateChannel.Stable],
            AutoSize = false,
            Location = new System.Drawing.Point(24, 145),
            Size = new System.Drawing.Size(500, 100),
            AccessibleName = Descriptions[UpdateChannel.Stable],
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
        var desc = Descriptions[channel];
        _description.Text = desc;
        _description.AccessibleName = desc;
        // Explicit "<mode>. <description>" with interrupt=true gives
        // deterministic output regardless of NVDA verbosity settings,
        // and arrowing fast through items cleanly cancels the previous
        // readout instead of stacking a queue. Trade-off accepted:
        // forgo NVDA's automatic combobox-selection announce in
        // exchange for predictability.
        Tolk.Output(channel + ". " + desc, true);
    }

    public void OnEnter(InstallContext context)
    {
        // Restore previously-saved selection if user navigated Back
        // and forward through this page. Subscribed handler does NOT
        // re-fire on programmatic SelectedItem assignment to the same
        // value, so we're safe from echo-speaking on entry.
        _combo.SelectedItem = context.SelectedChannel;
    }

    public void OnLeave(InstallContext context)
    {
        context.SelectedChannel = SelectedChannel;
    }
}
