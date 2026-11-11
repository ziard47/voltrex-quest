using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using voltrex_quest.Helpers;
using voltrex_quest.Models;
using voltrex_quest.Services;

namespace voltrex_quest.ViewModels;

public sealed class DiscoverGameItemViewModel : ObservableObject
{
    private bool _isInLibrary;

    public DiscoverGameItemViewModel(Game game, bool isInLibrary)
    {
        Game = game;
        _isInLibrary = isInLibrary;
    }

    public Game Game { get; }

    public bool IsInLibrary
    {
        get => _isInLibrary;
        set
        {
            if (SetProperty(ref _isInLibrary, value))
            {
                OnPropertyChanged(nameof(AddButtonText));
                OnPropertyChanged(nameof(CanAdd));
            }
        }
    }

    public string AddButtonText => IsInLibrary ? "In Library" : "Add to Library";
    public bool CanAdd => !IsInLibrary;
}

public sealed record PlatformFilterOption(DiscoverPlatformFilter Value, string Label);

public sealed class DiscoverViewModel : ObservableObject
{
    private readonly AppServices _services;
    private CancellationTokenSource? _filterCts;
    private string _searchQuery = string.Empty;
    private PlatformFilterOption _selectedPlatformFilter;
    private string _selectedTheme = "All Themes";
    private bool _isLoading;
    private bool _hasError;
    private string _errorMessage = string.Empty;
    private int _totalMatchedCount;
    private string _resultsSummary = "Loading catalog...";
    private bool _hasLoadedOnce;

    public DiscoverViewModel() : this(AppServices.Current)
    {
    }

    public DiscoverViewModel(AppServices services)
    {
        _services = services;
        PlatformFilters =
        [
            new(DiscoverPlatformFilter.WindowsSupported, "Windows Supported"),
            new(DiscoverPlatformFilter.OverlaySupported, "Overlay Supported"),
            new(DiscoverPlatformFilter.AllGames, "All Catalog Games")
        ];

        _selectedPlatformFilter = PlatformFilters.FirstOrDefault(p => p.Value == _services.Settings.Current.DefaultDiscoverFilter)
                                  ?? PlatformFilters[0];

        ThemeFilters.Add("All Themes");

        AddToLibraryCommand = new AsyncRelayCommand<DiscoverGameItemViewModel>(AddToLibraryAsync);
        ClearFiltersCommand = new RelayCommand(ClearFilters);
        RetryLoadCommand = new AsyncRelayCommand(() => LoadCatalogAsync(forceRefresh: true));

        _services.Library.LibraryChanged += OnLibraryChanged;
    }

    public IncrementalLoadingCollection<DiscoverGameItemViewModel> Games { get; } = new(batchSize: 60);
    public IReadOnlyList<PlatformFilterOption> PlatformFilters { get; }
    public ObservableCollection<string> ThemeFilters { get; } = [];

