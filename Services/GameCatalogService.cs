using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using voltrex_quest.Models;

namespace voltrex_quest.Services;

/// <summary>
/// Loads and queries the bundled Discord Quest-supported game catalog (Data/gamelist.json).
/// </summary>
public sealed class GameCatalogService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private List<Game> _games = [];
    private List<string> _availableThemes = [];
    private bool _isLoaded;

    public bool IsLoaded => _isLoaded;
    public int TotalCount => _games.Count;
    public int WindowsSupportedCount { get; private set; }
    public IReadOnlyList<string> AvailableThemes => _availableThemes;
    public string CatalogPath => AppDataPaths.ResolveCatalogFilePath();

    public async Task<IReadOnlyList<Game>> LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        if (_isLoaded)
        {
            return _games;
        }

        await _loadLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isLoaded)
            {
                return _games;
            }

            var path = AppDataPaths.ResolveCatalogFilePath();
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("Could not locate gamelist.json in Data/gamelist.json.", path);
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 65536,
                useAsync: true);

            var parsed = await JsonSerializer.DeserializeAsync<List<Game>>(stream, JsonOptions, cancellationToken)
                .ConfigureAwait(false);

            _games = (parsed ?? [])
                .Where(g => !string.IsNullOrWhiteSpace(g.Id) && !string.IsNullOrWhiteSpace(g.Name))
                .ToList();

            WindowsSupportedCount = _games.Count(g => g.SupportsWindows);
            _availableThemes = _games
                .SelectMany(g => g.Themes)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _isLoaded = true;
            return _games;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    public List<Game> FilterGames(
        string? searchQuery,
        DiscoverPlatformFilter platformFilter,
        string? themeFilter)
    {
        if (!_isLoaded)
        {
            return [];
        }

        IEnumerable<Game> query = _games;

        query = platformFilter switch
        {
            DiscoverPlatformFilter.WindowsSupported => query.Where(g => g.SupportsWindows),
            DiscoverPlatformFilter.OverlaySupported => query.Where(g => g.Overlay),
            _ => query
        };

        if (!string.IsNullOrWhiteSpace(themeFilter) &&
            !string.Equals(themeFilter, "All Themes", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(g => g.Themes.Any(t => string.Equals(t, themeFilter, StringComparison.OrdinalIgnoreCase)));
        }

        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var term = searchQuery.Trim();
            query = query.Where(g => MatchesSearch(g, term));

            // Rank prefix/exact matches ahead of substring matches for responsive discovery
            return query
                .OrderByDescending(g => string.Equals(g.Name, term, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(g => g.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(g => !string.IsNullOrWhiteSpace(g.CoverImageHash))
                .ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        return query.ToList();
    }

    private static bool MatchesSearch(Game game, string term)
    {
        if (game.Name.Contains(term, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (game.Aliases.Any(a => a.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (game.Themes.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        if (game.Executables.Any(e => e.Name.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        return false;
    }
}

