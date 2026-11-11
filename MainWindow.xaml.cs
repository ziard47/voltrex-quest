using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.IO;
using System.Linq;
using voltrex_quest.ViewModels;
using voltrex_quest.Views;
using Windows.Graphics;
using WinRT.Interop;

namespace voltrex_quest;

/// <summary>
/// Main application shell window hosting sidebar navigation, search, and page frame.
/// </summary>
public sealed partial class MainWindow : Window
{
    public static MainWindow? Instance { get; private set; }
    public static nint WindowHandle { get; private set; }

    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();
        Instance = this;
        WindowHandle = WindowNative.GetWindowHandle(this);

        ConfigureWindow();
        ViewModel.RefreshCounts();
        Closed += (_, _) => Services.AppServices.Current.Launcher.Dispose();

        // Start on Home Dashboard
        NavigateToPage("home");
    }

    public void NavigateToPage(string tag, object? parameter = null)
    {
        var targetType = tag switch
        {
            "home" => typeof(HomePage),
            "discover" => typeof(DiscoverPage),
            "library" => typeof(LibraryPage),
            "settings" => typeof(SettingsPage),
            _ => typeof(HomePage)
        };

        if (ContentFrame.CurrentSourcePageType == targetType && parameter is null)
        {
            return;
        }

        ViewModel.ActivePageTag = tag;
        ContentFrame.Navigate(targetType, parameter, new EntranceNavigationTransitionInfo());
    }

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        var windowId = Win32Interop.GetWindowIdFromWindow(WindowHandle);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Title = "Voltrex Quest";
        appWindow.Resize(new SizeInt32(1260, 800));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        if (File.Exists(iconPath))
        {
            appWindow.SetIcon(iconPath);
        }

        if (appWindow.TitleBar is not null)
        {
            appWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
            appWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        }
    }

    private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            NavigateToPage("settings");
            return;
        }

        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            NavigateToPage(tag);
        }
    }

    private void NavView_BackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
    {
        if (ContentFrame.CanGoBack)
        {
            ContentFrame.GoBack();
        }
    }

    private void ContentFrame_Navigated(object sender, NavigationEventArgs e)
    {
        if (e.SourcePageType == typeof(SettingsPage))
        {
            NavView.SelectedItem = NavView.SettingsItem;
            ViewModel.ActivePageTag = "settings";
            return;
        }

        var tag = e.SourcePageType switch
        {
            var t when t == typeof(HomePage) => "home",
            var t when t == typeof(DiscoverPage) => "discover",
            var t when t == typeof(LibraryPage) => "library",
            _ => "home"
        };

        ViewModel.ActivePageTag = tag;
        var matchingItem = NavView.MenuItems
            .OfType<NavigationViewItem>()
            .FirstOrDefault(item => string.Equals(item.Tag as string, tag, StringComparison.Ordinal));

        if (matchingItem is not null && !Equals(NavView.SelectedItem, matchingItem))
        {
            NavView.SelectedItem = matchingItem;
        }
    }

    private void ShellSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        var query = args.QueryText?.Trim() ?? string.Empty;

        if (ContentFrame.Content is LibraryPage libraryPage)
        {
            libraryPage.ViewModel.SearchQuery = query;
            return;
        }

        if (ContentFrame.Content is DiscoverPage discoverPage)
        {
            discoverPage.ViewModel.ApplyExternalSearch(query);
            return;
        }

        NavigateToPage("discover", query);
    }
}
