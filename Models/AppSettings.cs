namespace voltrex_quest.Models;

public enum LibrarySortOption
{
    NameAscending,
    NameDescending,
    RecentlyAdded,
    RecentlyPlayed
}

public enum DiscoverPlatformFilter
{
    WindowsSupported,
    AllGames,
    OverlaySupported
}

/// <summary>
/// User-configurable application settings persisted locally.
/// </summary>
public sealed class AppSettings
{
    public DiscoverPlatformFilter DefaultDiscoverFilter { get; set; } = DiscoverPlatformFilter.WindowsSupported;
    public LibrarySortOption DefaultLibrarySort { get; set; } = LibrarySortOption.NameAscending;
    public bool ConfirmBeforeRemove { get; set; } = true;
    public bool PromptForExecutableOnPlay { get; set; } = true;
}

