using System;
using System.Text.Json.Serialization;
using voltrex_quest.Helpers;

namespace voltrex_quest.Models;

public enum GameSessionStatus
{
    Initializing,
    Launching,
    Running,
    Stopped,
    Failed
}

/// <summary>
/// Represents a single launched game session and its persisted history entry.
/// </summary>
public sealed class GameSession : ObservableObject
{
    private GameSessionStatus _status = GameSessionStatus.Initializing;
    private DateTimeOffset? _endedAt;
    private bool _isActive;
    private int? _processId;
    private string? _processName;
    private string _outcome = "Initializing";
    private string? _statusDetail;
    private int? _exitCode;
    private TimeSpan _liveElapsed;

    public Guid SessionId { get; set; } = Guid.NewGuid();
    public string GameId { get; set; } = string.Empty;
    public string GameName { get; set; } = string.Empty;
    public string? CoverImageHash { get; set; }
    public string? IconHash { get; set; }
    public string? ExecutablePath { get; set; }
    public string LaunchMethod { get; set; } = "Executable";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;

    public GameSessionStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(IsTerminal));
            }
        }
    }

    public string Outcome
    {
        get => _outcome;
        set => SetProperty(ref _outcome, value);
    }

    public string? StatusDetail
    {
        get => _statusDetail;
        set
        {
            if (SetProperty(ref _statusDetail, value))
            {
                OnPropertyChanged(nameof(HasStatusDetail));
            }
        }
    }

    public DateTimeOffset? EndedAt
    {
        get => _endedAt;
        set
        {
            if (SetProperty(ref _endedAt, value))
            {
                OnPropertyChanged(nameof(Duration));
                OnPropertyChanged(nameof(DurationDisplay));
                OnPropertyChanged(nameof(DurationClockDisplay));
                OnPropertyChanged(nameof(EndedAtDisplay));
            }
        }
    }

    public int? ProcessId
    {
        get => _processId;
        set
        {
            if (SetProperty(ref _processId, value))
            {
                OnPropertyChanged(nameof(ProcessIdDisplay));
            }
        }
    }

    public string? ProcessName
    {
        get => _processName;
        set
        {
            if (SetProperty(ref _processName, value))
            {
                OnPropertyChanged(nameof(ProcessNameDisplay));
            }
        }
    }

    public int? ExitCode
    {
        get => _exitCode;
        set => SetProperty(ref _exitCode, value);
    }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(Duration));
                OnPropertyChanged(nameof(DurationDisplay));
                OnPropertyChanged(nameof(DurationClockDisplay));
                OnPropertyChanged(nameof(IsTerminal));
            }
        }
    }

    [JsonIgnore]
    public bool HasStatusDetail => !string.IsNullOrWhiteSpace(StatusDetail);

    [JsonIgnore]
    public bool IsTerminal => Status is GameSessionStatus.Stopped or GameSessionStatus.Failed;

    [JsonIgnore]
    public TimeSpan Duration
    {
        get
        {
            if (EndedAt.HasValue)
            {
                return EndedAt.Value > StartedAt ? EndedAt.Value - StartedAt : TimeSpan.Zero;
            }

            if (IsActive && _liveElapsed > TimeSpan.Zero)
            {
                return _liveElapsed;
            }

            var elapsed = DateTimeOffset.UtcNow - StartedAt;
            return elapsed > TimeSpan.Zero ? elapsed : TimeSpan.Zero;
        }
    }

    public void RefreshElapsedFromProcessClock()
    {
        if (IsActive && !EndedAt.HasValue)
        {
            _liveElapsed = DateTimeOffset.UtcNow - StartedAt;
            OnPropertyChanged(nameof(Duration));
            OnPropertyChanged(nameof(DurationDisplay));
            OnPropertyChanged(nameof(DurationClockDisplay));
        }
    }

    [JsonIgnore]
    public string StatusDisplay => Status.ToString();

    [JsonIgnore]
    public string ProcessIdDisplay => ProcessId.HasValue ? ProcessId.Value.ToString() : "—";

    [JsonIgnore]
    public string ProcessNameDisplay => string.IsNullOrWhiteSpace(ProcessName) ? "Pending..." : ProcessName;

    [JsonIgnore]
    public string StartedAtDisplay => StartedAt.ToLocalTime().ToString("MMM d, yyyy · HH:mm:ss");

    [JsonIgnore]
    public string StartedAtShortDisplay => StartedAt.ToLocalTime().ToString("MMM d, HH:mm");

    [JsonIgnore]
    public string EndedAtDisplay => EndedAt.HasValue ? EndedAt.Value.ToLocalTime().ToString("HH:mm:ss") : "Active";

    [JsonIgnore]
    public string DurationClockDisplay
    {
        get
        {
            var d = Duration;
            return $"{(int)d.TotalHours:D2}:{d.Minutes:D2}:{d.Seconds:D2}";
        }
    }

    [JsonIgnore]
    public string DurationDisplay
    {
        get
        {
            var d = Duration;
            if (d.TotalHours >= 1)
            {
                return $"{(int)d.TotalHours}h {d.Minutes}m";
            }

            if (d.TotalMinutes >= 1)
            {
                return $"{d.Minutes}m {d.Seconds}s";
            }

            return $"{Math.Max(0, (int)d.TotalSeconds)}s";
        }
    }

    [JsonIgnore]
    public string? CoverImageUrl =>
        !string.IsNullOrWhiteSpace(CoverImageHash) && !string.IsNullOrWhiteSpace(GameId)
            ? $"https://cdn.discordapp.com/app-icons/{GameId}/{CoverImageHash}.png?size=512"
            : IconImageUrl;

    [JsonIgnore]
    public string? IconImageUrl =>
        !string.IsNullOrWhiteSpace(IconHash) && !string.IsNullOrWhiteSpace(GameId)
            ? $"https://cdn.discordapp.com/app-icons/{GameId}/{IconHash}.png?size=256"
            : null;

    [JsonIgnore]
    public string Initials
    {
        get
        {
            if (string.IsNullOrWhiteSpace(GameName))
            {
                return "?";
            }

            var words = GameName.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 1)
            {
                return words[0].Substring(0, Math.Min(2, words[0].Length)).ToUpperInvariant();
            }

            return $"{char.ToUpperInvariant(words[0][0])}{char.ToUpperInvariant(words[1][0])}";
        }
    }
}
