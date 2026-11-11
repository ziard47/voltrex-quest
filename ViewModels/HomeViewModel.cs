using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using voltrex_quest.Helpers;
using voltrex_quest.Models;
using voltrex_quest.Services;

namespace voltrex_quest.ViewModels;

public sealed class HomeViewModel : ObservableObject
{
    private readonly AppServices _services;
    private int _totalLibraryGames;
    private int _availableCatalogGames;
    private int _windowsSupportedGames;
    private int _totalSessionsCount;
    private bool _isCatalogLoading;

    public HomeViewModel() : this(AppServices.Current)
    {
    }

    public HomeViewModel(AppServices services)
    {
        _services = services;
        _services.Library.LibraryChanged += OnLibraryChanged;
        _services.Launcher.SessionStateChanged += OnSessionStateChanged;

        OpenDiscoverCommand = new RelayCommand(() => NavigateRequested?.Invoke("discover"));
        OpenLibraryCommand = new RelayCommand(() => NavigateRequested?.Invoke("library"));
    }

    public event Action<string>? NavigateRequested;

    public RelayCommand OpenDiscoverCommand { get; }
    public RelayCommand OpenLibraryCommand { get; }

    public ObservableCollection<LibraryGame> RecentlyPlayedGames { get; } = [];
    public ObservableCollection<LibraryGame> RecentlyAddedGames { get; } = [];
    public ObservableCollection<GameSession> RecentSessions { get; } = [];

    public int TotalLibraryGames
    {
        get => _totalLibraryGames;
        private set
        {
            if (SetProperty(ref _totalLibraryGames, value))
            {
                OnPropertyChanged(nameof(HasLibraryGames));
            }
        }
    }

    public int AvailableCatalogGames
    {
        get => _availableCatalogGames;
        private set => SetProperty(ref _availableCatalogGames, value);
    }

    public int WindowsSupportedGames
    {
        get => _windowsSupportedGames;
        private set => SetProperty(ref _windowsSupportedGames, value);
    }

    public int TotalSessionsCount
    {
        get => _totalSessionsCount;
        private set => SetProperty(ref _totalSessionsCount, value);
    }

    public bool IsCatalogLoading
    {
        get => _isCatalogLoading;
        private set => SetProperty(ref _isCatalogLoading, value);
    }

    public bool HasLibraryGames => TotalLibraryGames > 0;
    public bool HasRecentlyPlayed => RecentlyPlayedGames.Count > 0;
    public bool HasRecentlyAdded => RecentlyAddedGames.Count > 0;
    public bool HasRecentSessions => RecentSessions.Count > 0;

    public async Task LoadAsync()
    {
        await _services.Library.InitializeAsync();
        RefreshFromLibrary();

        if (!_services.Catalog.IsLoaded)
        {
            IsCatalogLoading = true;
            try
            {
                await _services.Catalog.LoadCatalogAsync();
            }
            catch
            {
                // Catalog error is handled in Discover; Home keeps graceful 0 count
            }
            finally
            {
                IsCatalogLoading = false;
            }
        }

        AvailableCatalogGames = _services.Catalog.TotalCount;
        WindowsSupportedGames = _services.Catalog.WindowsSupportedCount;
    }

    public void RefreshFromLibrary()
    {
        _services.RunOnUiThread(() =>
        {
            var games = _services.Library.Games;
            TotalLibraryGames = games.Count;
            TotalSessionsCount = _services.Library.Sessions.Count;

            RecentlyPlayedGames.Clear();
            foreach (var g in games
                         .Where(g => g.LastPlayedAt.HasValue)
                         .OrderByDescending(g => g.LastPlayedAt)
                         .Take(6))
            {
                RecentlyPlayedGames.Add(g);
            }

            RecentlyAddedGames.Clear();
            foreach (var g in games
                         .OrderByDescending(g => g.AddedAt)
                         .Take(6))
            {
                RecentlyAddedGames.Add(g);
            }

            RecentSessions.Clear();
            foreach (var s in _services.Library.Sessions.Take(8))
            {
                RecentSessions.Add(s);
            }

            OnPropertyChanged(nameof(HasRecentlyPlayed));
            OnPropertyChanged(nameof(HasRecentlyAdded));
            OnPropertyChanged(nameof(HasRecentSessions));
        });
    }

    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        RefreshFromLibrary();
    }

    private void OnSessionStateChanged(object? sender, GameSession session)
    {
        RefreshFromLibrary();
    }
}
