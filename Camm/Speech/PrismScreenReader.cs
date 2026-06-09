namespace Camm.Speech;

// IScreenReader over Prism (ethindp/prism) via the PrismNative P/Invoke
// layer. Mirrors RimWorld Access's TolkHelper Prism path:
//   prism_config_init -> prism_init -> prism_registry_acquire_best
//   -> prism_backend_initialize, then speak via prism_backend_output
//   (falling back to prism_backend_speak if output isn't implemented).
//
// Initialize() returns false (never throws) on any failure — missing
// prism.dll (DllNotFoundException), null context, no available backend,
// or an init error. ScreenReaderFactory uses that to fall back to Tolk.
public sealed class PrismScreenReader : IScreenReader
{
    private IntPtr _ctx;
    private IntPtr _backend;
    private bool _supportsOutput;
    private bool _supportsSpeak;
    private bool _supportsStop;
    private bool _supportsIsSpeaking;

    public string BackendName => "Prism";
    public string? DetectedReader { get; private set; }

    public bool IsSpeaking
    {
        get
        {
            if (_backend == IntPtr.Zero || !_supportsIsSpeaking) return false;
            try
            {
                if (PrismNative.prism_backend_is_speaking(_backend, out bool speaking) == PrismError.Ok)
                    return speaking;
            }
            catch { /* fall through */ }
            return false;
        }
    }

    public bool Initialize()
    {
        try
        {
            var cfg = PrismNative.prism_config_init();
            _ctx = PrismNative.prism_init(ref cfg);
            if (_ctx == IntPtr.Zero)
            {
                Logger.Warn("Prism: prism_init returned a null context.");
                return false;
            }

            _backend = PrismNative.prism_registry_acquire_best(_ctx);
            if (_backend == IntPtr.Zero)
            {
                Logger.Warn("Prism: no screen reader or TTS backend available.");
                Cleanup();
                return false;
            }

            var rc = PrismNative.prism_backend_initialize(_backend);
            if (rc != PrismError.Ok && rc != PrismError.AlreadyInitialized)
            {
                Logger.Warn($"Prism: backend initialize failed: {PrismNative.ErrorString(rc)}");
                Cleanup();
                return false;
            }

            var features = (PrismBackendFeature)PrismNative.prism_backend_get_features(_backend);
            _supportsOutput = features.HasFlag(PrismBackendFeature.SupportsOutput);
            _supportsSpeak = features.HasFlag(PrismBackendFeature.SupportsSpeak);
            _supportsStop = features.HasFlag(PrismBackendFeature.SupportsStop);
            _supportsIsSpeaking = features.HasFlag(PrismBackendFeature.SupportsIsSpeaking);
            DetectedReader = PrismNative.ReadUtf8(PrismNative.prism_backend_name(_backend));

            Logger.Info($"Prism backend initialized: '{DetectedReader ?? "unknown"}' " +
                        $"(output={_supportsOutput}, speak={_supportsSpeak}, stop={_supportsStop}).");

            // Useless without at least one speech entry point.
            if (!_supportsOutput && !_supportsSpeak)
            {
                Logger.Warn("Prism: backend supports neither output nor speak; falling back.");
                Cleanup();
                return false;
            }
            return true;
        }
        catch (DllNotFoundException ex)
        {
            Logger.Warn($"Prism: prism.dll not found ({ex.Message}). Falling back to Tolk.");
            return false;
        }
        catch (Exception ex)
        {
            Logger.Exception("Prism: Initialize threw", ex);
            Cleanup();
            return false;
        }
    }

    public void Speak(string text, bool interrupt)
    {
        if (_backend == IntPtr.Zero || string.IsNullOrEmpty(text)) return;
        try
        {
            if (_supportsOutput)
            {
                var rc = PrismNative.prism_backend_output(_backend, text, interrupt);
                if (rc == PrismError.NotImplemented && _supportsSpeak)
                    PrismNative.prism_backend_speak(_backend, text, interrupt);
            }
            else if (_supportsSpeak)
            {
                PrismNative.prism_backend_speak(_backend, text, interrupt);
            }
        }
        catch (Exception ex)
        {
            Logger.Exception("Prism: Speak threw", ex);
        }
    }

    public void Stop()
    {
        if (_backend == IntPtr.Zero || !_supportsStop) return;
        try { PrismNative.prism_backend_stop(_backend); } catch { /* best effort */ }
    }

    public void Dispose() => Cleanup();

    private void Cleanup()
    {
        try
        {
            if (_backend != IntPtr.Zero)
            {
                PrismNative.prism_backend_free(_backend);
                _backend = IntPtr.Zero;
            }
            if (_ctx != IntPtr.Zero)
            {
                PrismNative.prism_shutdown(_ctx);
                _ctx = IntPtr.Zero;
            }
        }
        catch { /* best effort */ }
    }
}
