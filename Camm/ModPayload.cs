using System.Text.Json.Serialization;

namespace Camm;

// A deployable artifact group. A mod with a single mod-folder ships
// with one payload; a mod with multiple destinations (Civ V Access:
// DLC package + proxy DLL + engine-fork DLL) ships multiple payloads,
// each with its own destination.
//
// `Name` doubles as the embed-resource prefix — the consuming csproj's
// <EmbeddedResource> glob sets <LogicalName> rooted at `<Name>/`. For
// example, a payload `Name="dlc"` embeds files as `dlc/...` resources
// and CAMM's ModFiles extractor picks them out by that prefix.
//
// `FolderName` is the dev-mode source-discovery hint. CAMM walks up
// from the launcher exe's directory looking for a directory of that
// name; if found, CAMM treats it as the mod source and copies its
// contents into `DefaultDestination()` at startup (skipping the
// embedded-resource extraction). Used during the dev edit-build-test
// loop.
//
// `SentinelFileName` is optional (empty string disables the check)
// — a file CAMM checks for inside the FolderName-matching dir to
// confirm it's genuinely the mod source and not a name collision.
//
// `DefaultDestination` always returns a directory path (not a file).
// Single-file payloads (a single DLL to drop somewhere) work fine —
// the payload tree contains just that one file, and the destination
// is the parent directory the file lands in.
public sealed record ModPayload(
    string Name,
    string FolderName,
    string SentinelFileName,
    Func<string> DefaultDestination);

// Manifest of files written by a payload's install. Persisted to
// %LocalAppData%\<LocalAppDataFolderName>\installed-<payload-name>.json
// so uninstall + update can delete the specific files this payload
// owned (vs nuking the whole destination directory, which could
// contain non-CAMM content like other mods' files or the game's own
// data).
public sealed class PayloadInstallManifest
{
    [JsonPropertyName("payloadName")]
    public string PayloadName { get; set; } = "";

    [JsonPropertyName("destinationRoot")]
    public string DestinationRoot { get; set; } = "";

    [JsonPropertyName("files")]
    public List<string> Files { get; set; } = new();

    [JsonPropertyName("createdAt")]
    public string CreatedAt { get; set; } = "";

    [JsonPropertyName("cammVersion")]
    public string CammVersion { get; set; } = "";
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(PayloadInstallManifest))]
internal partial class PayloadInstallManifestJsonContext : JsonSerializerContext { }
