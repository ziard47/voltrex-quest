using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using voltrex_quest.Helpers;

namespace voltrex_quest.Models;

/// <summary>
/// Represents a Discord Quest-supported / detectable game entry from gamelist.json.
/// </summary>
public class Game
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("aliases")]
    public List<string> Aliases { get; set; } = [];

    [JsonPropertyName("cover_image_hash")]
    public string? CoverImageHash { get; set; }

    [JsonPropertyName("icon_hash")]
    public string? IconHash { get; set; }

    [JsonPropertyName("executables")]
    public List<GameExecutable> Executables { get; set; } = [];

    [JsonPropertyName("hook")]
    public bool Hook { get; set; }

    [JsonPropertyName("overlay")]
    public bool Overlay { get; set; }

    [JsonPropertyName("overlay_compatibility_hook")]
    public bool OverlayCompatibilityHook { get; set; }

    [JsonPropertyName("overlay_methods")]
    public int? OverlayMethods { get; set; }

    [JsonPropertyName("overlay_warn")]
    public bool OverlayWarn { get; set; }

    [JsonPropertyName("themes")]
    public List<string> Themes { get; set; } = [];

    [JsonPropertyName("third_party_skus")]
    public List<ThirdPartySku> ThirdPartySkus { get; set; } = [];

    [JsonPropertyName("linked_applications")]
    public List<LinkedApplication> LinkedApplications { get; set; } = [];

    [JsonIgnore]
    public string? CoverImageUrl =>
        !string.IsNullOrWhiteSpace(CoverImageHash) && !string.IsNullOrWhiteSpace(Id)
            ? $"https://cdn.discordapp.com/app-icons/{Id}/{CoverImageHash}.png?size=512"
            : IconImageUrl;

    [JsonIgnore]
    public string? IconImageUrl =>
        !string.IsNullOrWhiteSpace(IconHash) && !string.IsNullOrWhiteSpace(Id)
            ? $"https://cdn.discordapp.com/app-icons/{Id}/{IconHash}.png?size=256"
            : null;

    [JsonIgnore]
    public bool SupportsWindows =>
        Executables.Any(e => string.Equals(e.Os, "win32", StringComparison.OrdinalIgnoreCase));

    [JsonIgnore]
    public string SupportedStatusLabel
    {
        get
        {
            if (SupportsWindows && Overlay)
            {
                return "Windows • Overlay";
            }

            if (SupportsWindows)
            {
                return Hook ? "Windows • Hook" : "Windows";
            }

            if (Executables.Count > 0)
            {
                var osList = Executables
                    .Select(e => e.Os)
                    .Where(o => !string.IsNullOrWhiteSpace(o))
                    .Distinct(StringComparer.OrdinalIgnoreCase);
                return string.Join(", ", osList);
            }

            return Hook ? "Hook Supported" : "Catalog Entry";
        }
    }

    [JsonIgnore]
    public string PrimaryExecutableDisplay
    {
        get
        {
            var winExe = Executables.FirstOrDefault(e =>
                string.Equals(e.Os, "win32", StringComparison.OrdinalIgnoreCase) && !e.IsLauncher)
                ?? Executables.FirstOrDefault(e =>
                    string.Equals(e.Os, "win32", StringComparison.OrdinalIgnoreCase))
                ?? Executables.FirstOrDefault();

            if (winExe is null || string.IsNullOrWhiteSpace(winExe.Name))
            {
                return "No executable listed";
            }

            return System.IO.Path.GetFileName(winExe.Name.TrimStart('>'));
        }
    }

    [JsonIgnore]
    public string ThemesSummary =>
        Themes.Count > 0 ? string.Join(" · ", Themes.Take(3)) : "General";

    [JsonIgnore]
    public string DistributorsSummary
    {
        get
        {
            var dists = ThirdPartySkus
                .Select(s => FormatDistributor(s.Distributor))
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(3)
                .ToList();

            return dists.Count > 0 ? string.Join(", ", dists) : "Standalone";
        }
    }

    [JsonIgnore]
    public string? SteamAppId =>
        ThirdPartySkus
            .FirstOrDefault(s =>
                string.Equals(s.Distributor, "steam", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(s.Id)
                && long.TryParse(s.Id, out _))
            ?.Id;

    [JsonIgnore]
    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return "?";
            }

            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 1)
            {
                return words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant();
            }

            return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
        }
    }

    private static string FormatDistributor(string? raw) => raw?.ToLowerInvariant() switch
    {
        "steam" => "Steam",
        "epic" => "Epic Games",
        "battlenet" => "Battle.net",
        "uplay" => "Ubisoft",
        "origin" => "EA",
        "gog" => "GOG",
        "xbox" => "Xbox",
        "playstation" => "PlayStation",
        "microsoft" => "Microsoft Store",
        "roblox" => "Roblox",
        "discord" => "Discord",
        _ => string.IsNullOrWhiteSpace(raw) ? string.Empty : char.ToUpperInvariant(raw[0]) + raw[1..]
    };
}

public sealed class GameExecutable
{
    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    [JsonPropertyName("is_launcher")]
    public bool IsLauncher { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("os")]
    public string Os { get; set; } = string.Empty;
}

public sealed class ThirdPartySku
{
    [JsonPropertyName("distributor")]
    public string Distributor { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string? Id { get; set; }
}

public sealed class LinkedApplication
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public int Type { get; set; }
}

