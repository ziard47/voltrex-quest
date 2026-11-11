using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using voltrex_quest.Models;
using voltrex_quest.Services;

namespace voltrex_quest.Views;

/// <summary>
/// Modal dialog that checks GitHub Releases, renders release notes, and installs updates via the Inno Setup installer.
/// </summary>
public sealed partial class UpdateDialog : ContentDialog
{
    private enum DialogPhase
    {
        Checking,
        UpdateAvailableWithInstaller,
        UpdateAvailableNoAsset,
        UpToDate,
        Downloading,
        Error
    }

    private readonly UpdateService _updateService;
    private CancellationTokenSource? _cts;
    private DialogPhase _phase = DialogPhase.Checking;
    private UpdateCheckResult? _latestResult;

    public UpdateDialog() : this(AppServices.Current.Updates)
    {
    }

    public UpdateDialog(UpdateService updateService)
    {
        _updateService = updateService;
        InitializeComponent();
        CheckingSubtitleText.Text = $"Installed: {_updateService.CurrentVersionDisplay}";
    }

    private async void UpdateDialog_Loaded(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync();
    }

    private void UpdateDialog_Closed(ContentDialog sender, ContentDialogClosedEventArgs args)
    {
        CancelOngoingOperation();
    }

    private async void UpdateDialog_PrimaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        switch (_phase)
        {
            case DialogPhase.UpdateAvailableWithInstaller:
                if (_latestResult?.InstallerAsset is { } asset)
                {
                    args.Cancel = true;
                    await DownloadAndInstallAsync(asset);
                }
                break;

            case DialogPhase.UpdateAvailableNoAsset:
                OpenExternalUrl(_latestResult?.ReleaseUrl ?? UpdateService.ReleasesPageUrl);
                break;

            case DialogPhase.Error:
                args.Cancel = true;
                await CheckForUpdatesAsync();
                break;
        }
    }

    private async void UpdateDialog_SecondaryButtonClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        switch (_phase)
        {
            case DialogPhase.UpdateAvailableWithInstaller:
            case DialogPhase.Error:
                args.Cancel = true;
                OpenExternalUrl(_latestResult?.ReleaseUrl ?? UpdateService.ReleasesPageUrl);
                break;

            case DialogPhase.UpToDate:
                args.Cancel = true;
                await CheckForUpdatesAsync();
                break;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        SetPhase(DialogPhase.Checking);
        CancelOngoingOperation();
        _cts = new CancellationTokenSource();

        try
        {
            var result = await _updateService.CheckForUpdatesAsync(_cts.Token);
            _latestResult = result;

            if (!result.HasPublishedRelease)
            {
                ShowNoPublishedReleases(result);
                return;
            }

            PopulateReleaseDetails(result);

            if (result.IsUpdateAvailable)
            {
                if (result.InstallerAsset is not null)
                {
                    StatusBanner.Severity = InfoBarSeverity.Informational;
                    StatusBanner.Title = "Update available";
                    StatusBanner.Message = $"A newer version ({result.LatestVersionText}) is ready to download and install.";
                    InstallerAssetText.Text = $"Installer: {result.InstallerAsset.Name} ({result.InstallerAsset.FormattedSize})";
                    SetPhase(DialogPhase.UpdateAvailableWithInstaller);
                }
                else
                {
                    StatusBanner.Severity = InfoBarSeverity.Warning;
                    StatusBanner.Title = "Update available";
                    StatusBanner.Message = $"Version {result.LatestVersionText} is available on GitHub, but no Windows installer asset (.exe) was attached.";
                    InstallerAssetText.Text = "Open the release page on GitHub to download files manually.";
                    SetPhase(DialogPhase.UpdateAvailableNoAsset);
                }
            }
            else
            {
                StatusBanner.Severity = InfoBarSeverity.Success;
                StatusBanner.Title = "You're up to date";
                StatusBanner.Message = $"Voltrex Quest {result.CurrentVersionText} matches the latest release ({result.LatestVersionText}).";
                InstallerAssetText.Text = result.InstallerAsset is not null
                    ? $"Latest release asset: {result.InstallerAsset.Name} ({result.InstallerAsset.FormattedSize})"
                    : string.Empty;
                SetPhase(DialogPhase.UpToDate);
            }
        }
        catch (OperationCanceledException) when (_cts is null || _cts.IsCancellationRequested)
        {
            // User cancelled check or closed modal.
        }
        catch (OperationCanceledException)
        {
            ShowError("The request to GitHub Releases timed out. Please check your network connection and try again.");
        }
        catch (HttpRequestException ex)
        {
            ShowError($"Unable to reach GitHub Releases. Check your internet connection and try again. ({ex.Message})");
        }
        catch (Exception ex)
        {
            ShowError(ex.Message);
        }
    }

    private async Task DownloadAndInstallAsync(UpdateAssetInfo asset)
    {
        SetPhase(DialogPhase.Downloading);
        DownloadStatusText.Text = $"Downloading {asset.Name}...";
        DownloadProgressBar.IsIndeterminate = asset.SizeBytes <= 0;
        DownloadProgressBar.Value = 0;

        CancelOngoingOperation();
        _cts = new CancellationTokenSource();

        var progress = new Progress<UpdateDownloadProgress>(p =>
        {
            if (p.TotalBytes > 0)
            {
                DownloadProgressBar.IsIndeterminate = false;
                DownloadProgressBar.Value = p.Percentage;
                DownloadStatusText.Text =
                    $"Downloading {asset.Name}... {p.Percentage:0}% ({UpdateAssetInfo.FormatBytes(p.BytesDownloaded)} / {UpdateAssetInfo.FormatBytes(p.TotalBytes)})";
            }
            else
            {
                DownloadProgressBar.IsIndeterminate = true;
                DownloadStatusText.Text = $"Downloading {asset.Name}... {UpdateAssetInfo.FormatBytes(p.BytesDownloaded)}";
            }
        });

        try
        {
            var installerPath = await _updateService.DownloadInstallerAsync(asset, progress, _cts.Token);

            DownloadProgressBar.IsIndeterminate = true;
            DownloadStatusText.Text = "Launching installer...";

            if (!_updateService.LaunchInstaller(installerPath, out var launchError))
            {
                ShowError(launchError ?? "Could not launch the downloaded installer.");
                return;
            }

            Hide();
            Application.Current.Exit();
        }
        catch (OperationCanceledException)
        {
            // Download cancelled by user.
        }
        catch (Exception ex)
        {
            ShowError($"Failed to download update installer: {ex.Message}");
        }
    }

    private void PopulateReleaseDetails(UpdateCheckResult result)
    {
        ReleaseTitleText.Text = result.ReleaseName;

        var publishedText = result.PublishedAt.HasValue
            ? $"Published {result.PublishedAt.Value.ToLocalTime():MMM d, yyyy}"
            : "Published release";
        ReleaseMetaText.Text = $"{publishedText} · Installed {result.CurrentVersionText}";

        VersionBadgeText.Text = result.IsUpdateAvailable
            ? $"{result.CurrentVersionText} → {result.LatestVersionText}"
            : result.LatestVersionText;

        ReleaseNotesText.Text = result.ReleaseNotes;
    }

    private void ShowNoPublishedReleases(UpdateCheckResult result)
    {
        StatusBanner.Severity = InfoBarSeverity.Informational;
        StatusBanner.Title = "No published releases found";
        StatusBanner.Message = $"You are running {result.CurrentVersionText}. No published releases were found on {UpdateService.RepositoryOwner}/{UpdateService.RepositoryName} yet.";
        ReleaseDetailsPanel.Visibility = Visibility.Collapsed;
        SetPhase(DialogPhase.UpToDate);
    }

    private void ShowError(string message)
    {
        StatusBanner.Severity = InfoBarSeverity.Error;
        StatusBanner.Title = "Update check failed";
        StatusBanner.Message = message;
        SetPhase(DialogPhase.Error);
    }

    private void SetPhase(DialogPhase phase)
    {
        _phase = phase;

        CheckingPanel.Visibility = phase == DialogPhase.Checking ? Visibility.Visible : Visibility.Collapsed;
        StatusBanner.Visibility = phase == DialogPhase.Checking ? Visibility.Collapsed : Visibility.Visible;
        StatusBanner.IsOpen = phase != DialogPhase.Checking;

        var showReleaseDetails = phase == DialogPhase.UpdateAvailableWithInstaller
            || phase == DialogPhase.UpdateAvailableNoAsset
            || (phase == DialogPhase.UpToDate && _latestResult?.HasPublishedRelease == true);
        ReleaseDetailsPanel.Visibility = showReleaseDetails ? Visibility.Visible : Visibility.Collapsed;

        DownloadPanel.Visibility = phase == DialogPhase.Downloading ? Visibility.Visible : Visibility.Collapsed;

        switch (phase)
        {
            case DialogPhase.Checking:
                PrimaryButtonText = string.Empty;
                SecondaryButtonText = string.Empty;
                CloseButtonText = "Cancel";
                DefaultButton = ContentDialogButton.Close;
                break;

            case DialogPhase.UpdateAvailableWithInstaller:
                PrimaryButtonText = "Update & Install";
                SecondaryButtonText = "Release Page";
                CloseButtonText = "Later";
                DefaultButton = ContentDialogButton.Primary;
                break;

            case DialogPhase.UpdateAvailableNoAsset:
                PrimaryButtonText = "Open Release Page";
                SecondaryButtonText = string.Empty;
                CloseButtonText = "Close";
                DefaultButton = ContentDialogButton.Primary;
                break;

            case DialogPhase.UpToDate:
                PrimaryButtonText = string.Empty;
                SecondaryButtonText = "Check Again";
                CloseButtonText = "Close";
                DefaultButton = ContentDialogButton.Close;
                break;

            case DialogPhase.Downloading:
                PrimaryButtonText = string.Empty;
                SecondaryButtonText = string.Empty;
                CloseButtonText = "Cancel Download";
                DefaultButton = ContentDialogButton.Close;
                break;

            case DialogPhase.Error:
                PrimaryButtonText = "Retry";
                SecondaryButtonText = "Open Releases";
                CloseButtonText = "Close";
                DefaultButton = ContentDialogButton.Primary;
                break;
        }
    }

    private void CancelOngoingOperation()
    {
        try
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
        catch
        {
            // Ignore disposal races.
        }
        finally
        {
            _cts = null;
        }
    }

    private void OpenExternalUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            ShowError($"Could not open browser: {ex.Message}");
        }
    }
}
