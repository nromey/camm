namespace Camm.Wizard;

// Shared mutable state passed between wizard pages. Pages write their
// result fields in OnLeave; subsequent pages read in OnEnter. Plain
// mutable POCO — the wizard form is the only owner so there's no
// risk of stale-state bugs from external mutation.
public sealed class InstallContext
{
    public UpdateChannel SelectedChannel { get; set; } = UpdateChannel.Stable;
    public bool IsFirstInstall { get; set; } = true;

    // Default true so wizard-test / dev iteration doesn't fire UAC
    // and touch Program Files on every keystroke. The real install
    // flow constructs the context with IsDryRun=false.
    public bool IsDryRun { get; set; } = true;

    // Set by InstallingPage when the real install fails. Non-null
    // means the Done page should render the failure variant instead
    // of the success variant.
    public string? InstallError { get; set; }
}
