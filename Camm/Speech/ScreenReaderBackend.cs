namespace Camm.Speech;

// Which screen-reader output library CAMM routes speech through.
//
// Adopters declare a default via CammModManifest.ScreenReaderBackend and
// bundle the matching native DLLs (embed tolk/* and/or prism/* resources
// in their launcher exe — see CivViAccess.csproj). An adopter can bundle
// one or both; bundling both lets the runtime fall back Prism -> Tolk and
// (later) lets an end-user pick.
//
// Tolk  — DavyKager.Tolk binding over Tolk.dll. The cross-mod convention
//         (Civ V Access, RimWorld Access). Default.
// Prism — ethindp/prism via our P/Invoke layer. Newer, cross-platform,
//         broader screen-reader matrix. Opt-in.
public enum ScreenReaderBackend
{
    Tolk,
    Prism,
}
