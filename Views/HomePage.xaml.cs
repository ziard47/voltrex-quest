using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using voltrex_quest.Helpers;
using voltrex_quest.Models;
using voltrex_quest.ViewModels;

namespace voltrex_quest.Views;

public sealed partial class HomePage : Page
{
    public HomeViewModel ViewModel { get; } = new();

    public HomePage()
    {
        InitializeComponent();
        ViewModel.NavigateRequested += OnNavigateRequested;
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ViewModel.LoadAsync();
    }

    private void OnNavigateRequested(string pageTag)
    {
        MainWindow.Instance?.NavigateToPage(pageTag);
    }

    private async void PlayQuickButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: LibraryGame game })
        {
            var outcome = await GameLaunchCoordinator.PlayGameAsync(game, XamlRoot);
            if (!string.IsNullOrWhiteSpace(outcome.Message))
            {
                ActionInfoBar.Severity = outcome.IsError ? InfoBarSeverity.Error : InfoBarSeverity.Success;
                ActionInfoBar.Title = outcome.IsError ? "Launch failed" : "Game launched";
                ActionInfoBar.Message = outcome.Message;
                ActionInfoBar.IsOpen = true;
            }
        }
    }

    private void OpenSessionMonitorButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: GameSession session })
        {
            SessionWindow.ShowOrActivate(session);
        }
    }
}

