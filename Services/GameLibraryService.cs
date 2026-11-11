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
/// Manages the user's personal game library and play sessions in a dedicated local persistence file.
/// </summary>
public sealed class GameLibraryService
{
    private sealed class LibraryPersistenceModel
    {
        public List<LibraryGame> Games { get; set; } = [];
        public List<GameSession> Sessions { get; set; } = [];
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<LibraryGame> _games = [];
    private readonly List<GameSession> _sessions = [];
    private readonly HashSet<string> _gameIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _isInitialized;

    public event EventHandler? LibraryChanged;
    public event EventHandler<GameSession>? SessionUpdated;

    public IReadOnlyList<LibraryGame> Games => _games;
    public IReadOnlyList<GameSession> Sessions => _sessions;

    public async Task InitializeAsync()
    {
        if (_isInitialized)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            var path = AppDataPaths.LibraryFilePath;
            if (File.Exists(path))
            {
                try
                {
                    await using var stream = File.OpenRead(path);
                    var model = await JsonSerializer.DeserializeAsync<LibraryPersistenceModel>(stream, JsonOptions)
                        .ConfigureAwait(false);

                    if (model is not null)
                    {
                        _games.Clear();
                        _gameIds.Clear();
                        foreach (var game in model.Games.Where(g => !string.IsNullOrWhiteSpace(g.Id)))
                        {
                            if (_gameIds.Add(game.Id))
                            {
                                _games.Add(game);
                            }
                        }

                        _sessions.Clear();
                        foreach (var session in model.Sessions)
                        {
                            // Any leftover active session from a previous app crash/restart is finalized cleanly
                            if (session.IsActive || session.Status is GameSessionStatus.Initializing or GameSessionStatus.Launching or GameSessionStatus.Running)
                            {
                                session.IsActive = false;
                                session.EndedAt ??= session.StartedAt;
                                session.Status = GameSessionStatus.Stopped;
                                if (string.IsNullOrWhiteSpace(session.Outcome) || session.Outcome is "Initializing" or "Running" or "Launching")
                                {
                                    session.Outcome = "Interrupted (app closed)";
                                }
                            }

                            _sessions.Add(session);
                        }
                    }
                }
                catch
                {
                    // Start clean if persisted file is corrupted rather than crashing
                    _games.Clear();
                    _gameIds.Clear();
                    _sessions.Clear();
                }
            }

            _isInitialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public bool Contains(string gameId) => !string.IsNullOrWhiteSpace(gameId) && _gameIds.Contains(gameId);

    public async Task<bool> AddGameAsync(Game catalogGame)
    {
        ArgumentNullException.ThrowIfNull(catalogGame);
        await InitializeAsync().ConfigureAwait(false);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_gameIds.Add(catalogGame.Id))
            {
                return false;
            }

            var entry = LibraryGame.FromCatalogGame(catalogGame);
            _games.Add(entry);
            await SaveLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public async Task<bool> RemoveGameAsync(string gameId)
    {
        if (string.IsNullOrWhiteSpace(gameId))
        {
            return false;
        }

        await InitializeAsync().ConfigureAwait(false);

        bool removed;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!_gameIds.Remove(gameId))
            {
                return false;
            }

            removed = _games.RemoveAll(g => string.Equals(g.Id, gameId, StringComparison.OrdinalIgnoreCase)) > 0;
            if (removed)
            {
                await SaveLockedAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            _gate.Release();
        }

        if (removed)
        {
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }

        return removed;
    }

    public async Task UpdateExecutablePathAsync(string gameId, string? executablePath, string? launchArguments = null)
    {
        await InitializeAsync().ConfigureAwait(false);

        LibraryGame? target = null;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            target = _games.FirstOrDefault(g => string.Equals(g.Id, gameId, StringComparison.OrdinalIgnoreCase));
            if (target is null)
            {
                return;
            }

            target.CustomExecutablePath = executablePath;
            if (launchArguments is not null)
            {
                target.LaunchArguments = launchArguments;
            }

            await SaveLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        if (target is not null)
        {
            LibraryChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task RecordSessionStartAsync(LibraryGame game, GameSession session)
    {
        await InitializeAsync().ConfigureAwait(false);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var stored = _games.FirstOrDefault(g => string.Equals(g.Id, game.Id, StringComparison.OrdinalIgnoreCase));
            if (stored is not null)
            {
                stored.LastPlayedAt = session.StartedAt;
                stored.PlayCount += 1;
            }

            _sessions.RemoveAll(s => s.SessionId == session.SessionId);
            _sessions.Insert(0, session);
            if (_sessions.Count > 100)
            {
                _sessions.RemoveRange(100, _sessions.Count - 100);
            }

            await SaveLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
        SessionUpdated?.Invoke(this, session);
    }

    public async Task SaveSessionUpdateAsync(GameSession session)
    {
        await InitializeAsync().ConfigureAwait(false);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        SessionUpdated?.Invoke(this, session);
        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearPlayHistoryAsync()
    {
        await InitializeAsync().ConfigureAwait(false);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _sessions.Clear();
            foreach (var game in _games)
            {
                game.LastPlayedAt = null;
                game.PlayCount = 0;
            }

            await SaveLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearLibraryAsync()
    {
        await InitializeAsync().ConfigureAwait(false);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _games.Clear();
            _gameIds.Clear();
            _sessions.Clear();
            await SaveLockedAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }

        LibraryChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveLockedAsync()
    {
        var model = new LibraryPersistenceModel
        {
            Games = _games.ToList(),
            Sessions = _sessions.ToList()
        };

        var path = AppDataPaths.LibraryFilePath;
        var tempPath = path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, model, JsonOptions).ConfigureAwait(false);
        }

        File.Move(tempPath, path, overwrite: true);
    }
}

