using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using voltrex_quest.Models;
using voltrex_quest.ViewModels;
using Windows.Graphics;
using WinRT.Interop;

namespace voltrex_quest.Views;

/// <summary>
/// Companion WinUI 3 window for monitoring a genuine launched game process.
/// </summary>
public sealed partial class SessionWindow : Window
{
    private static readonly Dictionary<Guid, SessionWindow> OpenWindows = [];

    public SessionViewModel ViewModel { get; }

    public SessionWindow(GameSession session)
    {
        InitializeComponent();
        ViewModel = new SessionViewModel(session);
        ViewModel.PropertyChanged += ViewModel_PropertyChanged;

        ConfigureWindow();
        UpdateInfoBarSeverity();

        OpenWindows[session.SessionId] = this;
        Closed += SessionWindow_Closed;
    }

    public static SessionWindow ShowOrActivate(GameSession session)
    {
        if (OpenWindows.TryGetValue(session.SessionId, out var existing))
        {
            existing.Activate();
            return existing;
        }

        var window = new SessionWindow(session);
        window.Activate();
        return window;
    }

    private void ConfigureWindow()
    {
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(SessionTitleBar);

        var hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(hwnd);
        var appWindow = AppWindow.GetFromWindowId(windowId);
        appWindow.Title = $"{ViewModel.Session.GameName} — Session Monitor";
        appWindow.Resize(new SizeInt32(720, 620));

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

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SessionViewModel.IsErrorState) or nameof(SessionViewModel.StatusText))
        {
            UpdateInfoBarSeverity();
        }
    }

    private void UpdateInfoBarSeverity()
    {
        SessionDetailBar.Severity = ViewModel.Session.Status switch
        {
            GameSessionStatus.Failed => InfoBarSeverity.Error,
            GameSessionStatus.Running => InfoBarSeverity.Success,
            _ => InfoBarSeverity.Informational
        };
    }

    private async void StopSessionButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.CanStop)
        {
            return;
        }

        var pidText = ViewModel.Session.ProcessId.HasValue
            ? $" (PID {ViewModel.Session.ProcessId.Value})"
            : string.Empty;

        var dialog = new ContentDialog
        {
            XamlRoot = RootGrid.XamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Title = $"Stop {ViewModel.Session.GameName}?",
            Content = $"This will terminate the game process{pidText} owned by this session and return you to My Games. Any unsaved progress in the game may be lost.",
            PrimaryButtonText = "Stop Game",
            CloseButtonText = "Keep Running",
            DefaultButton = ContentDialogButton.Close
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return;
        }

        await ViewModel.StopSessionAsync();

        // Return user cleanly to My Games in the main window
        if (MainWindow.Instance is not null)
        {
            MainWindow.Instance.NavigateToPage("library");
            MainWindow.Instance.Activate();
        }
    }

    private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void SessionWindow_Closed(object sender, WindowEventArgs args)
    {
        // Cleanly release UI subscriptions without terminating the running game process
        Closed -= SessionWindow_Closed;
        ViewModel.PropertyChanged -= ViewModel_PropertyChanged;
        OpenWindows.Remove(ViewModel.Session.SessionId);
        ViewModel.Dispose();

        if (MainWindow.Instance is not null)
        {
            MainWindow.Instance.Activate();
        }
    }
}

