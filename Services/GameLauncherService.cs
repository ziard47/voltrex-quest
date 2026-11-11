using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using voltrex_quest.Models;

namespace voltrex_quest.Services;

public enum LaunchErrorKind
{
    None,
    InvalidPath,
    ExecutableNotFound,
    AccessDenied,
    AlreadyRunning,
    AlreadyTrackedSession,
    LaunchFailed
}

public sealed record LaunchStartResult(
    bool Success,
    GameSession? Session,
    LaunchErrorKind ErrorKind = LaunchErrorKind.None,
    string Message = "",
    int? ExistingProcessId = null,
    string? ExistingProcessName = null);

/// <summary>
/// Abstraction for extensible game launch mechanisms (local executables, store URIs, or future streaming providers).
/// </summary>
public interface IGameLaunchProvider
{
    string ProviderName { get; }
    bool CanLaunch(LibraryGame game);
}

/// <summary>
/// Launches genuine game processes, monitors their actual OS process lifecycle, and handles safe session termination.
/// </summary>
public sealed class GameLauncherService : IDisposable
{
    private sealed class ManagedSession : IDisposable
    {
        public required GameSession Session { get; init; }
        public required LibraryGame Game { get; init; }
        public Process? Process { get; set; }
        public bool IsOwnedProcess { get; set; }
        public bool StopRequested { get; set; }
        public int FinalizedFlag;
        public CancellationTokenSource MonitorCts { get; } = new();
        public Task? MonitorTask { get; set; }

        public void Dispose()
        {
            MonitorCts.Cancel();
            MonitorCts.Dispose();
            try
            {
                Process?.Dispose();
            }
            catch
            {
                // Ignore disposal issues for exited/protected handles
            }
            Process = null;
        }
    }

    private readonly GameLibraryService _libraryService;
    private readonly Action<Action> _uiDispatcher;
    private readonly ConcurrentDictionary<Guid, ManagedSession> _activeSessions = new();
    private bool _disposed;

    public GameLauncherService(GameLibraryService libraryService, Action<Action>? uiDispatcher = null)
    {
        _libraryService = libraryService ?? throw new ArgumentNullException(nameof(libraryService));
        _uiDispatcher = uiDispatcher ?? (action => action());
    }

    public event EventHandler<GameSession>? SessionStateChanged;

    public IReadOnlyCollection<GameSession> ActiveSessions =>
        _activeSessions.Values.Select(m => m.Session).ToList();

    public GameSession? GetActiveSessionForGame(string gameId) =>
        _activeSessions.Values
            .FirstOrDefault(m => string.Equals(m.Session.GameId, gameId, StringComparison.OrdinalIgnoreCase))
            ?.Session;

    public GameSession? GetSession(Guid sessionId) =>
        _activeSessions.TryGetValue(sessionId, out var managed) ? managed.Session : null;

    public (bool IsValid, LaunchErrorKind ErrorKind, string ErrorMessage) ValidateExecutablePath(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return (false, LaunchErrorKind.InvalidPath, "No executable path was provided.");
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(executablePath.Trim());
        }
        catch (Exception)
        {
            return (false, LaunchErrorKind.InvalidPath, "The configured executable path contains invalid characters or format.");
        }

        if (Directory.Exists(fullPath))
        {
            return (false, LaunchErrorKind.InvalidPath, "The selected path is a folder, not an executable file (.exe).");
        }

        if (!File.Exists(fullPath))
        {
            return (false, LaunchErrorKind.ExecutableNotFound, $"Executable not found at \"{fullPath}\". Please locate the game's .exe file again.");
        }

        if (!string.Equals(Path.GetExtension(fullPath), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return (false, LaunchErrorKind.InvalidPath, "Selected file is not a Windows executable (.exe).");
        }

        return (true, LaunchErrorKind.None, string.Empty);
    }

    public async Task<LaunchStartResult> LaunchDummyGameAsync(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        string dummyExePath;
        try
        {
            dummyExePath = PrepareDummyGameExecutable(game);
        }
        catch (Exception ex)
        {
            return new LaunchStartResult(false, null, LaunchErrorKind.LaunchFailed, $"Could not prepare dummy game executable: {ex.Message}");
        }

        var safeName = game.Name.Replace("\"", string.Empty);
        var args = $"--game \"{safeName}\"";
        return await LaunchDirectExecutableAsync(game, dummyExePath, args, useShellExecute: false).ConfigureAwait(false);
    }

