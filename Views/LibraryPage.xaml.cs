using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using voltrex_quest.Helpers;
using voltrex_quest.Models;
using voltrex_quest.Services;
using voltrex_quest.ViewModels;

namespace voltrex_quest.Views;

public sealed partial class LibraryPage : Page
{
    public LibraryViewModel ViewModel { get; } = new();

    public LibraryPage()
    {
        InitializeComponent();
        ViewModel.NavigateToDiscoverRequested += () => MainWindow.Instance?.NavigateToPage("discover");
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string externalSearch && !string.IsNullOrWhiteSpace(externalSearch))
        {
            ViewModel.SearchQuery = externalSearch;
        }

        await ViewModel.LoadAsync();
        ResponsiveGridHelper.ScheduleFourColumnGridUpdate(LibraryGridView);
    }

    private void LibraryGridView_Loaded(object sender, RoutedEventArgs e)
    {
        ResponsiveGridHelper.ScheduleFourColumnGridUpdate(LibraryGridView);
    }

    private void LibraryGridView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        ResponsiveGridHelper.UpdateFourColumnGrid(LibraryGridView);
    }

    private void LibraryGridView_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        ResponsiveGridHelper.ScheduleFourColumnGridUpdate(LibraryGridView);
    }

    private async void PlayButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LibraryGame game })
        {
            return;
        }

        var outcome = await GameLaunchCoordinator.PlayGameAsync(game, XamlRoot);
        if (!string.IsNullOrWhiteSpace(outcome.Message))
        {
            LibraryInfoBar.Severity = outcome.IsError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
            ViewModel.ShowInfo(outcome.Message, outcome.IsError);
        }
    }

    private async void ConfigureExeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LibraryGame game })
        {
            return;
        }

        var path = await GameLaunchCoordinator.ConfigureLaunchAsync(game, XamlRoot);
        if (!string.IsNullOrWhiteSpace(path))
        {
            LibraryInfoBar.Severity = InfoBarSeverity.Success;
            ViewModel.ShowInfo($"Launch settings updated for {game.Name}: {path}");
        }
    }

    private async void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: LibraryGame game })
        {
            return;
        }

        if (AppServices.Current.Settings.Current.ConfirmBeforeRemove)
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = "Remove from My Games?",
                Content = $"Remove \"{game.Name}\" from your personal library? You can add it again anytime from Discover Games.",
                PrimaryButtonText = "Remove",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }
        }

        var removed = await AppServices.Current.Library.RemoveGameAsync(game.Id);
        if (removed)
        {
            LibraryInfoBar.Severity = InfoBarSeverity.Informational;
            ViewModel.ShowInfo($"Removed {game.Name} from My Games.");
        }
    }

    private void LibraryInfoBar_CloseButtonClick(InfoBar sender, object args)
    {
        ViewModel.DismissStatusCommand.Execute(null);
    }
}

