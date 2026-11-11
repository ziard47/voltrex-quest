using Microsoft.UI.Xaml;
using System;
using voltrex_quest.Helpers;
using voltrex_quest.Services;

namespace voltrex_quest.ViewModels;

public sealed class MainViewModel : ObservableObject
{
    private readonly AppServices _services;
    private int _libraryCount;
    private string _searchQuery = string.Empty;
    private string _activePageTag = "home";

    public MainViewModel() : this(AppServices.Current)
    {
    }

    public MainViewModel(AppServices services)
    {
        _services = services;
        _libraryCount = _services.Library.Games.Count;
        _services.Library.LibraryChanged += OnLibraryChanged;
    }

    public int LibraryCount
    {
        get => _libraryCount;
        private set
        {
            if (SetProperty(ref _libraryCount, value))
            {
                OnPropertyChanged(nameof(HasLibraryItems));
                OnPropertyChanged(nameof(LibraryBadgeVisibility));
            }
        }
    }

    public bool HasLibraryItems => LibraryCount > 0;
    public Visibility LibraryBadgeVisibility => HasLibraryItems ? Visibility.Visible : Visibility.Collapsed;

    public string SearchQuery
    {
        get => _searchQuery;
        set => SetProperty(ref _searchQuery, value);
    }

    public string ActivePageTag
    {
        get => _activePageTag;
        set => SetProperty(ref _activePageTag, value);
    }

    public void RefreshCounts()
    {
        _services.RunOnUiThread(() => LibraryCount = _services.Library.Games.Count);
    }

    private void OnLibraryChanged(object? sender, EventArgs e)
    {
        RefreshCounts();
    }
}

