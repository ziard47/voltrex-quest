using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using voltrex_quest.Helpers;
using voltrex_quest.Models;
using voltrex_quest.Services;

namespace voltrex_quest.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly AppServices _services;
    private PlatformFilterOption _selectedDiscoverFilter;
    private LibrarySortChoice _selectedLibrarySort;
    private bool _confirmBeforeRemove;
    private bool _promptForExecutableOnPlay;
    private string? _statusMessage;

    public SettingsViewModel() : this(AppServices.Current)
    {
    }

    public SettingsViewModel(AppServices services)
    {
        _services = services;

        DiscoverFilterOptions =
        [
            new(DiscoverPlatformFilter.WindowsSupported, "Windows Supported"),
            new(DiscoverPlatformFilter.OverlaySupported, "Overlay Supported"),
            new(DiscoverPlatformFilter.AllGames, "All Catalog Games")
        ];

        LibrarySortOptions =
        [
            new(LibrarySortOption.NameAscending, "Name (A–Z)"),
            new(LibrarySortOption.NameDescending, "Name (Z–A)"),
            new(LibrarySortOption.RecentlyAdded, "Recently Added"),
            new(LibrarySortOption.RecentlyPlayed, "Recently Played")
        ];

        var current = _services.Settings.Current;
        _selectedDiscoverFilter = DiscoverFilterOptions.FirstOrDefault(o => o.Value == current.DefaultDiscoverFilter)
                                  ?? DiscoverFilterOptions[0];
        _selectedLibrarySort = LibrarySortOptions.FirstOrDefault(o => o.Option == current.DefaultLibrarySort)
                               ?? LibrarySortOptions[0];
        _confirmBeforeRemove = current.ConfirmBeforeRemove;
        _promptForExecutableOnPlay = current.PromptForExecutableOnPlay;

        OpenDataFolderCommand = new RelayCommand(OpenDataFolder);
        OpenGitHubReleasesCommand = new RelayCommand(OpenGitHubReleases);
    }

    public IReadOnlyList<PlatformFilterOption> DiscoverFilterOptions { get; }
    public IReadOnlyList<LibrarySortChoice> LibrarySortOptions { get; }
    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand OpenGitHubReleasesCommand { get; }

    public PlatformFilterOption SelectedDiscoverFilter
    {
        get => _selectedDiscoverFilter;
        set
        {
            if (value is not null && SetProperty(ref _selectedDiscoverFilter, value))
            {
                _services.Settings.Current.DefaultDiscoverFilter = value.Value;
                _ = SaveWithStatusAsync("Default Discover filter saved.");
            }
        }
    }

    public LibrarySortChoice SelectedLibrarySort
    {
        get => _selectedLibrarySort;
        set
        {
            if (value is not null && SetProperty(ref _selectedLibrarySort, value))
            {
                _services.Settings.Current.DefaultLibrarySort = value.Option;
                _ = SaveWithStatusAsync("Default Library sort order saved.");
            }
        }
    }

    public bool ConfirmBeforeRemove
    {
        get => _confirmBeforeRemove;
        set
        {
            if (SetProperty(ref _confirmBeforeRemove, value))
            {
                _services.Settings.Current.ConfirmBeforeRemove = value;
                _ = SaveWithStatusAsync("Removal confirmation preference saved.");
            }
        }
    }

    public bool PromptForExecutableOnPlay
    {
        get => _promptForExecutableOnPlay;
        set
        {
            if (SetProperty(ref _promptForExecutableOnPlay, value))
            {
                _services.Settings.Current.PromptForExecutableOnPlay = value;
                _ = SaveWithStatusAsync("Launch prompt preference saved.");
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public string LibraryFilePath => AppDataPaths.LibraryFilePath;
    public string SettingsFilePath => AppDataPaths.SettingsFilePath;
    public string CatalogFilePath => _services.Catalog.CatalogPath;
    public string AppVersionDisplay => _services.Updates.CurrentVersionDisplay;
    public string AppVersionTag => $"v{_services.Updates.CurrentVersionNumber}";
    public string GitHubRepoSlug => $"{UpdateService.RepositoryOwner}/{UpdateService.RepositoryName}";

    public string CatalogStatsText =>
        _services.Catalog.IsLoaded
            ? $"{_services.Catalog.TotalCount:N0} total entries ({_services.Catalog.WindowsSupportedCount:N0} Windows-supported)"
            : "Loaded on demand from Data/gamelist.json";

    public string LibraryStatsText =>
        $"{_services.Library.Games.Count:N0} games saved · {_services.Library.Sessions.Count:N0} recorded sessions";

    public void RefreshStats()
    {
        OnPropertyChanged(nameof(CatalogStatsText));
        OnPropertyChanged(nameof(LibraryStatsText));
    }

    public async Task ClearPlayHistoryAsync()
    {
        await _services.Library.ClearPlayHistoryAsync();
        RefreshStats();
        StatusMessage = "Play history and session records cleared.";
    }

    public async Task ClearLibraryAsync()
    {
        await _services.Library.ClearLibraryAsync();
        RefreshStats();
        StatusMessage = "Personal library cleared. The catalog (gamelist.json) remains unchanged.";
    }

    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = AppDataPaths.UserDataDirectory,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open folder: {ex.Message}";
        }
    }

    private void OpenGitHubReleases()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = UpdateService.ReleasesPageUrl,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not open GitHub Releases: {ex.Message}";
        }
    }

    private async Task SaveWithStatusAsync(string message)
    {
        try
        {
            await _services.Settings.SaveAsync();
            StatusMessage = message;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to save settings: {ex.Message}";
        }
    }
}