    public AsyncRelayCommand<DiscoverGameItemViewModel> AddToLibraryCommand { get; }
    public RelayCommand ClearFiltersCommand { get; }
    public AsyncRelayCommand RetryLoadCommand { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ScheduleFilterRefresh();
            }
        }
    }

    public PlatformFilterOption SelectedPlatformFilter
    {
        get => _selectedPlatformFilter;
        set
        {
            if (value is not null && SetProperty(ref _selectedPlatformFilter, value))
            {
                ScheduleFilterRefresh(immediate: true);
            }
        }
    }

    public string SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && SetProperty(ref _selectedTheme, value))
            {
                ScheduleFilterRefresh(immediate: true);
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set
        {
            if (SetProperty(ref _isLoading, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(ShowContentGrid));
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set
        {
            if (SetProperty(ref _hasError, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(ShowContentGrid));
            }
        }
    }

    public string ErrorMessage
    {
        get => _errorMessage;
        private set => SetProperty(ref _errorMessage, value);
    }

    public int TotalMatchedCount
    {
        get => _totalMatchedCount;
        private set
        {
            if (SetProperty(ref _totalMatchedCount, value))
            {
                OnPropertyChanged(nameof(IsEmpty));
                OnPropertyChanged(nameof(ShowContentGrid));
            }
        }
    }

    public string ResultsSummary
    {
        get => _resultsSummary;
        private set => SetProperty(ref _resultsSummary, value);
    }

    public bool IsEmpty => !IsLoading && !HasError && _hasLoadedOnce && TotalMatchedCount == 0;
    public bool ShowContentGrid => !IsLoading && !HasError && TotalMatchedCount > 0;

    public async Task LoadCatalogAsync(bool forceRefresh = false)
    {
        if (_hasLoadedOnce && !forceRefresh && _services.Catalog.IsLoaded)
        {
            SyncLibraryFlags();
            return;
        }

        IsLoading = true;
        HasError = false;
        ErrorMessage = string.Empty;

        try
        {
            await _services.Library.InitializeAsync();
            await _services.Catalog.LoadCatalogAsync();

            if (ThemeFilters.Count <= 1)
            {
                foreach (var theme in _services.Catalog.AvailableThemes)
                {
                    ThemeFilters.Add(theme);
                }
            }

            _hasLoadedOnce = true;
            await ApplyFilterAsync(CancellationToken.None);
        }
        catch (Exception ex)
        {
            HasError = true;
            ErrorMessage = $"Failed to load gamelist.json: {ex.Message}";
            ResultsSummary = "Catalog unavailable";
        }
        finally
        {
            IsLoading = false;
        }
    }

    public void ApplyExternalSearch(string query)
    {
        SearchQuery = query;
    }

    private void ClearFilters()
    {
        _searchQuery = string.Empty;
        OnPropertyChanged(nameof(SearchQuery));
        _selectedPlatformFilter = PlatformFilters[0];
        OnPropertyChanged(nameof(SelectedPlatformFilter));
        _selectedTheme = "All Themes";
        OnPropertyChanged(nameof(SelectedTheme));
        ScheduleFilterRefresh(immediate: true);
    }

    private void ScheduleFilterRefresh(bool immediate = false)
    {
        if (!_services.Catalog.IsLoaded)
        {
            return;
        }

        _filterCts?.Cancel();
        _filterCts?.Dispose();
        _filterCts = new CancellationTokenSource();
        var token = _filterCts.Token;

        _ = RefreshFilteredDelayedAsync(immediate ? 0 : 140, token);
    }

    private async Task RefreshFilteredDelayedAsync(int delayMs, CancellationToken token)
    {
        try
        {
            if (delayMs > 0)
            {
                await Task.Delay(delayMs, token);
            }

            await ApplyFilterAsync(token);
        }
        catch (OperationCanceledException)
        {
            // Superseded by newer filter input
        }
    }

    private async Task ApplyFilterAsync(CancellationToken token)
    {
        var query = _searchQuery;
        var platform = _selectedPlatformFilter.Value;
        var theme = _selectedTheme;

        var matched = await Task.Run(() =>
        {
            token.ThrowIfCancellationRequested();
            return _services.Catalog.FilterGames(query, platform, theme);
        }, token);

        token.ThrowIfCancellationRequested();

        var items = matched.Select(g => new DiscoverGameItemViewModel(g, _services.Library.Contains(g.Id)));
        Games.Reset(items);
        TotalMatchedCount = matched.Count;

        ResultsSummary = string.IsNullOrWhiteSpace(query)
            ? $"{matched.Count:N0} games · {_selectedPlatformFilter.Label}"
            : $"{matched.Count:N0} {(matched.Count == 1 ? "match" : "matches")} for \"{query.Trim()}\"";
    }

    private async Task AddToLibraryAsync(DiscoverGameItemViewModel? item)
    {
        if (item is null || item.IsInLibrary)
        {
            return;
        }

        var added = await _services.Library.AddGameAsync(item.Game);
        if (added)
        {
            item.IsInLibrary = true;
        }
        else
        {
            // Already present in library (duplicate guard)
            item.IsInLibrary = _services.Library.Contains(item.Game.Id);
        }
    }

    private void SyncLibraryFlags()
    {
        _services.RunOnUiThread(() =>
        {
            foreach (var item in Games)
            {
                item.IsInLibrary = _services.Library.Contains(item.Game.Id);
            }
        });
    }

    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        SyncLibraryFlags();
    }
}

