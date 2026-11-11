using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using voltrex_quest.Models;

namespace voltrex_quest.Services;

/// <summary>
/// Checks GitHub Releases (ziard47/voltrex-quest), downloads installer assets, and launches setup updates.
/// </summary>
public sealed class UpdateService
{
    public const string RepositoryOwner = "ziard47";
    public const string RepositoryName = "voltrex-quest";
    public const string LatestReleaseApiUrl = $"https://api.github.com/repos/{RepositoryOwner}/{RepositoryName}/releases/latest";
    public const string ReleasesPageUrl = $"https://github.com/{RepositoryOwner}/{RepositoryName}/releases";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;

    public UpdateService(HttpClient? httpClient = null)
    {
        CurrentVersion = ResolveInstalledVersion();

        if (httpClient is not null)
        {
            _httpClient = httpClient;
        }
        else
        {
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        ConfigureDefaultHeaders(_httpClient);
    }

    public Version CurrentVersion { get; }

    public string CurrentVersionNumber => $"{CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}";

    public string CurrentVersionDisplay => $"Version {CurrentVersionNumber}";

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                HasPublishedRelease = false,
                IsUpdateAvailable = false,
                ReleaseUrl = ReleasesPageUrl
            };
        }

        if (response.StatusCode is HttpStatusCode.Forbidden or (HttpStatusCode)429)
        {
            throw new InvalidOperationException(
                "GitHub API rate limit reached while checking for updates. Please try again later or check the GitHub Releases page.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GitHub Releases returned status {(int)response.StatusCode} ({response.ReasonPhrase}).");
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var release = await JsonSerializer.DeserializeAsync<GitHubReleaseDto>(stream, JsonOptions, cancellationToken)
            .ConfigureAwait(false);

        if (release is null || release.Draft || string.IsNullOrWhiteSpace(release.TagName))
        {
            return new UpdateCheckResult
            {
                CurrentVersion = CurrentVersion,
                HasPublishedRelease = false,
                IsUpdateAvailable = false,
                ReleaseUrl = ReleasesPageUrl
            };
        }

        var latestVersion = ParseReleaseVersion(release.TagName);
        var isNewer = latestVersion is not null && CompareVersions(latestVersion, CurrentVersion) > 0;
        var installerAsset = SelectInstallerAsset(release.Assets);
        var releaseName = string.IsNullOrWhiteSpace(release.Name) ? release.TagName : release.Name.Trim();

        return new UpdateCheckResult
        {
            CurrentVersion = CurrentVersion,
            LatestVersion = latestVersion,
            HasPublishedRelease = true,
            IsUpdateAvailable = isNewer,
            TagName = release.TagName.Trim(),
            ReleaseName = releaseName,
            ReleaseNotes = string.IsNullOrWhiteSpace(release.Body)
                ? "No release notes were provided for this release."
                : release.Body.Trim(),
            ReleaseUrl = string.IsNullOrWhiteSpace(release.HtmlUrl) ? ReleasesPageUrl : release.HtmlUrl.Trim(),
            PublishedAt = release.PublishedAt,
            InstallerAsset = installerAsset
        };
    }

    public async Task<string> DownloadInstallerAsync(
        UpdateAssetInfo asset,
        IProgress<UpdateDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);

        if (string.IsNullOrWhiteSpace(asset.DownloadUrl))
        {
            throw new InvalidOperationException("The selected release does not include a valid installer download URL.");
        }

        var downloadsDir = Path.Combine(Path.GetTempPath(), "VoltrexQuestUpdates");
        Directory.CreateDirectory(downloadsDir);

        var safeFileName = Path.GetFileName(asset.Name);
        if (string.IsNullOrWhiteSpace(safeFileName))
        {
            safeFileName = "Voltrex-Quest-Setup.exe";
        }

        var destinationPath = Path.Combine(downloadsDir, safeFileName);
        var partPath = $"{destinationPath}.part";

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.DownloadUrl);
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? asset.SizeBytes;
            long totalRead = 0;

            await using (var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var fileStream = new FileStream(partPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                var buffer = new byte[81920];
                int bytesRead;

                while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken).ConfigureAwait(false);
                    totalRead += bytesRead;
                    progress?.Report(new UpdateDownloadProgress(totalRead, totalBytes));
                }
            }

            if (totalRead == 0)
            {
                throw new InvalidOperationException("Downloaded installer file is empty.");
            }

            if (File.Exists(destinationPath))
            {
                File.Delete(destinationPath);
            }

            File.Move(partPath, destinationPath);
            progress?.Report(new UpdateDownloadProgress(totalRead, totalBytes > 0 ? totalBytes : totalRead));
            return destinationPath;
        }
        catch
        {
            TryDeleteFile(partPath);
            throw;
        }
    }

    public bool LaunchInstaller(string installerPath, out string? errorMessage)
    {
        if (string.IsNullOrWhiteSpace(installerPath) || !File.Exists(installerPath))
        {
            errorMessage = "Downloaded installer file could not be found.";
            return false;
        }

        try
        {
            var isExe = installerPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
            var startInfo = new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = isExe ? "/CLOSEAPPLICATIONS" : string.Empty,
                UseShellExecute = true
            };

            Process.Start(startInfo);
            errorMessage = null;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Failed to start the installer: {ex.Message}";
            return false;
        }
    }

    public static Version? ParseReleaseVersion(string? tagName)
    {
        if (string.IsNullOrWhiteSpace(tagName))
        {
            return null;
        }

        var normalized = tagName.Trim().TrimStart('v', 'V');
        var separatorIndex = normalized.IndexOfAny(['-', '+']);
        if (separatorIndex >= 0)
        {
            normalized = normalized[..separatorIndex];
        }

        if (!Version.TryParse(normalized, out var parsed))
        {
            return null;
        }

        return new Version(
            Math.Max(0, parsed.Major),
            Math.Max(0, parsed.Minor),
            Math.Max(0, parsed.Build));
    }

    public static int CompareVersions(Version left, Version right)
    {
        var l = new Version(Math.Max(0, left.Major), Math.Max(0, left.Minor), Math.Max(0, left.Build));
        var r = new Version(Math.Max(0, right.Major), Math.Max(0, right.Minor), Math.Max(0, right.Build));
        return l.CompareTo(r);
    }

    public static UpdateAssetInfo? SelectInstallerAsset(IReadOnlyList<GitHubReleaseAssetDto>? assets)
    {
        if (assets is null || assets.Count == 0)
        {
            return null;
        }

        var validAssets = assets
            .Where(a => !string.IsNullOrWhiteSpace(a.Name) && !string.IsNullOrWhiteSpace(a.BrowserDownloadUrl))
            .ToList();

        var chosen =
            validAssets.FirstOrDefault(a =>
                a.Name!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                (a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                 a.Name.Contains("installer", StringComparison.OrdinalIgnoreCase)) &&
                a.Name.Contains("x64", StringComparison.OrdinalIgnoreCase))
            ?? validAssets.FirstOrDefault(a =>
                a.Name!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                (a.Name.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
                 a.Name.Contains("installer", StringComparison.OrdinalIgnoreCase)))
            ?? validAssets.FirstOrDefault(a =>
                a.Name!.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            ?? validAssets.FirstOrDefault(a =>
                a.Name!.EndsWith(".msix", StringComparison.OrdinalIgnoreCase) ||
                a.Name!.EndsWith(".msixbundle", StringComparison.OrdinalIgnoreCase));

        if (chosen is null)
        {
            return null;
        }

        return new UpdateAssetInfo
        {
            Name = chosen.Name!,
            DownloadUrl = chosen.BrowserDownloadUrl!,
            SizeBytes = chosen.Size,
            ContentType = chosen.ContentType ?? string.Empty
        };
    }

    private static Version ResolveInstalledVersion()
    {
        var assembly = typeof(UpdateService).Assembly;
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var parsedInformational = ParseReleaseVersion(informational);
        if (parsedInformational is not null)
        {
            return parsedInformational;
        }

        var asmVersion = assembly.GetName().Version;
        if (asmVersion is not null)
        {
            return new Version(
                Math.Max(0, asmVersion.Major),
                Math.Max(0, asmVersion.Minor),
                Math.Max(0, asmVersion.Build));
        }

        return new Version(1, 0, 0);
    }

    private void ConfigureDefaultHeaders(HttpClient client)
    {
        if (!client.DefaultRequestHeaders.UserAgent.Any())
        {
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Voltrex-Quest", CurrentVersionNumber));
        }

        if (!client.DefaultRequestHeaders.Accept.Any())
        {
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        }

        if (!client.DefaultRequestHeaders.Contains("X-GitHub-Api-Version"))
        {
            client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of partial download.
        }
    }
}

