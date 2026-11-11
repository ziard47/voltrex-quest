using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace voltrex_quest.Models;

/// <summary>
/// GitHub Releases API payload returned by /repos/{owner}/{repo}/releases/latest.
/// </summary>
public sealed class GitHubReleaseDto
{
    [JsonPropertyName("tag_name")]
    public string? TagName { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("body")]
    public string? Body { get; set; }

    [JsonPropertyName("html_url")]
    public string? HtmlUrl { get; set; }

    [JsonPropertyName("published_at")]
    public DateTimeOffset? PublishedAt { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAssetDto>? Assets { get; set; }
}

/// <summary>
/// Individual downloadable asset attached to a GitHub Release.
/// </summary>
public sealed class GitHubReleaseAssetDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string? BrowserDownloadUrl { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("content_type")]
    public string? ContentType { get; set; }
}

/// <summary>
/// Resolved installer asset metadata for display and download.
/// </summary>
public sealed class UpdateAssetInfo
{
    public required string Name { get; init; }
    public required string DownloadUrl { get; init; }
    public long SizeBytes { get; init; }
    public string ContentType { get; init; } = string.Empty;

    public string FormattedSize => FormatBytes(SizeBytes);

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0)
        {
            return "Unknown size";
        }

        const double kb = 1024.0;
        const double mb = kb * 1024.0;
        const double gb = mb * 1024.0;

        if (bytes >= gb)
        {
            return $"{bytes / gb:0.00} GB";
        }

        if (bytes >= mb)
        {
            return $"{bytes / mb:0.0} MB";
        }

        return bytes >= kb ? $"{bytes / kb:0.0} KB" : $"{bytes} B";
    }
}

/// <summary>
/// Outcome of checking GitHub Releases for a newer Voltrex Quest build.
/// </summary>
public sealed class UpdateCheckResult
{
    public required Version CurrentVersion { get; init; }
    public Version? LatestVersion { get; init; }
    public bool HasPublishedRelease { get; init; }
    public bool IsUpdateAvailable { get; init; }
    public string TagName { get; init; } = string.Empty;
    public string ReleaseName { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public string ReleaseUrl { get; init; } = string.Empty;
    public DateTimeOffset? PublishedAt { get; init; }
    public UpdateAssetInfo? InstallerAsset { get; init; }

    public string CurrentVersionText => $"v{CurrentVersion.Major}.{CurrentVersion.Minor}.{Math.Max(0, CurrentVersion.Build)}";
    public string LatestVersionText => LatestVersion is not null
        ? $"v{LatestVersion.Major}.{LatestVersion.Minor}.{Math.Max(0, LatestVersion.Build)}"
        : TagName;
}

/// <summary>
/// Progress report emitted while downloading the update installer.
/// </summary>
public readonly record struct UpdateDownloadProgress(long BytesDownloaded, long TotalBytes)
{
    public double Percentage => TotalBytes > 0
        ? Math.Clamp(BytesDownloaded * 100.0 / TotalBytes, 0, 100)
        : 0;
}

