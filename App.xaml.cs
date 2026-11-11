using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using voltrex_quest.Services;

namespace voltrex_quest;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        AppServices.Current.UiDispatcher = DispatcherQueue.GetForCurrentThread();
        await AppServices.Current.InitializeAsync();

        _window = new MainWindow();
        _window.Activate();
    }
}
