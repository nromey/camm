using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Camm.Speech;

// Hand-rolled P/Invoke layer over Prism's flat C ABI (ethindp/prism,
// include/prism.h). LibraryImport source-generated stubs are Native-AOT
// clean (the consuming launcher publishes with PublishAot=true). The
// native prism.dll is discovered the same way as Tolk.dll — extracted
// next to the exe (or to a temp dir + SetDllDirectory) by TolkBootstrap.
//
// Calling convention is __cdecl (PRISM_CALL); on x64 Windows there is a
// single native convention so the attribute is effectively cosmetic, but
// it is declared for correctness and the future Mac/Linux targets.
//
// const char* IN params marshal as UTF-8 (StringMarshalling.Utf8).
// const char* RETURN values (backend name, error string) are owned by
// Prism and must NOT be freed, so they come back as IntPtr and are read
// with Marshal.PtrToStringUTF8.
//
// C `bool` is one byte (<stdbool.h>); marshal every bool as U1, otherwise
// the default 4-byte Win32 BOOL corrupts the call.

internal enum PrismError
{
    Ok = 0,
    NotInitialized,
    InvalidParam,
    NotImplemented,
    NoVoices,
    VoiceNotFound,
    SpeakFailure,
    MemoryFailure,
    RangeOutOfBounds,
    Internal,
    NotSpeaking,
    NotPaused,
    AlreadyPaused,
    InvalidUtf8,
    InvalidOperation,
    AlreadyInitialized,
    BackendNotAvailable,
    Unknown,
    InvalidAudioFormat,
    InternalBackendLimitExceeded,
    BackendEnteredUndefinedState,
    Count,
}

[Flags]
internal enum PrismBackendFeature : ulong
{
    IsSupportedAtRuntime = 1UL << 0,
    SupportsSpeak = 1UL << 2,
    SupportsSpeakToMemory = 1UL << 3,
    SupportsBraille = 1UL << 4,
    SupportsOutput = 1UL << 5,
    SupportsIsSpeaking = 1UL << 6,
    SupportsStop = 1UL << 7,
    SupportsPause = 1UL << 8,
    SupportsResume = 1UL << 9,
}

// PrismConfig is a single version byte (PRISM_CONFIG_VERSION). Returned
// by value from prism_config_init and passed by pointer to prism_init.
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal struct PrismConfig
{
    public byte Version;
}

internal static partial class PrismNative
{
    private const string Lib = "prism";

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PrismConfig prism_config_init();

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr prism_init(ref PrismConfig cfg);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void prism_shutdown(IntPtr ctx);

    // Auto-select the best available backend (screen reader preferred over
    // raw TTS). Returns an owned backend handle to free with
    // prism_backend_free.
    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr prism_registry_acquire_best(IntPtr ctx);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void prism_backend_free(IntPtr backend);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr prism_backend_name(IntPtr backend);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial ulong prism_backend_get_features(IntPtr backend);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PrismError prism_backend_initialize(IntPtr backend);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PrismError prism_backend_output(
        IntPtr backend, string text, [MarshalAs(UnmanagedType.U1)] bool interrupt);

    [LibraryImport(Lib, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PrismError prism_backend_speak(
        IntPtr backend, string text, [MarshalAs(UnmanagedType.U1)] bool interrupt);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PrismError prism_backend_stop(IntPtr backend);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial PrismError prism_backend_is_speaking(
        IntPtr backend, [MarshalAs(UnmanagedType.U1)] out bool outSpeaking);

    [LibraryImport(Lib)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr prism_error_string(PrismError error);

    // Read a null-terminated UTF-8 string Prism owns. Does NOT free it.
    internal static string? ReadUtf8(IntPtr p) =>
        p == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(p);

    internal static string ErrorString(PrismError error)
    {
        try { return ReadUtf8(prism_error_string(error)) ?? error.ToString(); }
        catch { return error.ToString(); }
    }
}
