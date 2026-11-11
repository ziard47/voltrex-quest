using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Threading.Tasks;
using voltrex_quest.Models;
using voltrex_quest.Services;
using voltrex_quest.Views;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace voltrex_quest.Helpers;

public sealed record LaunchOutcome(bool Launched, string Message, bool IsError = false);

/// <summary>
/// Coordinates launch configuration, genuine process launching, and opening the SessionWindow.
/// </summary>
public static class GameLaunchCoordinator
{
    public static async Task<LaunchOutcome> PlayGameAsync(LibraryGame game, XamlRoot xamlRoot)
    {
        var services = AppServices.Current;

        var active = services.Launcher.GetActiveSessionForGame(game.Id);
        if (active is not null)
        {
            SessionWindow.ShowOrActivate(active);
            return new LaunchOutcome(true, $"Opened active session monitor for {game.Name} (PID {active.ProcessIdDisplay}).");
        }

        if (!string.IsNullOrWhiteSpace(game.CustomExecutablePath))
        {
            var result = await services.Launcher.LaunchDirectExecutableAsync(game, game.CustomExecutablePath);
            return await HandleLaunchResultAsync(game, result, xamlRoot);
        }

        if (!services.Settings.Current.PromptForExecutableOnPlay && !string.IsNullOrWhiteSpace(game.SteamAppId))
        {
            var result = await services.Launcher.LaunchViaSteamUriAsync(game, game.SteamAppId);
            return await HandleLaunchResultAsync(game, result, xamlRoot);
        }

        var hasSteam = !string.IsNullOrWhiteSpace(game.SteamAppId);
        var useSteamInstead = false;

        var dialogBody = new StackPanel { Spacing = 10 };
        dialogBody.Children.Add(new TextBlock
        {
            Text = $"Expected binary: {game.ExpectedExecutableName}\n\nClick Launch to open a dummy game window running as {game.ExpectedExecutableName}, or browse for an existing .exe on your PC.",
            TextWrapping = TextWrapping.Wrap
        });

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Title = $"Launch {game.Name}",
            Content = dialogBody,
            PrimaryButtonText = "Launch",
            SecondaryButtonText = "Browse .exe...",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (hasSteam)
        {
            var steamButton = new Button
            {
                Content = "Launch via Steam instead"
            };
            steamButton.Click += (_, _) =>
            {
                useSteamInstead = true;
                dialog.Hide();
            };
            dialogBody.Children.Add(steamButton);
        }

        var choice = await dialog.ShowAsync();
        if (useSteamInstead && hasSteam && game.SteamAppId is not null)
        {
            var steamResult = await services.Launcher.LaunchViaSteamUriAsync(game, game.SteamAppId);
            return await HandleLaunchResultAsync(game, steamResult, xamlRoot);
        }

        if (choice == ContentDialogResult.Primary)
        {
            var result = await services.Launcher.LaunchDummyGameAsync(game);
            return await HandleLaunchResultAsync(game, result, xamlRoot);
        }

        if (choice == ContentDialogResult.Secondary)
        {
            var pickedPath = await PickExecutableAsync();
            if (string.IsNullOrWhiteSpace(pickedPath))
            {
                return new LaunchOutcome(false, string.Empty);
            }

            await services.Library.UpdateExecutablePathAsync(game.Id, pickedPath);
            var result = await services.Launcher.LaunchDirectExecutableAsync(game, pickedPath);
            return await HandleLaunchResultAsync(game, result, xamlRoot);
        }

        return new LaunchOutcome(false, string.Empty);
    }

