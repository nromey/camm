namespace Camm;

// Static entry point for CAMM-built launchers. The consumer calls
// CammHost.Initialize(manifest) once at startup, before any other CAMM
// module is used. All CAMM modules then read per-mod state via
// CammHost.Manifest.
//
// Future work (Steps 6+ of CAMM_EXTRACTION_PLAN.md): CammHost will
// also host RunAsync(args, manifest) — the unified routing entry
// point that does apply-pending-self-update, Tolk bootstrap, mode
// branching (--install / --uninstall / --config / transparent-
// invocation / bare-exe) — at which point the consumer's Program.cs
// is just a few lines: build the manifest, hand it to RunAsync, done.
public static class CammHost
{
    private static CammModManifest? _manifest;

    // The active manifest. Throws if accessed before Initialize. This
    // is intentional: an uninitialized CAMM is a programming error, not
    // a runtime condition to recover from. Fail fast rather than write
    // files under \%LocalAppData%\Camm\ and silently make the launcher
    // misbehave.
    public static CammModManifest Manifest =>
        _manifest ?? throw new InvalidOperationException(
            "CammHost not initialized. Call CammHost.Initialize(manifest) " +
            "before invoking any other CAMM module.");

    // Call once at process startup. Throws if called twice — re-
    // initializing mid-process would be a sign of a logic error.
    public static void Initialize(CammModManifest manifest)
    {
        if (_manifest is not null)
        {
            throw new InvalidOperationException(
                "CammHost.Initialize has already been called this process. " +
                "Manifest can only be set once.");
        }
        _manifest = manifest;
    }
}
