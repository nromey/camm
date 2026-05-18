using System.IO.Compression;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.Versioning;
using System.Text.Json;
using Camm.Localization;

namespace Camm;

// Install-time fetcher for external-mod dependencies declared on
// CammModManifest.Dependencies. For each declared dependency:
//
//   1. Sentinel check — if InstallPath/SentinelFileName already
//      exists, log and return (idempotent).
//   2. Fetch latest release metadata from
//      api.github.com/repos/<owner>/<repo>/releases/latest.
//   3. Find the asset matching AssetNamePattern (with `{0}`
//      replaced by the bare tag, leading `v` stripped).
//   4. Prompt the user — Install / Skip / Cancel. Skip continues
//      the install without the dep (user takes responsibility);
//      Cancel throws OperationCanceledException to abort.
//   5. Download the asset to a temp file.
//   6. Extract: zip → ZipArchive with optional ZipRootStripPrefix;
//      bare DLL → file copy. Other formats fail with a clear error.
//   7. Persist DependencyInstallManifest so future runs can
//      diagnose what version is installed.
//
// All failure modes (network errors, asset-not-found, extraction
// errors) surface a retry/skip/cancel dialog. No silent fallbacks.
[SupportedOSPlatform("windows")]
public static class DependencyInstaller
{
    public static async Task EnsureAsync(
        ModDependency dep,
        Action<string> log,
        Action<string> speak,
        CancellationToken ct = default)
    {
        var installPath = dep.InstallPath();
        var sentinelFull = Path.Combine(installPath, dep.SentinelFileName);

        if (File.Exists(sentinelFull))
        {
            log($"Dependency '{dep.Name}' already present at {installPath}; skipping.");
            return;
        }

        log($"Dependency '{dep.Name}' not found at {installPath}; preparing prompt.");

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(CammHost.Manifest.UserAgent);
        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        bool consentGiven = false;

        while (true)
        {
            try
            {
                // Fetch release metadata so the prompt can show
                // accurate download size.
                var url = $"https://api.github.com/repos/{dep.GitHubReleasesOwner}/{dep.GitHubReleasesRepo}/releases/latest";
                var release = await http.GetFromJsonAsync(
                    url,
                    ReleasesJsonContext.Default.ReleaseDto,
                    ct).ConfigureAwait(false);

                if (release is null || string.IsNullOrEmpty(release.TagName))
                {
                    throw new InvalidOperationException(
                        $"GitHub returned no latest release for {dep.GitHubReleasesOwner}/{dep.GitHubReleasesRepo}.");
                }

                var bareVersion = release.TagName.TrimStart('v', 'V');
                var assetName = string.Format(dep.AssetNamePattern, bareVersion);
                var asset = release.Assets?
                    .FirstOrDefault(a => string.Equals(a.Name, assetName, StringComparison.OrdinalIgnoreCase));

                if (asset is null)
                {
                    var available = release.Assets is null
                        ? "(none)"
                        : string.Join(", ", release.Assets.Select(a => a.Name));
                    throw new InvalidOperationException(
                        $"No asset named '{assetName}' on release {release.TagName}. " +
                        $"Available: {available}.");
                }

                // Only prompt the first time through the loop. If
                // we're retrying after a download failure, the user
                // already said yes — don't re-prompt for consent.
                if (!consentGiven)
                {
                    var promptResult = PromptForDependency(dep, asset.Size);
                    switch (promptResult)
                    {
                        case PromptOutcome.Install:
                            consentGiven = true;
                            break;
                        case PromptOutcome.Skip:
                            log($"User skipped dependency '{dep.Name}'. Install continues without it.");
                            speak(FormatDependencyKey("Dependency.Skipped", dep, asset));
                            return;
                        case PromptOutcome.Cancel:
                            log($"User cancelled install at dependency '{dep.Name}' prompt.");
                            throw new OperationCanceledException(
                                $"Install cancelled by user at dependency '{dep.Name}'.");
                    }
                }

                // Download + extract.
                speak(FormatDependencyKey("Dependency.Downloading", dep, asset));
                log($"Downloading {asset.Name} ({asset.Size:N0} bytes) from {asset.DownloadUrl}.");

                var tempPath = Path.Combine(
                    Path.GetTempPath(),
                    $"camm-dep-{dep.Name}-{Guid.NewGuid():N}");
                try
                {
                    await DownloadAsync(http, asset.DownloadUrl, tempPath, ct).ConfigureAwait(false);

                    speak(FormatDependencyKey("Dependency.Installing", dep, asset));
                    log($"Extracting to {installPath}.");
                    Directory.CreateDirectory(installPath);

                    var writtenFiles = ExtractAsset(dep, asset, tempPath, installPath, log);
                    WriteDependencyManifest(dep, release.TagName, installPath, writtenFiles, asset.DownloadUrl);

                    if (!File.Exists(sentinelFull))
                    {
                        log($"WARNING: Sentinel {dep.SentinelFileName} not found at {installPath} " +
                            $"after extraction. The dependency may have a different layout than expected; " +
                            $"check ZipRootStripPrefix.");
                    }

                    speak(FormatDependencyKey("Dependency.Installed", dep, asset));
                    log($"Dependency '{dep.Name}' installed.");
                    return;
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { /* best effort */ }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log($"Dependency '{dep.Name}' failed: {ex.Message}");
                var outcome = HandleFailureDialog(dep, ex.Message);
                switch (outcome)
                {
                    case FailureOutcome.Retry:
                        log($"Retrying dependency '{dep.Name}'.");
                        continue;  // loop back, fetch metadata + download again
                    case FailureOutcome.Skip:
                        log($"User skipped dependency '{dep.Name}' after failure.");
                        speak($"{dep.DisplayName} was skipped.");
                        return;
                    case FailureOutcome.Cancel:
                        log($"User cancelled install after dependency '{dep.Name}' failure.");
                        throw new OperationCanceledException(
                            $"Install cancelled by user after dependency '{dep.Name}' failure.");
                }
            }
        }
    }

    private enum PromptOutcome { Install, Skip, Cancel }

    private static PromptOutcome PromptForDependency(ModDependency dep, long sizeBytes)
    {
        const int ID_INSTALL = 101;
        const int ID_SKIP = 102;
        const int ID_CANCEL = 103;

        var sizeLabel = FormatSize(sizeBytes);
        // Stash the per-prompt substitutions so Strings.Get can pull
        // them out of the manifest-scoped substitution table.
        DependencyTokens.CurrentDependencyDisplayName = dep.DisplayName;
        DependencyTokens.CurrentDependencySize = sizeLabel;

        try
        {
            var choice = Dialogs.ShowChoice(
                title: Strings.Get("Dependency.Prompt.Title"),
                mainInstruction: Strings.Get("Dependency.Prompt.Instruction"),
                content: Strings.Get("Dependency.Prompt.Content"),
                choices: new[]
                {
                    new Dialogs.ChoiceButton(ID_INSTALL,
                        Strings.Get("Dependency.Prompt.InstallButton.Heading"),
                        Strings.Get("Dependency.Prompt.InstallButton.Note")),
                    new Dialogs.ChoiceButton(ID_SKIP,
                        Strings.Get("Dependency.Prompt.SkipButton.Heading"),
                        Strings.Get("Dependency.Prompt.SkipButton.Note")),
                    new Dialogs.ChoiceButton(ID_CANCEL,
                        Strings.Get("Dependency.Prompt.CancelButton.Heading"),
                        Strings.Get("Dependency.Prompt.CancelButton.Note")),
                },
                defaultChoiceId: ID_INSTALL);

            return choice switch
            {
                ID_INSTALL => PromptOutcome.Install,
                ID_SKIP => PromptOutcome.Skip,
                _ => PromptOutcome.Cancel,
            };
        }
        finally
        {
            DependencyTokens.CurrentDependencyDisplayName = null;
            DependencyTokens.CurrentDependencySize = null;
        }
    }

    private static string FormatDependencyKey(string key, ModDependency dep, AssetDto asset)
    {
        DependencyTokens.CurrentDependencyDisplayName = dep.DisplayName;
        DependencyTokens.CurrentDependencySize = FormatSize(asset.Size);
        try { return Strings.Get(key); }
        finally
        {
            DependencyTokens.CurrentDependencyDisplayName = null;
            DependencyTokens.CurrentDependencySize = null;
        }
    }

    private enum FailureOutcome { Retry, Skip, Cancel }

    private static FailureOutcome HandleFailureDialog(ModDependency dep, string message)
    {
        const int ID_RETRY = 201;
        const int ID_SKIP = 202;
        const int ID_CANCEL = 203;

        DependencyTokens.CurrentDependencyDisplayName = dep.DisplayName;
        DependencyTokens.CurrentDependencyErrorMessage = message;
        try
        {
            var choice = Dialogs.ShowChoice(
                title: Strings.Get("Dependency.DownloadFailed.Title"),
                mainInstruction: Strings.Get("Dependency.DownloadFailed.Title"),
                content: Strings.Get("Dependency.DownloadFailed.Content"),
                choices: new[]
                {
                    new Dialogs.ChoiceButton(ID_RETRY,
                        Strings.Get("Dependency.DownloadFailed.RetryButton.Heading"),
                        Strings.Get("Dependency.DownloadFailed.RetryButton.Note")),
                    new Dialogs.ChoiceButton(ID_SKIP,
                        Strings.Get("Dependency.Prompt.SkipButton.Heading"),
                        Strings.Get("Dependency.Prompt.SkipButton.Note")),
                    new Dialogs.ChoiceButton(ID_CANCEL,
                        Strings.Get("Dependency.Prompt.CancelButton.Heading"),
                        Strings.Get("Dependency.Prompt.CancelButton.Note")),
                },
                defaultChoiceId: ID_RETRY,
                warningIcon: true);

            return choice switch
            {
                ID_RETRY => FailureOutcome.Retry,
                ID_SKIP => FailureOutcome.Skip,
                _ => FailureOutcome.Cancel,
            };
        }
        finally
        {
            DependencyTokens.CurrentDependencyDisplayName = null;
            DependencyTokens.CurrentDependencyErrorMessage = null;
        }
    }

    private static async Task DownloadAsync(HttpClient http, string url, string destPath, CancellationToken ct)
    {
        using var response = await http.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var file = File.Create(destPath);
        await stream.CopyToAsync(file, ct).ConfigureAwait(false);
    }

    private static List<string> ExtractAsset(
        ModDependency dep,
        AssetDto asset,
        string tempPath,
        string installPath,
        Action<string> log)
    {
        var written = new List<string>();
        var name = asset.Name.ToLowerInvariant();

        if (name.EndsWith(".dll"))
        {
            // Bare-DLL dependency. Copy as-is.
            var destFile = Path.Combine(installPath, asset.Name);
            File.Copy(tempPath, destFile, overwrite: true);
            written.Add(Path.GetFullPath(destFile));
            return written;
        }

        if (!name.EndsWith(".zip"))
        {
            throw new NotSupportedException(
                $"Dependency '{dep.Name}' asset '{asset.Name}' has an unsupported format. " +
                $"v0.4.0 supports .zip and bare .dll only.");
        }

        // Zip extraction with optional ZipRootStripPrefix.
        using var archive = ZipFile.OpenRead(tempPath);
        string? stripPrefix = ResolveStripPrefix(dep.ZipRootStripPrefix, archive);
        if (stripPrefix is not null)
        {
            log($"Stripping top-level prefix '{stripPrefix}' from extraction.");
        }

        foreach (var entry in archive.Entries)
        {
            // Skip directory entries (length 0, name ends with /).
            if (string.IsNullOrEmpty(entry.Name)) continue;

            var entryPath = entry.FullName.Replace('\\', '/');
            if (stripPrefix is not null)
            {
                if (entryPath.StartsWith(stripPrefix + "/", StringComparison.Ordinal))
                {
                    entryPath = entryPath.Substring(stripPrefix.Length + 1);
                }
                else if (entryPath == stripPrefix)
                {
                    continue;  // the prefix-dir entry itself
                }
            }

            if (string.IsNullOrEmpty(entryPath)) continue;

            var destPath = Path.Combine(
                installPath,
                entryPath.Replace('/', Path.DirectorySeparatorChar));

            var destDir = Path.GetDirectoryName(destPath);
            if (!string.IsNullOrEmpty(destDir)) Directory.CreateDirectory(destDir);

            entry.ExtractToFile(destPath, overwrite: true);
            written.Add(Path.GetFullPath(destPath));
        }

        return written;
    }

    private static string? ResolveStripPrefix(string? configured, ZipArchive archive)
    {
        if (configured is null) return null;
        if (configured != "*") return configured;

        // "*" → infer from the first non-directory entry's leading dir.
        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            var path = entry.FullName.Replace('\\', '/');
            var slashIdx = path.IndexOf('/');
            if (slashIdx <= 0) return null;  // no top-level dir to strip
            return path.Substring(0, slashIdx);
        }
        return null;
    }