    public string PrepareDummyGameExecutable(LibraryGame game)
    {
        ArgumentNullException.ThrowIfNull(game);

        var sourceDir = ResolveDummyRunnerSourceDirectory();
        var sourceExe = Path.Combine(sourceDir, "DummyGameRunner.exe");

        if (!File.Exists(sourceExe))
        {
            throw new FileNotFoundException("DummyGameRunner.exe was not found in the application output.", sourceExe);
        }

        var targetExeName = ResolveDummyExecutableFileName(game);
        var targetDir = Path.Combine(AppDataPaths.UserDataDirectory, "DummyRunner");
        Directory.CreateDirectory(targetDir);

        var targetExePath = Path.Combine(targetDir, targetExeName);
        CopyIfDifferent(sourceExe, targetExePath);
        return targetExePath;
    }

    public async Task<LaunchStartResult> LaunchDirectExecutableAsync(
        LibraryGame game,
        string executablePath,
        string? overrideArguments = null,
        bool useShellExecute = true)
    {
        ArgumentNullException.ThrowIfNull(game);

        var existingTracked = GetActiveSessionForGame(game.Id);
        if (existingTracked is not null)
        {
            return new LaunchStartResult(
                false,
                existingTracked,
                LaunchErrorKind.AlreadyTrackedSession,
                $"{game.Name} already has an active session running (PID {existingTracked.ProcessIdDisplay}).",
                existingTracked.ProcessId,
                existingTracked.ProcessName);
        }

        var validation = ValidateExecutablePath(executablePath);
        if (!validation.IsValid)
        {
            return new LaunchStartResult(false, null, validation.ErrorKind, validation.ErrorMessage);
        }

        var fullPath = Path.GetFullPath(executablePath.Trim());

        var runningExternal = FindExistingGameProcess(game, fullPath);
        if (runningExternal is not null)
        {
            var pid = runningExternal.Id;
            var name = TryGetProcessName(runningExternal) ?? Path.GetFileNameWithoutExtension(fullPath);
            runningExternal.Dispose();
            return new LaunchStartResult(
                false,
                null,
                LaunchErrorKind.AlreadyRunning,
                $"{game.Name} appears to already be running ({name}, PID {pid}).",
                pid,
                name);
        }

        var arguments = overrideArguments ?? ResolveLaunchArguments(game, fullPath);
        var session = new GameSession
        {
            GameId = game.Id,
            GameName = game.Name,
            CoverImageHash = game.CoverImageHash,
            IconHash = game.IconHash,
            ExecutablePath = fullPath,
            LaunchMethod = "Executable",
            StartedAt = DateTimeOffset.UtcNow,
            Status = GameSessionStatus.Initializing,
            Outcome = "Initializing",
            IsActive = true
        };

        await _libraryService.RecordSessionStartAsync(game, session).ConfigureAwait(false);
        NotifySessionChanged(session);

        UpdateSessionUi(session, () =>
        {
            session.Status = GameSessionStatus.Launching;
            session.Outcome = "Launching";
            session.StatusDetail = $"Starting {Path.GetFileName(fullPath)}...";
        });

        Process? process = null;
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fullPath,
                WorkingDirectory = Path.GetDirectoryName(fullPath) ?? Environment.CurrentDirectory,
                Arguments = arguments,
                UseShellExecute = useShellExecute
            };

