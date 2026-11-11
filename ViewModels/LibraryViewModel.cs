using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using voltrex_quest.Helpers;
using voltrex_quest.Models;
using voltrex_quest.Services;

namespace voltrex_quest.ViewModels;

public sealed record LibrarySortChoice(LibrarySortOption Option, string Label);

public sealed class LibraryViewModel : ObservableObject
{
    private readonly AppServices _services;
    private string _searchQuery = string.Empty;
    private LibrarySortChoice _selectedSort;
    private int _totalLibraryCount;
    private string? _statusMessage;
    private bool _isStatusError;

    public LibraryViewModel() : this(AppServices.Current)
    {
    }

    public LibraryViewModel(AppServices services)
    {
        _services = services;
        SortOptions =
        [
            new(LibrarySortOption.NameAscending, "Name (A–Z)"),
            new(LibrarySortOption.NameDescending, "Name (Z–A)"),
            new(LibrarySortOption.RecentlyAdded, "Recently Added"),
            new(LibrarySortOption.RecentlyPlayed, "Recently Played")
        ];

        _selectedSort = SortOptions.FirstOrDefault(s => s.Option == _services.Settings.Current.DefaultLibrarySort)
                        ?? SortOptions[0];

        ClearSearchCommand = new RelayCommand(() => SearchQuery = string.Empty);
        OpenDiscoverCommand = new RelayCommand(() => NavigateToDiscoverRequested?.Invoke());
        DismissStatusCommand = new RelayCommand(() => StatusMessage = null);

        _services.Library.LibraryChanged += OnLibraryChanged;
    }

    public event Action? NavigateToDiscoverRequested;

    public ObservableCollection<LibraryGame> FilteredGames { get; } = [];
    public IReadOnlyList<LibrarySortChoice> SortOptions { get; }

    public RelayCommand ClearSearchCommand { get; }
    public RelayCommand OpenDiscoverCommand { get; }
    public RelayCommand DismissStatusCommand { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ApplyFilterAndSort();
            }
        }
    }

    public LibrarySortChoice SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (value is not null && SetProperty(ref _selectedSort, value))
            {
                ApplyFilterAndSort();
            }
        }
    }

    public int TotalLibraryCount
    {
        get => _totalLibraryCount;
        private set
        {
            if (SetProperty(ref _totalLibraryCount, value))
            {
                OnPropertyChanged(nameof(IsLibraryEmpty));
                OnPropertyChanged(nameof(IsSearchEmpty));
                OnPropertyChanged(nameof(ShowLibraryGrid));
                OnPropertyChanged(nameof(LibrarySummary));
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool IsStatusError
    {
        get => _isStatusError;
        set => SetProperty(ref _isStatusError, value);
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);
    public bool IsLibraryEmpty => TotalLibraryCount == 0;
    public bool IsSearchEmpty => TotalLibraryCount > 0 && FilteredGames.Count == 0;
    public bool ShowLibraryGrid => FilteredGames.Count > 0;

    public string LibrarySummary =>
        TotalLibraryCount == 1
            ? "1 game in your library"
            : $"{TotalLibraryCount:N0} games in your library";

    public async Task LoadAsync()
    {
        await _services.Library.InitializeAsync();
        ApplyFilterAndSort();
    }

    public void ShowInfo(string message, bool isError = false)
    {
        IsStatusError = isError;
        StatusMessage = message;
    }

    public void ApplyFilterAndSort()
    {
        _services.RunOnUiThread(() =>
        {
            var all = _services.Library.Games;
            TotalLibraryCount = all.Count;

            IEnumerable<LibraryGame> query = all;

            if (!string.IsNullOrWhiteSpace(_searchQuery))
            {
                var term = _searchQuery.Trim();
                query = query.Where(g =>
                    g.Name.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                    g.Aliases.Any(a => a.Contains(term, StringComparison.OrdinalIgnoreCase)) ||
                    g.Themes.Any(t => t.Contains(term, StringComparison.OrdinalIgnoreCase)));
            }

            query = _selectedSort.Option switch
            {
                LibrarySortOption.NameDescending => query.OrderByDescending(g => g.Name, StringComparer.OrdinalIgnoreCase),
                LibrarySortOption.RecentlyAdded => query.OrderByDescending(g => g.AddedAt).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
                LibrarySortOption.RecentlyPlayed => query.OrderByDescending(g => g.LastPlayedAt ?? DateTimeOffset.MinValue).ThenBy(g => g.Name, StringComparer.OrdinalIgnoreCase),
                _ => query.OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
            };

            FilteredGames.Clear();
            foreach (var game in query)
            {
                FilteredGames.Add(game);
            }

            OnPropertyChanged(nameof(IsLibraryEmpty));
            OnPropertyChanged(nameof(IsSearchEmpty));
            OnPropertyChanged(nameof(ShowLibraryGrid));
        });
    }

    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        ApplyFilterAndSort();
    }
}