    private static void WriteDependencyManifest(
        ModDependency dep,
        string installedVersion,
        string installPath,
        List<string> files,
        string sourceUrl)
    {
        var manifest = new DependencyInstallManifest
        {
            DependencyName = dep.Name,
            InstalledVersion = installedVersion,
            InstallPath = Path.GetFullPath(installPath),
            Files = files,
            SourceAssetUrl = sourceUrl,
            CreatedAt = DateTime.UtcNow.ToString("O"),
            CammVersion = SemVer.Current().ToString(),
        };

        try
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                CammHost.Manifest.LocalAppDataFolderName);
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"dep-{SafeFileName(dep.Name)}.json");
            using var fs = File.Create(path);
            JsonSerializer.Serialize(
                fs, manifest,
                DependencyInstallManifestJsonContext.Default.DependencyInstallManifest);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Failed to write dependency manifest for '{dep.Name}': {ex.Message}");
        }
    }

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Create(name.Length, name, (span, src) =>
        {
            for (int i = 0; i < src.Length; i++)
            {
                span[i] = Array.IndexOf(invalid, src[i]) >= 0 ? '_' : src[i];
            }
        });
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "unknown size";
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024L * 1024L * 1024L) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }
}

// Per-prompt substitution state. AsyncLocal so concurrent installs
// (unlikely in CAMM's single-process flow, but cheap to do right)
// don't bleed dependency tokens between prompts. Read by
// Strings.Substitute when rendering Dependency.* keys.
internal static class DependencyTokens
{
    private static readonly AsyncLocal<string?> _displayName = new();
    private static readonly AsyncLocal<string?> _size = new();
    private static readonly AsyncLocal<string?> _error = new();

    public static string? CurrentDependencyDisplayName
    {
        get => _displayName.Value;
        set => _displayName.Value = value;
    }

    public static string? CurrentDependencySize
    {
        get => _size.Value;
        set => _size.Value = value;
    }

    public static string? CurrentDependencyErrorMessage
    {
        get => _error.Value;
        set => _error.Value = value;
    }
}