            process = Process.Start(startInfo);
            if (process is null)
            {
                return await FailBeforeRunAsync(
                    session,
                    LaunchErrorKind.LaunchFailed,
                    "Launch failed",
                    $"Windows could not create a process for {game.Name}.").ConfigureAwait(false);
            }
        }
        catch (Win32Exception ex)
        {
            process?.Dispose();
            var (kind, message) = MapWin32LaunchError(ex, game.Name, fullPath);
            return await FailBeforeRunAsync(session, kind, "Launch failed", message).ConfigureAwait(false);
        }
        catch (UnauthorizedAccessException)
        {
            process?.Dispose();
            return await FailBeforeRunAsync(
                session,
                LaunchErrorKind.AccessDenied,
                "Access denied",
                $"Access was denied starting {game.Name}. Try running with appropriate permissions or check antivirus settings.").ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            process?.Dispose();
            return await FailBeforeRunAsync(
                session,
                LaunchErrorKind.ExecutableNotFound,
                "Executable not found",
                $"The executable \"{fullPath}\" could not be found.").ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            process?.Dispose();
            return await FailBeforeRunAsync(
                session,
                LaunchErrorKind.LaunchFailed,
                "Launch failed",
                $"Could not start {game.Name}: {ex.Message}").ConfigureAwait(false);
        }

        var pidStarted = TryGetProcessId(process);
        var procName = TryGetProcessName(process) ?? Path.GetFileNameWithoutExtension(fullPath);

        UpdateSessionUi(session, () =>
        {
            session.ProcessId = pidStarted;
            session.ProcessName = procName;
            session.Status = GameSessionStatus.Running;
            session.Outcome = "Running";
            session.StatusDetail = null;
        });

        await _libraryService.SaveSessionUpdateAsync(session).ConfigureAwait(false);
        NotifySessionChanged(session);

        var managed = new ManagedSession
        {
            Session = session,
            Game = game,
            Process = process,
            IsOwnedProcess = true
        };
        _activeSessions[session.SessionId] = managed;
        managed.MonitorTask = MonitorDirectProcessAsync(managed);

        return new LaunchStartResult(true, session);
    }

    public async Task<LaunchStartResult> AttachToRunningProcessAsync(LibraryGame game, int processId)
    {
        ArgumentNullException.ThrowIfNull(game);

        var existingTracked = GetActiveSessionForGame(game.Id);
        if (existingTracked is not null)
        {
            return new LaunchStartResult(
                false,
                existingTracked,
                LaunchErrorKind.AlreadyTrackedSession,
                $"{game.Name} is already tracked in an active session.");
        }

        Process process;
        try
        {
            process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                process.Dispose();
                return new LaunchStartResult(false, null, LaunchErrorKind.LaunchFailed, "That process has already exited.");
            }
        }
        catch (ArgumentException)
        {
            return new LaunchStartResult(false, null, LaunchErrorKind.LaunchFailed, "The running process could no longer be found.");
        }
        catch (Win32Exception)
        {
            return new LaunchStartResult(
                false,
                null,
                LaunchErrorKind.AccessDenied,
                "Access denied while attaching to the running process (it may be running elevated or protected by anti-cheat).");
        }

        var procName = TryGetProcessName(process) ?? game.Name;
        var session = new GameSession
        {
            GameId = game.Id,
            GameName = game.Name,
            CoverImageHash = game.CoverImageHash,
            IconHash = game.IconHash,
            ExecutablePath = game.CustomExecutablePath ?? procName,
            LaunchMethod = "Attached",
            StartedAt = DateTimeOffset.UtcNow,
            ProcessId = processId,
            ProcessName = procName,
            Status = GameSessionStatus.Running,
            Outcome = "Running",
            StatusDetail = "Attached to an already-running process.",
            IsActive = true
        };

        await _libraryService.RecordSessionStartAsync(game, session).ConfigureAwait(false);
        NotifySessionChanged(session);

        var managed = new ManagedSession
        {
            Session = session,
            Game = game,
            Process = process,
            IsOwnedProcess = false
        };
        _activeSessions[session.SessionId] = managed;
        managed.MonitorTask = MonitorDirectProcessAsync(managed);

        return new LaunchStartResult(true, session);
    }

    /// <summary>
    /// Launches a game via Steam URI and monitors for any matching Win32 game executable from gamelist.json.
    /// Limitation: If Steam launches an unlisted executable or requires user interaction (login/updates),
    /// process detection times out cleanly rather than faking a running state.
    /// </summary>
    public async Task<LaunchStartResult> LaunchViaSteamUriAsync(LibraryGame game, string steamAppId)
    {
        ArgumentNullException.ThrowIfNull(game);

        var existingTracked = GetActiveSessionForGame(game.Id);
        if (existingTracked is not null)
        {
            return new LaunchStartResult(
                false,
                existingTracked,
                LaunchErrorKind.AlreadyTrackedSession,
                $"{game.Name} already has an active session.");
        }

        if (string.IsNullOrWhiteSpace(steamAppId) || !long.TryParse(steamAppId, out _))
        {
            return new LaunchStartResult(false, null, LaunchErrorKind.InvalidPath, "Invalid Steam App ID for this game.");
        }

        var uri = $"steam://rungameid/{steamAppId}";
        var session = new GameSession
        {
            GameId = game.Id,
            GameName = game.Name,
            CoverImageHash = game.CoverImageHash,
            IconHash = game.IconHash,
            ExecutablePath = uri,
            LaunchMethod = "Steam",
            StartedAt = DateTimeOffset.UtcNow,
            Status = GameSessionStatus.Launching,
            Outcome = "Launching via Steam",
            StatusDetail = "Waiting for Steam to start the game process...",
            IsActive = true
        };

        await _libraryService.RecordSessionStartAsync(game, session).ConfigureAwait(false);
        NotifySessionChanged(session);

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });
        }
        catch (Win32Exception ex)
        {
            var (_, msg) = MapWin32LaunchError(ex, game.Name, uri);
            return await FailBeforeRunAsync(session, LaunchErrorKind.LaunchFailed, "Steam launch failed", msg).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await FailBeforeRunAsync(
                session,
                LaunchErrorKind.LaunchFailed,
                "Steam launch failed",
                $"Could not invoke Steam protocol: {ex.Message}").ConfigureAwait(false);
        }

        var managed = new ManagedSession
        {
            Session = session,
            Game = game,
            Process = null,
            IsOwnedProcess = false
        };
        _activeSessions[session.SessionId] = managed;
        managed.MonitorTask = MonitorSteamLaunchAsync(managed);

        return new LaunchStartResult(true, session);
    }

    public async Task StopSessionAsync(Guid sessionId)
    {
        if (!_activeSessions.TryGetValue(sessionId, out var managed))
        {
            return;
        }

        managed.StopRequested = true;

        var proc = managed.Process;
        if (proc is not null)
        {
            try
            {
                if (!proc.HasExited && managed.Session.ProcessId.HasValue && proc.Id == managed.Session.ProcessId.Value)
                {
                    if (managed.IsOwnedProcess)
                    {
                        proc.Kill(entireProcessTree: true);
                    }
                    else
                    {
                        proc.Kill();
                    }

                    using var exitWaitCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await proc.WaitForExitAsync(exitWaitCts.Token).ConfigureAwait(false);
                }
            }
            catch (Win32Exception)
            {
                UpdateSessionUi(managed.Session, () =>
                {
                    managed.Session.StatusDetail = "Process termination was denied by Windows permissions (elevated or protected process).";
                });
            }
            catch (InvalidOperationException)
            {
                // Process already exited before Kill
            }
            catch (OperationCanceledException)
            {
                // Wait timeout reached after Kill
            }
        }

        managed.MonitorCts.Cancel();

        if (managed.MonitorTask is not null)
        {
            try
            {
                await managed.MonitorTask.ConfigureAwait(false);
            }
            catch
            {
                // Monitor task already handled cancellation/cleanup
            }
        }
        else
        {
            await FinalizeSessionAsync(
                managed,
                GameSessionStatus.Stopped,
                "Stopped by user",
                null,
                null).ConfigureAwait(false);
        }
    }

    private async Task MonitorDirectProcessAsync(ManagedSession managed)
    {
        var session = managed.Session;
        var token = managed.MonitorCts.Token;

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            while (!token.IsCancellationRequested)
            {
                var proc = managed.Process;
                if (proc is null)
                {
                    break;
                }

                bool hasExited;
                try
                {
                    proc.Refresh();
                    hasExited = proc.HasExited;
                }
                catch (Win32Exception)
                {
                    await FinalizeSessionAsync(
                        managed,
                        GameSessionStatus.Failed,
                        "Process monitoring failed",
                        "Access to monitor the running game process was restricted (for example, elevated privileges or kernel anti-cheat).",
                        null).ConfigureAwait(false);
                    return;
                }
                catch (InvalidOperationException)
                {
                    await FinalizeSessionAsync(
                        managed,
                        GameSessionStatus.Failed,
                        "Process monitoring failed",
                        "Lost connection to the game process handle.",
                        null).ConfigureAwait(false);
                    return;
                }

                if (hasExited)
                {
                    int? exitCode = TryGetExitCode(proc);
                    var runDuration = DateTimeOffset.UtcNow - session.StartedAt;

                    // Account for launcher stubs that spawn a real secondary game executable and exit cleanly
                    var hasOtherCandidate = HasSecondaryExecutableCandidate(managed.Game, session.ExecutablePath);
                    if (!managed.StopRequested && runDuration.TotalSeconds < 15 && (exitCode is null or 0) && hasOtherCandidate)
                    {
                        UpdateSessionUi(session, () =>
                        {
                            session.StatusDetail = "Initial process exited; checking for secondary game process...";
                        });

                        var secondary = await WaitForSecondaryExecutableAsync(managed.Game, proc.Id, token).ConfigureAwait(false);
                        if (secondary is not null)
                        {
                            proc.Dispose();
                            managed.Process = secondary;
                            managed.IsOwnedProcess = true; // Spawned by our launched tree

                            var newPid = TryGetProcessId(secondary);
                            var newName = TryGetProcessName(secondary) ?? session.ProcessName;
                            UpdateSessionUi(session, () =>
                            {
                                session.ProcessId = newPid;
                                session.ProcessName = newName;
                                session.Status = GameSessionStatus.Running;
                                session.Outcome = "Running";
                                session.StatusDetail = $"Launcher handed off to {newName} (PID {newPid}).";
                            });

                            await _libraryService.SaveSessionUpdateAsync(session).ConfigureAwait(false);
                            NotifySessionChanged(session);
                            continue;
                        }
                    }

                    if (managed.StopRequested)
                    {
                        await FinalizeSessionAsync(managed, GameSessionStatus.Stopped, "Stopped by user", null, exitCode).ConfigureAwait(false);
                        return;
                    }

                    if (runDuration.TotalSeconds < 3)
                    {
                        var detail = exitCode.HasValue
                            ? $"{session.GameName} exited immediately after launch (exit code {exitCode.Value}). Verify the executable or launch it through its required store launcher."
                            : $"{session.GameName} exited immediately after launch without keeping a game process running.";
                        await FinalizeSessionAsync(managed, GameSessionStatus.Failed, "Exited immediately", detail, exitCode).ConfigureAwait(false);
                        return;
                    }

                    if (exitCode.HasValue && exitCode.Value != 0)
                    {
                        await FinalizeSessionAsync(
                            managed,
                            GameSessionStatus.Failed,
                            $"Exited unexpectedly (code {exitCode.Value})",
                            $"{session.GameName} terminated unexpectedly with exit code {exitCode.Value}.",
                            exitCode).ConfigureAwait(false);
                        return;
                    }

                    await FinalizeSessionAsync(managed, GameSessionStatus.Stopped, "Exited normally", null, exitCode).ConfigureAwait(false);
                    return;
                }

                UpdateSessionUi(session, session.RefreshElapsedFromProcessClock);

                if (!await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when user requests Stop
        }

        if (managed.StopRequested)
        {
            await FinalizeSessionAsync(managed, GameSessionStatus.Stopped, "Stopped by user", null, null).ConfigureAwait(false);
        }
    }

    private async Task MonitorSteamLaunchAsync(ManagedSession managed)
    {
        var session = managed.Session;
        var token = managed.MonitorCts.Token;

        try
        {
            // Poll up to 25s for Steam to start a matching Win32 game executable
            var deadline = DateTimeOffset.UtcNow.AddSeconds(25);
            using var waitTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));

            while (!token.IsCancellationRequested && DateTimeOffset.UtcNow < deadline)
            {
                var found = FindExistingGameProcess(managed.Game, null);
                if (found is not null)
                {
                    managed.Process = found;
                    managed.IsOwnedProcess = false;

                    var pid = TryGetProcessId(found);
                    var procName = TryGetProcessName(found) ?? managed.Game.ExpectedExecutableName;

                    UpdateSessionUi(session, () =>
                    {
                        session.ProcessId = pid;
                        session.ProcessName = procName;
                        session.Status = GameSessionStatus.Running;
                        session.Outcome = "Running";
                        session.StatusDetail = "Detected game process started via Steam.";
                    });

                    await _libraryService.SaveSessionUpdateAsync(session).ConfigureAwait(false);
                    NotifySessionChanged(session);

                    await MonitorDirectProcessAsync(managed).ConfigureAwait(false);
                    return;
                }

                UpdateSessionUi(session, session.RefreshElapsedFromProcessClock);
                if (!await waitTimer.WaitForNextTickAsync(token).ConfigureAwait(false))
                {
                    break;
                }
            }

            if (!managed.StopRequested)
            {
                await FinalizeSessionAsync(
                    managed,
                    GameSessionStatus.Failed,
                    "Launcher did not start game",
                    "Steam launch was requested, but no matching game process was detected within 25 seconds. Steam may require sign-in, updates, or manual launch.",
                    null).ConfigureAwait(false);
                return;
            }
        }
        catch (OperationCanceledException)
        {
            // Stop requested while waiting for Steam
        }

        await FinalizeSessionAsync(managed, GameSessionStatus.Stopped, "Stopped by user", null, null).ConfigureAwait(false);
    }

    private async Task<Process?> WaitForSecondaryExecutableAsync(LibraryGame game, int exitedPid, CancellationToken token)
    {
        for (var attempt = 0; attempt < 4; attempt++)
        {
            if (token.IsCancellationRequested)
            {
                return null;
            }

            await Task.Delay(400, token).ConfigureAwait(false);

            var candidate = FindExistingGameProcess(game, null, excludePid: exitedPid);
            if (candidate is not null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static bool HasSecondaryExecutableCandidate(LibraryGame game, string? launchedExePath)
    {
        var launchedName = string.IsNullOrWhiteSpace(launchedExePath)
            ? null
            : Path.GetFileName(launchedExePath);

        return game.Executables.Any(e =>
            string.Equals(e.Os, "win32", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(Path.GetFileName(e.Name.TrimStart('>')), launchedName, StringComparison.OrdinalIgnoreCase));
    }

    private async Task FinalizeSessionAsync(
        ManagedSession managed,
        GameSessionStatus finalStatus,
        string outcome,
        string? detail,
        int? exitCode)
    {
        if (Interlocked.Exchange(ref managed.FinalizedFlag, 1) != 0)
        {
            return;
        }

        _activeSessions.TryRemove(managed.Session.SessionId, out _);

        var session = managed.Session;
        UpdateSessionUi(session, () =>
        {
            session.IsActive = false;
            session.EndedAt = DateTimeOffset.UtcNow;
            session.Status = finalStatus;
            session.Outcome = outcome;
            session.StatusDetail = detail;
            session.ExitCode = exitCode;
        });

        managed.Dispose();

        await _libraryService.SaveSessionUpdateAsync(session).ConfigureAwait(false);
        NotifySessionChanged(session);
    }

    private async Task<LaunchStartResult> FailBeforeRunAsync(
        GameSession session,
        LaunchErrorKind errorKind,
        string outcome,
        string message)
    {
        UpdateSessionUi(session, () =>
        {
            session.IsActive = false;
            session.EndedAt = DateTimeOffset.UtcNow;
            session.Status = GameSessionStatus.Failed;
            session.Outcome = outcome;
            session.StatusDetail = message;
        });

        await _libraryService.SaveSessionUpdateAsync(session).ConfigureAwait(false);
        NotifySessionChanged(session);

        return new LaunchStartResult(false, session, errorKind, message);
    }

    private Process? FindExistingGameProcess(LibraryGame game, string? fullExePath, int? excludePid = null)
    {
        var candidateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(fullExePath))
        {
            var fileName = Path.GetFileNameWithoutExtension(fullExePath);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                candidateNames.Add(fileName);
            }
        }

        foreach (var exe in game.Executables.Where(e => string.Equals(e.Os, "win32", StringComparison.OrdinalIgnoreCase)))
        {
            var cleaned = Path.GetFileNameWithoutExtension(exe.Name.TrimStart('>'));
            if (!string.IsNullOrWhiteSpace(cleaned))
            {
                candidateNames.Add(cleaned);
            }
        }

        foreach (var name in candidateNames)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch
            {
                continue;
            }

            Process? match = null;
            foreach (var p in processes)
            {
                if (match is null && p.Id != excludePid && !TryHasExited(p))
                {
                    if (!string.IsNullOrWhiteSpace(fullExePath))
                    {
                        var modPath = TryGetMainModulePath(p);
                        if (modPath is null || string.Equals(modPath, fullExePath, StringComparison.OrdinalIgnoreCase))
                        {
                            match = p;
                            continue;
                        }
                    }
                    else
                    {
                        match = p;
                        continue;
                    }
                }

                p.Dispose();
            }

            if (match is not null)
            {
                return match;
            }
        }

        return null;
    }

    private static string ResolveDummyRunnerSourceDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "DummyRunner"),
            Path.Combine(Environment.CurrentDirectory, "DummyRunner"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "DummyRunner"))
        };

        foreach (var candidate in candidates)
        {
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "DummyGameRunner.exe")))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private static string ResolveDummyExecutableFileName(LibraryGame game)
    {
        var candidate = game.ExpectedExecutableName;
        if (string.IsNullOrWhiteSpace(candidate) ||
            string.Equals(candidate, "Executable not set", StringComparison.OrdinalIgnoreCase))
        {
            var sanitized = string.Concat(game.Name.Where(c => !Path.GetInvalidFileNameChars().Contains(c))).Trim();
            candidate = string.IsNullOrWhiteSpace(sanitized) ? "DummyGame.exe" : $"{sanitized}.exe";
        }

        var fileName = Path.GetFileName(candidate.Trim());
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            fileName = fileName.Replace(invalid, '_');
        }

        if (!fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            fileName += ".exe";
        }

        return fileName;
    }

    private static void CopyIfDifferent(string sourcePath, string destinationPath)
    {
        if (File.Exists(destinationPath))
        {
            var srcInfo = new FileInfo(sourcePath);
            var dstInfo = new FileInfo(destinationPath);
            if (srcInfo.Length == dstInfo.Length && srcInfo.LastWriteTimeUtc == dstInfo.LastWriteTimeUtc)
            {
                return;
            }
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static string ResolveLaunchArguments(LibraryGame game, string fullExePath)
    {
        if (!string.IsNullOrWhiteSpace(game.LaunchArguments))
        {
            return game.LaunchArguments;
        }

        var exeName = Path.GetFileName(fullExePath);
        var catalogMatch = game.Executables.FirstOrDefault(e =>
            string.Equals(e.Os, "win32", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(Path.GetFileName(e.Name.TrimStart('>')), exeName, StringComparison.OrdinalIgnoreCase));

        return catalogMatch?.Arguments ?? string.Empty;
    }

    private static (LaunchErrorKind Kind, string Message) MapWin32LaunchError(Win32Exception ex, string gameName, string path)
    {
        return ex.NativeErrorCode switch
        {
            2 => (LaunchErrorKind.ExecutableNotFound, $"Executable not found: \"{path}\"."),
            3 => (LaunchErrorKind.ExecutableNotFound, $"The directory for \"{path}\" could not be found."),
            5 => (LaunchErrorKind.AccessDenied, $"Access denied when launching {gameName}. Elevated permissions may be required."),
            740 => (LaunchErrorKind.AccessDenied, $"{gameName} requires administrator elevation (UAC) to launch."),
            1223 => (LaunchErrorKind.AccessDenied, $"Launch was cancelled at the Windows elevation prompt."),
            193 => (LaunchErrorKind.InvalidPath, $"\"{Path.GetFileName(path)}\" is not a valid Windows executable."),
            _ => (LaunchErrorKind.LaunchFailed, $"Unable to start {gameName}: {ex.Message}")
        };
    }

    private void UpdateSessionUi(GameSession session, Action update)
    {
        _uiDispatcher(update);
    }

    private void NotifySessionChanged(GameSession session)
    {
        _uiDispatcher(() => SessionStateChanged?.Invoke(this, session));
    }

    private static int? TryGetProcessId(Process process)
    {
        try { return process.Id; }
        catch { return null; }
    }

    private static string? TryGetProcessName(Process process)
    {
        try { return process.ProcessName; }
        catch { return null; }
    }

    private static int? TryGetExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch { return null; }
    }

    private static bool TryHasExited(Process process)
    {
        try { return process.HasExited; }
        catch { return false; }
    }

    private static string? TryGetMainModulePath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch { return null; }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var managed in _activeSessions.Values)
        {
            managed.Dispose();
        }

        _activeSessions.Clear();
    }
}

