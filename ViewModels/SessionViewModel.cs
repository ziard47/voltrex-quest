using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using System;
using System.ComponentModel;
using System.Threading.Tasks;
using voltrex_quest.Helpers;
using voltrex_quest.Models;
using voltrex_quest.Services;
using Windows.UI;

namespace voltrex_quest.ViewModels;

public sealed class SessionViewModel : ObservableObject, IDisposable
{
    private readonly AppServices _services;
    private bool _isStopping;
    private bool _disposed;

    public SessionViewModel(GameSession session) : this(session, AppServices.Current)
    {
    }

    public SessionViewModel(GameSession session, AppServices services)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        _services = services;

        Session.PropertyChanged += OnSessionPropertyChanged;
        _services.Launcher.SessionStateChanged += OnLauncherSessionStateChanged;
    }

    public GameSession Session { get; }

    public bool IsStopping
    {
        get => _isStopping;
        private set
        {
            if (SetProperty(ref _isStopping, value))
            {
                OnPropertyChanged(nameof(CanStop));
                OnPropertyChanged(nameof(StopButtonText));
            }
        }
    }

    public bool CanStop => Session.IsActive && !IsStopping;
    public string StopButtonText => IsStopping ? "Stopping..." : "Stop Session";

    public string StatusText => Session.StatusDisplay;
    public string ProcessIdText => Session.ProcessIdDisplay;
    public string ProcessNameText => Session.ProcessNameDisplay;
    public string StartedAtText => Session.StartedAtDisplay;
    public string ElapsedText => Session.DurationClockDisplay;
    public string OutcomeText => Session.Outcome;
    public string? StatusDetailText => Session.StatusDetail;
    public Visibility StatusDetailVisibility => Session.HasStatusDetail ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OutcomeRowVisibility => Session.IsTerminal ? Visibility.Visible : Visibility.Collapsed;

    public SolidColorBrush StatusBadgeBackground => Session.Status switch
    {
        GameSessionStatus.Running => new SolidColorBrush(Color.FromArgb(255, 22, 68, 42)),
        GameSessionStatus.Launching or GameSessionStatus.Initializing => new SolidColorBrush(Color.FromArgb(255, 34, 58, 88)),
        GameSessionStatus.Failed => new SolidColorBrush(Color.FromArgb(255, 92, 32, 34)),
        _ => new SolidColorBrush(Color.FromArgb(255, 48, 52, 60))
    };

    public SolidColorBrush StatusBadgeForeground => Session.Status switch
    {
        GameSessionStatus.Running => new SolidColorBrush(Color.FromArgb(255, 134, 239, 172)),
        GameSessionStatus.Launching or GameSessionStatus.Initializing => new SolidColorBrush(Color.FromArgb(255, 147, 197, 253)),
        GameSessionStatus.Failed => new SolidColorBrush(Color.FromArgb(255, 252, 165, 165)),
        _ => new SolidColorBrush(Colors.LightGray)
    };

    public bool IsErrorState => Session.Status == GameSessionStatus.Failed;

    public async Task StopSessionAsync()
    {
        if (!CanStop)
        {
            return;
        }

        IsStopping = true;
        try
        {
            await _services.Launcher.StopSessionAsync(Session.SessionId);
        }
        finally
        {
            IsStopping = false;
        }
    }

    private void OnSessionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        _services.RunOnUiThread(() =>
        {
            switch (e.PropertyName)
            {
                case nameof(GameSession.Status):
                    OnPropertyChanged(nameof(StatusText));
                    OnPropertyChanged(nameof(StatusBadgeBackground));
                    OnPropertyChanged(nameof(StatusBadgeForeground));
                    OnPropertyChanged(nameof(IsErrorState));
                    OnPropertyChanged(nameof(OutcomeRowVisibility));
                    OnPropertyChanged(nameof(CanStop));
                    break;
                case nameof(GameSession.IsActive):
                    OnPropertyChanged(nameof(CanStop));
                    OnPropertyChanged(nameof(OutcomeRowVisibility));
                    break;
                case nameof(GameSession.ProcessId):
                    OnPropertyChanged(nameof(ProcessIdText));
                    break;
                case nameof(GameSession.ProcessName):
                    OnPropertyChanged(nameof(ProcessNameText));
                    break;
                case nameof(GameSession.Duration):
                case nameof(GameSession.DurationClockDisplay):
                    OnPropertyChanged(nameof(ElapsedText));
                    break;
                case nameof(GameSession.Outcome):
                    OnPropertyChanged(nameof(OutcomeText));
                    break;
                case nameof(GameSession.StatusDetail):
                    OnPropertyChanged(nameof(StatusDetailText));
                    OnPropertyChanged(nameof(StatusDetailVisibility));
                    break;
            }
        });
    }

    private void OnLauncherSessionStateChanged(object? sender, GameSession updated)
    {
        if (updated.SessionId != Session.SessionId)
        {
            return;
        }

        _services.RunOnUiThread(() =>
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBadgeBackground));
            OnPropertyChanged(nameof(StatusBadgeForeground));
            OnPropertyChanged(nameof(IsErrorState));
            OnPropertyChanged(nameof(CanStop));
            OnPropertyChanged(nameof(ProcessIdText));
            OnPropertyChanged(nameof(ProcessNameText));
            OnPropertyChanged(nameof(ElapsedText));
            OnPropertyChanged(nameof(OutcomeText));
            OnPropertyChanged(nameof(StatusDetailText));
            OnPropertyChanged(nameof(StatusDetailVisibility));
            OnPropertyChanged(nameof(OutcomeRowVisibility));
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Session.PropertyChanged -= OnSessionPropertyChanged;
        _services.Launcher.SessionStateChanged -= OnLauncherSessionStateChanged;
    }
}

