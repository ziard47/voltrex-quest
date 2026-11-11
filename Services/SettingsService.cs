using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using voltrex_quest.Models;

namespace voltrex_quest.Services;

/// <summary>
/// Persists and manages user preferences independently from game catalog data.
/// </summary>
public sealed class SettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _isInitialized;

    public AppSettings Current { get; private set; } = new();

    public event EventHandler? SettingsChanged;

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

            var path = AppDataPaths.SettingsFilePath;
            if (File.Exists(path))
            {
                try
                {
                    await using var stream = File.OpenRead(path);
                    var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions)
                        .ConfigureAwait(false);
                    if (loaded is not null)
                    {
                        Current = loaded;
                    }
                }
                catch
                {
                    Current = new AppSettings();
                }
            }

            _isInitialized = true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var path = AppDataPaths.SettingsFilePath;
            var tempPath = path + ".tmp";
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, Current, JsonOptions).ConfigureAwait(false);
            }

            File.Move(tempPath, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}

