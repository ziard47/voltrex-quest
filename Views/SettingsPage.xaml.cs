using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using voltrex_quest.ViewModels;

namespace voltrex_quest.Views;

public sealed partial class SettingsPage : Page
{
    public SettingsViewModel ViewModel { get; } = new();

    public SettingsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.RefreshStats();
    }

    private async void ClearHistoryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Clear play history?",
            Content = "This clears recorded launch sessions and resets play counts in My Games. Your saved games will remain in your library.",
            PrimaryButtonText = "Clear History",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ClearPlayHistoryAsync();
        }
    }

    private async void ClearLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = "Clear personal library?",
            Content = "This removes all games and session history from your personal library. The bundled gamelist.json catalog is never modified.",
            PrimaryButtonText = "Clear Library",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            await ViewModel.ClearLibraryAsync();
        }
    }

    private async void CheckUpdatesButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new UpdateDialog
        {
            XamlRoot = XamlRoot
        };

        await dialog.ShowAsync();
    }
}

