using System.Runtime.Versioning;

namespace Camm;

// Update-channel picker — wrapper around Dialogs.ShowChoice that
// renders a four-button command-link dialog. Used by the launcher's
// --config entry point and by the Already-Installed dialog's "Change
// update channel" branch.
//
// Title + content come from CammHost.Manifest at call time so the
// dialog reads "<DisplayName> — Update Channel" naturally.
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

        var displayName = CammHost.Manifest.DisplayName;
        var choice = Dialogs.ShowChoice(
            title: $"{displayName} — Update Channel",
            mainInstruction: $"Choose how {displayName} checks for updates",
            content: $"Current update channel: {currentLabel}",
            choices: new[]
            {
                new Dialogs.ChoiceButton(ID_STABLE, "Stable (recommended)",
                    "Tested releases only. Safest, gets new features after they've been validated."),
                new Dialogs.ChoiceButton(ID_LATEST, "Latest",
                    "Includes pre-release builds. Newer features but may be rougher. Good for testers."),
                new Dialogs.ChoiceButton(ID_OFF, "Off (not recommended)",
                    "Never check for updates. You will miss bug fixes and new screen support."),
                new Dialogs.ChoiceButton(ID_KEEP, "Keep current setting",
                    $"Leave the channel as {currentLabel} and close this dialog."),
            },
            defaultChoiceId: ID_STABLE);

        return choice switch
        {
            ID_STABLE => UpdateChannel.Stable,
            ID_LATEST => UpdateChannel.Latest,
            ID_OFF => UpdateChannel.Off,
            _ => null,  // Keep current (or Esc)
        };
    }
}
