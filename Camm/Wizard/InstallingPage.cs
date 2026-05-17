using System.Diagnostics;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace Camm.Wizard;

[SupportedOSPlatform("windows")]
public sealed class InstallingPage : UserControl, IWizardPage
{
    private const string HeadingText = "Installing...";
    private const string StatusInitial = "Preparing to install...";

    private readonly Label _status;
    private readonly ProgressBar _progress;

    public string Title => "Installing";
    public bool CanGoNext => false;
    public Control? InitialFocusControl => null;

    // All buttons disabled during install. Cancel-during-install is not
    // safe once elevation has been granted and files are mid-copy;
    // revisit if/when CAMM grows a staged-rollback path.
    public bool ButtonsEnabled => false;

    public string AnnouncementText =>
        $"Installing {CammHost.Manifest.DisplayName}. Please wait.";

    public event EventHandler? CanGoNextChanged { add { } remove { } }
    public event EventHandler? AdvanceRequested;

    public InstallingPage()
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

        _status = new Label
        {
            Text = StatusInitial,
            AutoSize = false,
            Location = new System.Drawing.Point(24, 80),
            Size = new System.Drawing.Size(500, 30),
            AccessibleName = StatusInitial,
            AccessibleRole = AccessibleRole.StaticText,
        };

        // Marquee mode: the install is short (~2 seconds for file copy
        // + registry writes) and has no useful percent-complete to
        // report. The marquee gives visual "something is happening"
        // feedback without lying about progress.
        _progress = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 30,
            Location = new System.Drawing.Point(24, 120),
            Size = new System.Drawing.Size(500, 20),
        };

        Controls.Add(heading);
        Controls.Add(_status);
        Controls.Add(_progress);
    }

    public void OnEnter(InstallContext context)
    {
        if (context.IsDryRun)
        {
            SimulateInstall();
        }
        else
        {
            RunRealInstall(context);
        }
    }

    private void SimulateInstall()
    {
        // Dry-run path (wizard-test). 2s delay then advance — lets
        // wizard-UI iteration happen without UAC every time.
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000).ConfigureAwait(false);
            BeginInvoke(() =>
            {
                _status.Text = "Install complete (dry run).";
                _status.AccessibleName = _status.Text;
                AdvanceRequested?.Invoke(this, EventArgs.Empty);
            });
        });
    }

    private void RunRealInstall(InstallContext context)
    {
        // Persist the chosen channel before elevation. launcher.ini
        // lives in %LocalAppData% (user-writable, no admin needed)
        // and the elevated install process doesn't need to know the
        // channel — it's read later at launcher startup time.
        try
        {
            var settings = LauncherSettings.LoadOrCreate(LauncherSettings.DefaultPath);
            settings.UpdateChannel = context.SelectedChannel;
            settings.Save(LauncherSettings.DefaultPath);
            Logger.Info($"InstallingPage: saved UpdateChannel={context.SelectedChannel}");
        }
        catch (Exception ex)
        {
            Logger.Exception("InstallingPage: failed to save UpdateChannel", ex);
            // Non-fatal — install proceeds with the prior channel
            // setting; user can change it from Apps & Features Modify.
        }

        _ = Task.Run(() =>
        {
            string? error = null;
            try
            {
                if (IfeoInstaller.IsRunningElevated())
                {
                    // Already elevated (rare for the wizard, but it
                    // happens if the user launched the installer via
                    // an elevated cmd). Call ApplyInstall directly —
                    // no need to spawn another process.
                    Installer.ApplyInstall(
                        msg => { Logger.Info($"InstallingPage(elevated): {msg}"); UpdateStatus(msg); },
                        msg => Logger.Info($"InstallingPage(elevated speak): {msg}"));
                }
                else
                {
                    var exe = Environment.ProcessPath
                        ?? throw new InvalidOperationException(
                            "Cannot determine current launcher exe path.");
                    var psi = new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = "--install-from-wizard",
                        UseShellExecute = true,  // required for runas
                        Verb = "runas",
                    };
                    Logger.Info($"InstallingPage: spawning elevated child {exe} --install-from-wizard");
                    using var proc = Process.Start(psi)
                        ?? throw new InvalidOperationException(
                            "Process.Start returned null for elevated install.");
                    proc.WaitForExit();
                    Logger.Info($"InstallingPage: child exited with code {proc.ExitCode}");
                    if (proc.ExitCode != 0)
                    {
                        error = $"Install process exited with code {proc.ExitCode}. " +
                                "Check launcher.log for details.";
                    }
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // User declined the UAC prompt.
                error = "Install cancelled — administrator permission was not granted.";
            }
            catch (Exception ex)
            {
                Logger.Exception("InstallingPage: install threw", ex);
                error = ex.Message;
            }

            BeginInvoke(() =>
            {
                context.InstallError = error;
                _status.Text = error is null ? "Install complete." : "Install failed.";
                _status.AccessibleName = _status.Text;
                AdvanceRequested?.Invoke(this, EventArgs.Empty);
            });
        });
    }

    private void UpdateStatus(string message)
    {
        // Called from background thread when ApplyInstall is invoked
        // in-process (elevated path). Marshal to UI thread.
        if (IsHandleCreated && !IsDisposed)
        {
            BeginInvoke(() =>
            {
                _status.Text = message;
                _status.AccessibleName = message;
            });
        }
    }

    public void OnLeave(InstallContext context) { }
}
