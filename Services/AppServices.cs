using Microsoft.UI.Dispatching;
using System;
using System.Threading.Tasks;

namespace voltrex_quest.Services;

/// <summary>
/// Centralizes singleton service instances for the application lifecycle.
/// </summary>
public sealed class AppServices
{
    private static AppServices? _instance;
    public static AppServices Current => _instance ??= new AppServices();

    private AppServices()
    {
        Settings = new SettingsService();
        Catalog = new GameCatalogService();
        Library = new GameLibraryService();
        Launcher = new GameLauncherService(Library, RunOnUiThread);
    }

    public SettingsService Settings { get; }
    public GameCatalogService Catalog { get; }
    public GameLibraryService Library { get; }
    public GameLauncherService Launcher { get; }
    public DispatcherQueue? UiDispatcher { get; set; }

    public async Task InitializeAsync()
    {
        await Settings.InitializeAsync().ConfigureAwait(false);
        await Library.InitializeAsync().ConfigureAwait(false);
    }

    public void RunOnUiThread(Action action)
    {
        var dispatcher = UiDispatcher;
        if (dispatcher is null || dispatcher.HasThreadAccess)
        {
            action();
            return;
        }

        dispatcher.TryEnqueue(() => action());
    }
}
