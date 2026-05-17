using System.Runtime.Versioning;
using Camm.Localization;

namespace Camm;

// Update-channel picker — wrapper around Dialogs.ShowChoice that
// renders a four-button command-link dialog. Used by the launcher's
// --config entry point and by the Already-Installed dialog's "Change
// update channel" branch.
//
// All visible strings come from the locale catalog (see
// Camm/lang/en.json); the title and dialog content read DisplayName
// from the manifest via the substitution layer in Strings.Get.
[SupportedOSPlatform("windows")]
public static class ChannelPickerDialog
{
    // Single TaskDialog with four command-link buttons (Stable /
    // Latest / Off / Keep current). Each button has its own heading
    // line and explanation; screen readers announce both. Returns the
    // selected channel, or null if the user pressed Esc or picked
    // "Keep current".
    public static UpdateChannel? Show(UpdateChannel currentChannel)
    {
        const int ID_STABLE = 101;
        const int ID_LATEST = 102;
        const int ID_OFF = 103;
        const int ID_KEEP = 104;

        var currentLabel = currentChannel switch
        {
            UpdateChannel.Latest => "Latest",
            UpdateChannel.Off => "Off",
            _ => "Stable",
        };

        var choice = Dialogs.ShowChoice(
            title: Strings.Get("ChannelPicker.Title"),
            mainInstruction: Strings.Get("ChannelPicker.Instruction"),
            content: Strings.Get("ChannelPicker.CurrentLabel") + currentLabel,
            choices: new[]
            {
                new Dialogs.ChoiceButton(ID_STABLE,
                    Strings.Get("ChannelPicker.Stable.Heading"),
                    Strings.Get("ChannelPicker.Stable.Note")),
                new Dialogs.ChoiceButton(ID_LATEST,
                    Strings.Get("ChannelPicker.Latest.Heading"),
                    Strings.Get("ChannelPicker.Latest.Note")),
                new Dialogs.ChoiceButton(ID_OFF,
                    Strings.Get("ChannelPicker.Off.Heading"),
                    Strings.Get("ChannelPicker.Off.Note")),
                new Dialogs.ChoiceButton(ID_KEEP,
                    Strings.Get("ChannelPicker.Keep.HeadingPrefix"),
                    Strings.Get("ChannelPicker.Keep.NotePrefix") + currentLabel +
                    Strings.Get("ChannelPicker.Keep.NoteSuffix")),
            },
            defaultChoiceId: ID_STABLE);

        return choice switch
        {
            ID_STABLE => UpdateChannel.Stable,
            ID_LATEST => UpdateChannel.Latest,
            ID_OFF => UpdateChannel.Off,
            _ => null,
        };
    }
}