    public static async Task<string?> ConfigureLaunchAsync(LibraryGame game, XamlRoot xamlRoot)
    {
        var pathBox = new TextBox
        {
            Header = "Executable Path (.exe)",
            Text = game.CustomExecutablePath ?? string.Empty,
            PlaceholderText = game.ExpectedExecutableName
        };

        var argsBox = new TextBox
        {
            Header = "Launch Arguments (optional)",
            Text = game.LaunchArguments ?? string.Empty,
            PlaceholderText = "e.g. -windowed -dx11"
        };

        var browseBtn = new Button
        {
            Content = "Browse .exe..."
        };
        browseBtn.Click += async (_, _) =>
        {
            var picked = await PickExecutableAsync();
            if (!string.IsNullOrWhiteSpace(picked))
            {
                pathBox.Text = picked;
            }
        };

        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(pathBox);
        panel.Children.Add(browseBtn);
        panel.Children.Add(argsBox);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            RequestedTheme = ElementTheme.Dark,
            Title = $"Launch Settings — {game.Name}",
            Content = panel,
            PrimaryButtonText = "Save",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary)
        {
            return null;
        }

        var cleanedPath = string.IsNullOrWhiteSpace(pathBox.Text) ? null : pathBox.Text.Trim();
        var cleanedArgs = argsBox.Text?.Trim() ?? string.Empty;

        await AppServices.Current.Library.UpdateExecutablePathAsync(game.Id, cleanedPath, cleanedArgs);
        return cleanedPath ?? "(cleared executable path)";
    }

    private static async Task<LaunchOutcome> HandleLaunchResultAsync(
        LibraryGame game,
        LaunchStartResult result,
        XamlRoot xamlRoot)
    {
        if (result.Success && result.Session is not null)
        {
            SessionWindow.ShowOrActivate(result.Session);
            return new LaunchOutcome(true, $"Started session for {game.Name}.");
        }

        if (result.ErrorKind == LaunchErrorKind.AlreadyTrackedSession && result.Session is not null)
        {
            SessionWindow.ShowOrActivate(result.Session);
            return new LaunchOutcome(true, result.Message);
        }

        if (result.ErrorKind == LaunchErrorKind.AlreadyRunning && result.ExistingProcessId.HasValue)
        {
            var attachDialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark,
                Title = $"{game.Name} is already running",
                Content = $"{result.Message}\n\nYou can attach the session monitor to this running process instead of launching a second instance.",
                PrimaryButtonText = "Monitor Running Process",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await attachDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var attachResult = await AppServices.Current.Launcher.AttachToRunningProcessAsync(game, result.ExistingProcessId.Value);
                if (attachResult.Success && attachResult.Session is not null)
                {
                    SessionWindow.ShowOrActivate(attachResult.Session);
                    return new LaunchOutcome(true, $"Monitoring running process for {game.Name} (PID {result.ExistingProcessId.Value}).");
                }

                return new LaunchOutcome(false, attachResult.Message, IsError: true);
            }

            return new LaunchOutcome(false, result.Message, IsError: false);
        }

        if (result.ErrorKind is LaunchErrorKind.ExecutableNotFound or LaunchErrorKind.InvalidPath)
        {
            var fixDialog = new ContentDialog
            {
                XamlRoot = xamlRoot,
                RequestedTheme = ElementTheme.Dark,
                Title = "Executable Configuration Required",
                Content = $"{result.Message}\n\nWould you like to select the game's .exe file now?",
                PrimaryButtonText = "Browse .exe...",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary
            };

            if (await fixDialog.ShowAsync() == ContentDialogResult.Primary)
            {
                var picked = await PickExecutableAsync();
                if (!string.IsNullOrWhiteSpace(picked))
                {
                    await AppServices.Current.Library.UpdateExecutablePathAsync(game.Id, picked);
                    var retry = await AppServices.Current.Launcher.LaunchDirectExecutableAsync(game, picked);
                    return await HandleLaunchResultAsync(game, retry, xamlRoot);
                }
            }
        }

        if (result.Session is not null)
        {
            SessionWindow.ShowOrActivate(result.Session);
        }

        return new LaunchOutcome(false, result.Message, IsError: true);
    }

    private static async Task<string?> PickExecutableAsync()
    {
        if (MainWindow.WindowHandle == IntPtr.Zero)
        {
            return null;
        }

        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.ComputerFolder,
            ViewMode = PickerViewMode.List
        };
        picker.FileTypeFilter.Add(".exe");

        InitializeWithWindow.Initialize(picker, MainWindow.WindowHandle);
        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }
}
