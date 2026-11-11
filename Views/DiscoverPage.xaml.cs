using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using System.Linq;
using voltrex_quest.Helpers;
using voltrex_quest.ViewModels;

namespace voltrex_quest.Views;

public sealed partial class DiscoverPage : Page
{
    public DiscoverViewModel ViewModel { get; } = new();

    public DiscoverPage()
    {
        InitializeComponent();
        NavigationCacheMode = NavigationCacheMode.Enabled;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string externalSearch && !string.IsNullOrWhiteSpace(externalSearch))
        {
            ViewModel.ApplyExternalSearch(externalSearch);
        }

        await ViewModel.LoadCatalogAsync();
        ResponsiveGridHelper.ScheduleFourColumnGridUpdate(CatalogGridView);
    }

    private void CatalogGridView_Loaded(object sender, RoutedEventArgs e)
    {
        ResponsiveGridHelper.ScheduleFourColumnGridUpdate(CatalogGridView);
    }

    private void CatalogGridView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ResponsiveGridHelper.UpdateFourColumnGrid(CatalogGridView);
    }

    private void CatalogGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        ResponsiveGridHelper.ScheduleFourColumnGridUpdate(CatalogGridView);
    }

    private void AddGameButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: DiscoverGameItemViewModel item })
        {
            ViewModel.AddToLibraryCommand.Execute(item);
        }
    }

    private async void DetailsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: DiscoverGameItemViewModel item })
        {
            return;
        }

        var game = item.Game;
        var winExes = game.Executables
            .Where(ex => string.Equals(ex.Os, "win32", StringComparison.OrdinalIgnoreCase))
            .Select(ex => ex.Name)
            .Take(6)
            .ToList();

        var aliasesText = game.Aliases.Count > 0 ? string.Join(", ", game.Aliases.Take(5)) : "None";
        var themesText = game.Themes.Count > 0 ? string.Join(", ", game.Themes) : "None";
        var exesText = winExes.Count > 0 ? string.Join("\n• ", winExes) : "No Win32 executables listed";

        var body = new StackPanel { Spacing = 8 };
        body.Children.Add(new TextBlock
        {
            Text = $"Discord App ID: {game.Id}",
            IsTextSelectionEnabled = true
        });
        body.Children.Add(new TextBlock
        {
            Text = $"Support: {game.SupportedStatusLabel} (Hook: {(game.Hook ? "Yes" : "No")}, Overlay: {(game.Overlay ? "Yes" : "No")})",
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = $"Distributors: {game.DistributorsSummary}",
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = $"Themes: {themesText}",
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = $"Aliases: {aliasesText}",
            TextWrapping = TextWrapping.Wrap
        });
        body.Children.Add(new TextBlock
        {
            Text = $"Windows Executables:\n• {exesText}",
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = game.Name,
            Content = new ScrollViewer
            {
                MaxHeight = 360,
                Content = body
            },
            PrimaryButtonText = item.IsInLibrary ? "In Library" : "Add to My Games",
            IsPrimaryButtonEnabled = !item.IsInLibrary,
            CloseButtonText = "Close"
        };

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && !item.IsInLibrary)
        {
            ViewModel.AddToLibraryCommand.Execute(item);
        }
    }
}

