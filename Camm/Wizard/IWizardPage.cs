using System.Windows.Forms;

namespace Camm.Wizard;

// Contract every wizard page UserControl implements. The host form
// drives transitions via OnEnter/OnLeave; pages signal Next-eligibility
// via CanGoNext + CanGoNextChanged so the host doesn't poll.
//
// AnnouncementText is what the host Tolk-speaks when this page becomes
// active. Keeping speech orchestration in the host (with a small delay
// so NVDA's focus event fires first) means pages don't each re-invent
// the timing dance.
//
// InitialFocusControl is the control that should receive focus when the
// page becomes active — combobox on Channel, Install button on Ready,
// etc. Return null to fall back to the host's default (Next button when
// enabled, Cancel otherwise).
//
// Button bar shape is per-page: NextButtonText relabels (Install,
// Finish); ShowBackButton / ShowCancelButton hide buttons; ButtonsEnabled
// disables them all (used by Installing).
//
// AdvanceRequested lets a page that runs async work (Installing)
// tell the host "I'm done — advance to the next page" without the page
// having to know about its host.
public interface IWizardPage
{
    string Title { get; }
    string AnnouncementText { get; }
    Control? InitialFocusControl { get; }
    string NextButtonText => "&Next";
    bool ShowBackButton => true;
    bool ShowCancelButton => true;
    bool ButtonsEnabled => true;
    bool CanGoNext { get; }
    event EventHandler? CanGoNextChanged;
    event EventHandler? AdvanceRequested;
    void OnEnter(InstallContext context);
    void OnLeave(InstallContext context);
}
