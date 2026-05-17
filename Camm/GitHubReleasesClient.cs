using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Camm;

// Minimal GitHub Releases API client. Consumes the public list endpoint
// (no auth), picks the highest-versioned release that matches the user's
// channel, and returns its asset URLs.
//
// Owner / Repo / UserAgent come from CammConfig — set by the consumer
// at startup.
//
// AOT note: System.Text.Json source generators (JsonSerializerContext
// below) keep us reflection-free. The DTOs only model the fields we
// actually read — extras in the API response are ignored.
public sealed class GitHubReleasesClient
{
    private readonly HttpClient _http;

    public GitHubReleasesClient(HttpClient? http = null)
    {
        _http = http ?? new HttpClient();
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(CammConfig.UserAgent);
        _http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public async Task<ReleaseInfo?> GetLatestForChannelAsync(
        UpdateChannel channel,
        CancellationToken ct = default)
    {
        var owner = CammConfig.GitHubReleasesOwner;
        var repo = CammConfig.GitHubReleasesRepo;
        var url = $"https://api.github.com/repos/{owner}/{repo}/releases?per_page=30";
        var releases = await _http.GetFromJsonAsync(
            url,
            ReleasesJsonContext.Default.ReleaseDtoArray,
            ct).ConfigureAwait(false);

        if (releases is null || releases.Length == 0)
        {
            return null;
        }

        ReleaseInfo? best = null;
        foreach (var dto in releases)
        {
            if (dto.Draft) continue;
            if (channel == UpdateChannel.Stable && dto.Prerelease) continue;
            if (string.IsNullOrEmpty(dto.TagName)) continue;
            if (!SemVer.TryParse(dto.TagName, out var version)) continue;

            if (best is null || version.CompareTo(best.Version) > 0)
            {
                best = new ReleaseInfo(
                    version,
                    dto.TagName!,
                    dto.Prerelease,
                    dto.Assets ?? Array.Empty<AssetDto>());
            }
        }
        return best;
    }
}

public sealed record ReleaseInfo(
    SemVer Version,
    string TagName,
    bool IsPrerelease,
    AssetDto[] Assets)
{
    public AssetDto? FindAsset(string fileName) =>
        Assets.FirstOrDefault(a =>
            string.Equals(a.Name, fileName, StringComparison.OrdinalIgnoreCase));
}

public sealed class ReleaseDto
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("assets")] public AssetDto[]? Assets { get; set; }
}

public sealed class AssetDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("browser_download_url")] public string DownloadUrl { get; set; } = "";
    [JsonPropertyName("size")] public long Size { get; set; }
}

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ReleaseDto[]))]
[JsonSerializable(typeof(ReleaseDto))]
[JsonSerializable(typeof(AssetDto[]))]
[JsonSerializable(typeof(AssetDto))]
public partial class ReleasesJsonContext : JsonSerializerContext { }
