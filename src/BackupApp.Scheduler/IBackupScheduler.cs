namespace BackupApp.Scheduler;

public enum BackupScheduleMode
{
    Manual = 0,
    Hourly = 1,
    Daily = 2,
    OnFileSystemChange = 3
}

public sealed record ScheduleConfiguration(
    BackupScheduleMode Mode = BackupScheduleMode.Manual,
    TimeSpan DailyTime = default,
    TimeSpan ChangeDebounce = default,
    IReadOnlyList<string>? WatchPaths = null
);

public interface IBackupScheduler : IDisposable
{
    bool IsRunning { get; }
    BackupScheduleMode CurrentMode { get; }
    DateTimeOffset? NextScheduledRunUtc { get; }

    event Func<Task>? BackupTriggered;

    void Configure(ScheduleConfiguration configuration);
    void StartScheduler();
    void StopScheduler();
    void PauseScheduler();
    void ResumeScheduler();
}

public sealed class BackupScheduler : IBackupScheduler
{
    private readonly object _lock = new();
    private ScheduleConfiguration _config = new();
    private System.Threading.Timer? _scheduleTimer;
    private System.Threading.Timer? _debounceTimer;
    private readonly List<FileSystemWatcher> _watchers = [];
    private bool _isRunning;
    private bool _isPaused;
    private DateTimeOffset? _nextScheduledRunUtc;
    private bool _isTriggering;

    public bool IsRunning
    {
        get { lock (_lock) return _isRunning && !_isPaused; }
    }

    public BackupScheduleMode CurrentMode => _config.Mode;
    public DateTimeOffset? NextScheduledRunUtc => _nextScheduledRunUtc;

    public event Func<Task>? BackupTriggered;

    public void Configure(ScheduleConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        lock (_lock)
        {
            _config = configuration;
            if (_isRunning)
            {
                ApplyConfiguration();
            }
        }
    }

    public void StartScheduler()
    {
        lock (_lock)
        {
            _isRunning = true;
            _isPaused = false;
            ApplyConfiguration();
        }
    }

    public void StopScheduler()
    {
        lock (_lock)
        {
            _isRunning = false;
            _isPaused = false;
            ClearWatchersAndTimers();
        }
    }

    public void PauseScheduler()
    {
        lock (_lock)
        {
            _isPaused = true;
            ClearWatchersAndTimers();
        }
    }

    public void ResumeScheduler()
    {
        lock (_lock)
        {
            _isPaused = false;
            if (_isRunning)
            {
                ApplyConfiguration();
            }
        }
    }

    private void ApplyConfiguration()
    {
        ClearWatchersAndTimers();

        if (!_isRunning || _isPaused)
        {
            return;
        }

        switch (_config.Mode)
        {
            case BackupScheduleMode.Hourly:
                ScheduleHourly();
                break;

            case BackupScheduleMode.Daily:
                ScheduleDaily();
                break;

            case BackupScheduleMode.OnFileSystemChange:
                SetupFileSystemWatchers();
                break;

            case BackupScheduleMode.Manual:
            default:
                _nextScheduledRunUtc = null;
                break;
        }
    }

    private void ScheduleHourly()
    {
        var interval = TimeSpan.FromHours(1);
        _nextScheduledRunUtc = DateTimeOffset.UtcNow.Add(interval);
        _scheduleTimer = new System.Threading.Timer(
            async _ => await HandleScheduledTriggerAsync(BackupScheduleMode.Hourly).ConfigureAwait(false),
            null,
            interval,
            interval
        );
    }

    private void ScheduleDaily()
    {
        var now = DateTimeOffset.Now;
        var dailyTime = _config.DailyTime;
        var scheduledToday = new DateTimeOffset(now.Year, now.Month, now.Day, dailyTime.Hours, dailyTime.Minutes, 0, now.Offset);
        var nextRun = scheduledToday > now ? scheduledToday : scheduledToday.AddDays(1);
        var initialDelay = nextRun - now;

        _nextScheduledRunUtc = nextRun.ToUniversalTime();

        _scheduleTimer = new System.Threading.Timer(
            async _ => await HandleDailyTimerFiredAsync().ConfigureAwait(false),
            null,
            initialDelay,
            Timeout.InfiniteTimeSpan
        );
    }

    private async Task HandleDailyTimerFiredAsync()
    {
        await TriggerBackupSafeAsync().ConfigureAwait(false);
        lock (_lock)
        {
            if (_isRunning && !_isPaused && _config.Mode == BackupScheduleMode.Daily)
            {
                ScheduleDaily();
            }
        }
    }

    private async Task HandleScheduledTriggerAsync(BackupScheduleMode expectedMode)
    {
        lock (_lock)
        {
            if (!_isRunning || _isPaused || _config.Mode != expectedMode)
            {
                return;
            }
            if (expectedMode == BackupScheduleMode.Hourly)
            {
                _nextScheduledRunUtc = DateTimeOffset.UtcNow.AddHours(1);
            }
        }

        await TriggerBackupSafeAsync().ConfigureAwait(false);
    }

    private void SetupFileSystemWatchers()
    {
        _nextScheduledRunUtc = null;
        var paths = _config.WatchPaths;
        if (paths == null || paths.Count == 0)
        {
            return;
        }

        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
                };

                watcher.Changed += OnFileSystemChanged;
                watcher.Created += OnFileSystemChanged;
                watcher.Deleted += OnFileSystemChanged;
                watcher.Renamed += OnFileSystemRenamed;
                watcher.EnableRaisingEvents = true;

                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to watch directory '{path}': {ex.Message}");
            }
        }
    }

    private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
    {
        DebounceChangeTrigger();
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        DebounceChangeTrigger();
    }

    private void DebounceChangeTrigger()
    {
        lock (_lock)
        {
            if (!_isRunning || _isPaused || _config.Mode != BackupScheduleMode.OnFileSystemChange)
            {
                return;
            }

            var debounce = _config.ChangeDebounce > TimeSpan.Zero
                ? _config.ChangeDebounce
                : TimeSpan.FromSeconds(10);

            _debounceTimer?.Dispose();
            _debounceTimer = new System.Threading.Timer(
                async _ => await TriggerBackupSafeAsync().ConfigureAwait(false),
                null,
                debounce,
                Timeout.InfiniteTimeSpan
            );
        }
    }

    private async Task TriggerBackupSafeAsync()
    {
        lock (_lock)
        {
            if (_isTriggering)
            {
                return;
            }
            _isTriggering = true;
        }

        try
        {
            if (BackupTriggered != null)
            {
                await BackupTriggered.Invoke().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"BackupScheduler trigger error: {ex.Message}");
        }
        finally
        {
            lock (_lock)
            {
                _isTriggering = false;
            }
        }
    }

    private void ClearWatchersAndTimers()
    {
        _scheduleTimer?.Dispose();
        _scheduleTimer = null;

        _debounceTimer?.Dispose();
        _debounceTimer = null;

        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.EnableRaisingEvents = false;
                watcher.Dispose();
            }
            catch { }
        }
        _watchers.Clear();
        _nextScheduledRunUtc = null;
    }

    public void Dispose()
    {
        StopScheduler();
    }
}