/// <summary>
/// Persisted entry in the user's personal game library.
/// </summary>
public sealed class LibraryGame : ObservableObject
{
    private string? _customExecutablePath;
    private string? _launchArguments;
    private DateTimeOffset? _lastPlayedAt;
    private int _playCount;

    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? CoverImageHash { get; set; }
    public string? IconHash { get; set; }
    public List<string> Aliases { get; set; } = [];
    public List<string> Themes { get; set; } = [];
    public List<GameExecutable> Executables { get; set; } = [];
    public List<ThirdPartySku> ThirdPartySkus { get; set; } = [];
    public bool Hook { get; set; }
    public bool Overlay { get; set; }

    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? LastPlayedAt
    {
        get => _lastPlayedAt;
        set
        {
            if (SetProperty(ref _lastPlayedAt, value))
            {
                OnPropertyChanged(nameof(LastPlayedDisplay));
            }
        }
    }

    public int PlayCount
    {
        get => _playCount;
        set
        {
            if (SetProperty(ref _playCount, value))
            {
                OnPropertyChanged(nameof(PlayCountDisplay));
            }
        }
    }

    public string? CustomExecutablePath
    {
        get => _customExecutablePath;
        set
        {
            if (SetProperty(ref _customExecutablePath, value))
            {
                OnPropertyChanged(nameof(HasExecutableConfigured));
                OnPropertyChanged(nameof(ExecutableStatusDisplay));
            }
        }
    }

    public string? LaunchArguments
    {
        get => _launchArguments;
        set => SetProperty(ref _launchArguments, value);
    }

    [JsonIgnore]
    public string? CoverImageUrl =>
        !string.IsNullOrWhiteSpace(CoverImageHash) && !string.IsNullOrWhiteSpace(Id)
            ? $"https://cdn.discordapp.com/app-icons/{Id}/{CoverImageHash}.png?size=512"
            : IconImageUrl;

    [JsonIgnore]
    public string? IconImageUrl =>
        !string.IsNullOrWhiteSpace(IconHash) && !string.IsNullOrWhiteSpace(Id)
            ? $"https://cdn.discordapp.com/app-icons/{Id}/{IconHash}.png?size=256"
            : null;

    [JsonIgnore]
    public bool HasExecutableConfigured =>
        !string.IsNullOrWhiteSpace(CustomExecutablePath) && System.IO.File.Exists(CustomExecutablePath);

    [JsonIgnore]
    public string? SteamAppId =>
        ThirdPartySkus
            .FirstOrDefault(s =>
                string.Equals(s.Distributor, "steam", StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(s.Id)
                && long.TryParse(s.Id, out _))
            ?.Id;

    [JsonIgnore]
    public string ThemesSummary =>
        Themes.Count > 0 ? string.Join(" · ", Themes.Take(3)) : "General";

    [JsonIgnore]
    public string ExpectedExecutableName
    {
        get
        {
            var winExe = Executables.FirstOrDefault(e =>
                string.Equals(e.Os, "win32", StringComparison.OrdinalIgnoreCase) && !e.IsLauncher)
                ?? Executables.FirstOrDefault(e =>
                    string.Equals(e.Os, "win32", StringComparison.OrdinalIgnoreCase));

            if (winExe is null || string.IsNullOrWhiteSpace(winExe.Name))
            {
                return "Executable not set";
            }

            return System.IO.Path.GetFileName(winExe.Name.TrimStart('>'));
        }
    }

    [JsonIgnore]
    public string ExecutableStatusDisplay
    {
        get
        {
            if (HasExecutableConfigured)
            {
                return $"Ready · {System.IO.Path.GetFileName(CustomExecutablePath)}";
            }

            if (!string.IsNullOrWhiteSpace(SteamAppId))
            {
                return $"Steam App #{SteamAppId}";
            }

            return $"Target: {ExpectedExecutableName}";
        }
    }

    [JsonIgnore]
    public string AddedAtDisplay => $"Added {AddedAt.ToLocalTime():MMM d, yyyy}";

    [JsonIgnore]
    public string LastPlayedDisplay =>
        LastPlayedAt.HasValue
            ? $"Played {LastPlayedAt.Value.ToLocalTime():MMM d, HH:mm}"
            : "Never played";

    [JsonIgnore]
    public string PlayCountDisplay =>
        PlayCount == 1 ? "1 session" : $"{PlayCount} sessions";

    [JsonIgnore]
    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Name))
            {
                return "?";
            }

            var words = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 1)
            {
                return words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant();
            }

            return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
        }
    }

    public static LibraryGame FromCatalogGame(Game game) => new()
    {
        Id = game.Id,
        Name = game.Name,
        CoverImageHash = game.CoverImageHash,
        IconHash = game.IconHash,
        Aliases = [.. game.Aliases],
        Themes = [.. game.Themes],
        Executables = game.Executables.Select(e => new GameExecutable
        {
            Arguments = e.Arguments,
            IsLauncher = e.IsLauncher,
            Name = e.Name,
            Os = e.Os
        }).ToList(),
        ThirdPartySkus = game.ThirdPartySkus.Select(s => new ThirdPartySku
        {
            Distributor = s.Distributor,
            Id = s.Id
        }).ToList(),
        Hook = game.Hook,
        Overlay = game.Overlay,
        AddedAt = DateTimeOffset.UtcNow
    };
}

