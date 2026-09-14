using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Windows.Input;
using BackupApp.BackupEngine;
using BackupApp.Catalog;
using BackupApp.Crypto;
using BackupApp.Domain;
using BackupApp.RestoreEngine;
using BackupApp.Scheduler;
using BackupApp.Storage;
using BackupApp.Storage.Telegram;
using BackupApp.UI.Services;

namespace BackupApp.UI.ViewModels;

public enum ProtectionState
{
    NotConfigured,
    Protected,
    BackingUp,
    Restoring,
    Warning,
    Error
}

public sealed class FileItemViewModel : ViewModelBase
{
    public SnapshotManifestItem ManifestItem { get; }
    public string RelativePath { get; }
    public string FormattedPath { get; }
    public long SizeBytes { get; }
    public string FormattedSize { get; }
    public DateTimeOffset ModifiedUtc { get; }
    public string FormattedDate { get; }
    public string ContentHashSha256 => ManifestItem.ContentHashSha256;
    public int ChunkCount => ManifestItem.ChunkIds.Count;
    public string ShortHash => ManifestItem.ContentHashSha256.Length > 16 ? ManifestItem.ContentHashSha256[..16] + "..." : ManifestItem.ContentHashSha256;

    public FileItemViewModel(SnapshotManifestItem item)
    {
        ManifestItem = item;
        RelativePath = item.Path;
        FormattedPath = PathFormatter.FormatForRtl(item.Path);
        SizeBytes = item.SizeBytes;
        FormattedSize = PathFormatter.FormatBytes(item.SizeBytes);
        ModifiedUtc = item.ModifiedUtc;
        FormattedDate = item.ModifiedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
    }
}

public sealed class FileVersionItemViewModel : ViewModelBase
{
    public FileVersion Version { get; }
    public string RelativePath { get; }
    public string FormattedPath { get; }
    public string SnapshotId { get; }
    public string SnapshotLabel { get; }
    public long SizeBytes { get; }
    public string FormattedSize { get; }
    public DateTimeOffset BackupDate { get; }
    public string BackupDateFormatted { get; }
    public DateTimeOffset ModifiedDate { get; }
    public string ModifiedDateFormatted { get; }
    public string ShortHash { get; }

    public FileVersionItemViewModel(FileVersion version, int versionIndex = 0)
    {
        Version = version;
        RelativePath = version.Path.Value;
        FormattedPath = PathFormatter.FormatForRtl(version.Path.Value);
        SnapshotId = version.SnapshotId.Value.ToString("D");
        SnapshotLabel = versionIndex > 0 ? $"גרסה #{versionIndex}" : "גרסה אחרונה";
        SizeBytes = version.SizeBytes;
        FormattedSize = PathFormatter.FormatBytes(version.SizeBytes);
        BackupDate = version.CreatedAtUtc;
        BackupDateFormatted = version.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        ModifiedDate = version.ModifiedUtc;
        ModifiedDateFormatted = version.ModifiedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        ShortHash = version.ContentHashSha256.Length > 16 ? version.ContentHashSha256[..16] + "..." : version.ContentHashSha256;
    }
}

public sealed class SnapshotItemViewModel : ViewModelBase
{
    public Snapshot Snapshot { get; }
    public BackupSetId BackupSetId { get; }
    public string IdString => Snapshot.Id.ToString();
    public long SnapshotNumber => Snapshot.SnapshotNumber;
    public DateTimeOffset CreatedAtUtc => Snapshot.CreatedAtUtc;
    public string DisplayText => $"גיבוי #{Snapshot.SnapshotNumber} — {Snapshot.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture)} ({Snapshot.TotalFiles} קבצים, {PathFormatter.FormatBytes(Snapshot.TotalBytes)})";

    public SnapshotItemViewModel(Snapshot snapshot, BackupSetId backupSetId)
    {
        Snapshot = snapshot;
        BackupSetId = backupSetId;
    }
}

public sealed class MainViewModel : ViewModelBase, IDisposable
{
    private int _selectedTabIndex;
    private ProtectionState _status = ProtectionState.Protected;
    private string _statusTitle = "המערכת מוגנת ומגובה";
    private string _statusSubtitle = "כל הנתונים שמורים ומוצפנים באפס-ידע";
    private string _lastBackupText = "לא בוצע גיבוי עדיין";
    private int _totalFilesCount;
    private string _totalSizeFormatted = "0 מ\"ב";

    // Progress
    private bool _isBusy;
    private double _progressPercent;
    private string _currentProgressItem = string.Empty;
    private string _progressSummary = string.Empty;
    private string _currentSpeedFormatted = string.Empty;
    private string _etaFormatted = string.Empty;
    private CancellationTokenSource? _currentCts;

    // Search & Browse
    private string _searchQuery = string.Empty;
    private ObservableCollection<FileItemViewModel> _browsedFiles = [];
    private FileItemViewModel? _selectedFile;
    private ObservableCollection<FileVersionItemViewModel> _selectedFileVersions = [];
    private FileVersionItemViewModel? _selectedVersion;
    private bool _isVersionHistoryVisible;

    // File Preview
    private string _previewTextContent = string.Empty;
    private string? _previewImagePath;
    private bool _isImagePreview;
    private bool _isTextPreview;
    private bool _isPreviewLoading;

    // Point-in-Time Restore Snapshots
    private ObservableCollection<SnapshotItemViewModel> _availableSnapshots = [];
    private SnapshotItemViewModel? _selectedSnapshot;

    // Restore Options
    private string _restoreDestination = string.Empty;
    private RestoreConflictResolution _conflictResolution = RestoreConflictResolution.Overwrite;
    private bool _restoreTimestamps = true;
    private bool _isRestoreCompleted;
    private string _restoreStatusMessage = string.Empty;
    private bool _hasRestoreError;

    // Settings
    private ObservableCollection<string> _includedRoots = [];
    private string _newRootPath = string.Empty;
    private string? _selectedRootPath;
    private string _telegramBotToken = string.Empty;
    private string _telegramChatId = string.Empty;
    private string _settingsStatusMessage = string.Empty;

    // Scheduling
    private BackupScheduleMode _scheduleMode = BackupScheduleMode.Manual;
    private string _dailyScheduleTime = "02:00";
    private string _scheduleStatusText = "התזמון כבוי (ידני בלבד)";

    // Onboarding
    private bool _isOnboardingOpen;
    private string _masterPassword = string.Empty;
    private string _recoveryPhrase = string.Empty;
    private bool _hasSavedRecoveryPhrase;

    public int SelectedTabIndex
    {
        get => _selectedTabIndex;
        set => SetProperty(ref _selectedTabIndex, value);
    }

    public ProtectionState Status
    {
        get => _status;
        set => SetProperty(ref _status, value);
    }

    public string StatusTitle
    {
        get => _statusTitle;
        set => SetProperty(ref _statusTitle, value);
    }

    public string StatusSubtitle
    {
        get => _statusSubtitle;
        set => SetProperty(ref _statusSubtitle, value);
    }

    public string LastBackupText
    {
        get => _lastBackupText;
        set => SetProperty(ref _lastBackupText, value);
    }

    public int TotalFilesCount
    {
        get => _totalFilesCount;
        set => SetProperty(ref _totalFilesCount, value);
    }

    public string TotalSizeFormatted
    {
        get => _totalSizeFormatted;
        set => SetProperty(ref _totalSizeFormatted, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        set => SetProperty(ref _isBusy, value);
    }

    public double ProgressPercent
    {
        get => _progressPercent;
        set => SetProperty(ref _progressPercent, value);
    }

    public string CurrentProgressItem
    {
        get => _currentProgressItem;
        set => SetProperty(ref _currentProgressItem, value);
    }

    public string ProgressSummary
    {
        get => _progressSummary;
        set => SetProperty(ref _progressSummary, value);
    }

    public string CurrentSpeedFormatted
    {
        get => _currentSpeedFormatted;
        set
        {
            if (SetProperty(ref _currentSpeedFormatted, value))
            {
                OnPropertyChanged(nameof(HasSpeedOrEta));
            }
        }
    }

    public string EtaFormatted
    {
        get => _etaFormatted;
        set
        {
            if (SetProperty(ref _etaFormatted, value))
            {
                OnPropertyChanged(nameof(HasSpeedOrEta));
            }
        }
    }

    public bool HasSpeedOrEta => !string.IsNullOrWhiteSpace(_currentSpeedFormatted) || !string.IsNullOrWhiteSpace(_etaFormatted);

    public string PreviewTextContent
    {
        get => _previewTextContent;
        set => SetProperty(ref _previewTextContent, value);
    }

    public string? PreviewImagePath
    {
        get => _previewImagePath;
        set => SetProperty(ref _previewImagePath, value);
    }

    public bool IsImagePreview
    {
        get => _isImagePreview;
        set => SetProperty(ref _isImagePreview, value);
    }

    public bool IsTextPreview
    {
        get => _isTextPreview;
        set => SetProperty(ref _isTextPreview, value);
    }

    public bool IsPreviewLoading
    {
        get => _isPreviewLoading;
        set => SetProperty(ref _isPreviewLoading, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                OnPropertyChanged(nameof(IsSearchQueryEmpty));
                FilterFiles();
            }
        }
    }

    public bool IsSearchQueryEmpty => string.IsNullOrEmpty(_searchQuery);

    public ObservableCollection<FileItemViewModel> BrowsedFiles
    {
        get => _browsedFiles;
        set => SetProperty(ref _browsedFiles, value);
    }

    public FileItemViewModel? SelectedFile
    {
        get => _selectedFile;
        set
        {
            if (SetProperty(ref _selectedFile, value))
            {
                OnPropertyChanged(nameof(HasSelectedFile));
                OnPropertyChanged(nameof(HasNoSelectedFile));
                _ = UpdatePreviewForSelectedFileAsync(value);
                if (value != null && IsVersionHistoryVisible)
                {
                    _ = LoadFileVersionsAsync(value);
                }
            }
        }
    }

    public bool HasSelectedFile => _selectedFile != null;
    public bool HasNoSelectedFile => _selectedFile == null;

    public ObservableCollection<FileVersionItemViewModel> SelectedFileVersions
    {
        get => _selectedFileVersions;
        set => SetProperty(ref _selectedFileVersions, value);
    }

    public FileVersionItemViewModel? SelectedVersion
    {
        get => _selectedVersion;
        set
        {
            if (SetProperty(ref _selectedVersion, value))
            {
                OnPropertyChanged(nameof(HasSelectedVersion));
            }
        }
    }

    public bool HasSelectedVersion => _selectedVersion != null;

    public bool IsVersionHistoryVisible
    {
        get => _isVersionHistoryVisible;
        set => SetProperty(ref _isVersionHistoryVisible, value);
    }

    public ObservableCollection<SnapshotItemViewModel> AvailableSnapshots
    {
        get => _availableSnapshots;
        set => SetProperty(ref _availableSnapshots, value);
    }

    public SnapshotItemViewModel? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set
        {
            if (SetProperty(ref _selectedSnapshot, value))
            {
                OnPropertyChanged(nameof(HasSelectedSnapshot));
                if (value != null)
                {
                    _ = LoadSnapshotManifestAsync(value);
                }
            }
        }
    }

    public bool HasSelectedSnapshot => _selectedSnapshot != null;

    public string RestoreDestination
    {
        get => _restoreDestination;
        set => SetProperty(ref _restoreDestination, value);
    }

    public RestoreConflictResolution ConflictResolution
    {
        get => _conflictResolution;
        set => SetProperty(ref _conflictResolution, value);
    }

    public bool RestoreTimestamps
    {
        get => _restoreTimestamps;
        set => SetProperty(ref _restoreTimestamps, value);
    }

    public bool IsRestoreCompleted
    {
        get => _isRestoreCompleted;
        set => SetProperty(ref _isRestoreCompleted, value);
    }

    public string RestoreStatusMessage
    {
        get => _restoreStatusMessage;
        set
        {
            if (SetProperty(ref _restoreStatusMessage, value))
            {
                OnPropertyChanged(nameof(HasRestoreStatusMessage));
            }
        }
    }

    public bool HasRestoreStatusMessage => !string.IsNullOrWhiteSpace(_restoreStatusMessage);

    public bool HasRestoreError
    {
        get => _hasRestoreError;
        set => SetProperty(ref _hasRestoreError, value);
    }

    public ObservableCollection<string> IncludedRoots
    {
        get => _includedRoots;
        set => SetProperty(ref _includedRoots, value);
    }

    public string NewRootPath
    {
        get => _newRootPath;
        set => SetProperty(ref _newRootPath, value);
    }

    public string? SelectedRootPath
    {
        get => _selectedRootPath;
        set => SetProperty(ref _selectedRootPath, value);
    }

    public string TelegramBotToken
    {
        get => _telegramBotToken;
        set => SetProperty(ref _telegramBotToken, value);
    }

    public string TelegramChatId
    {
        get => _telegramChatId;
        set => SetProperty(ref _telegramChatId, value);
    }

    public string SettingsStatusMessage
    {
        get => _settingsStatusMessage;
        set => SetProperty(ref _settingsStatusMessage, value);
    }

    public bool IsOnboardingOpen
    {
        get => _isOnboardingOpen;
        set => SetProperty(ref _isOnboardingOpen, value);
    }

    public string MasterPassword
    {
        get => _masterPassword;
        set => SetProperty(ref _masterPassword, value);
    }

    public string RecoveryPhrase
    {
        get => _recoveryPhrase;
        set => SetProperty(ref _recoveryPhrase, value);
    }

    public bool HasSavedRecoveryPhrase
    {
        get => _hasSavedRecoveryPhrase;
        set => SetProperty(ref _hasSavedRecoveryPhrase, value);
    }

    // Scheduling
    public IBackupScheduler Scheduler => _scheduler;

    public BackupScheduleMode ScheduleMode
    {
        get => _scheduleMode;
        set
        {
            if (SetProperty(ref _scheduleMode, value))
            {
                OnPropertyChanged(nameof(IsScheduleModeManual));
                OnPropertyChanged(nameof(IsScheduleModeHourly));
                OnPropertyChanged(nameof(IsScheduleModeDaily));
                OnPropertyChanged(nameof(IsScheduleModeChange));
                UpdateScheduler();
            }
        }
    }

    public bool IsScheduleModeManual
    {
        get => _scheduleMode == BackupScheduleMode.Manual;
        set { if (value) ScheduleMode = BackupScheduleMode.Manual; }
    }

    public bool IsScheduleModeHourly
    {
        get => _scheduleMode == BackupScheduleMode.Hourly;
        set { if (value) ScheduleMode = BackupScheduleMode.Hourly; }
    }

    public bool IsScheduleModeDaily
    {
        get => _scheduleMode == BackupScheduleMode.Daily;
        set { if (value) ScheduleMode = BackupScheduleMode.Daily; }
    }

    public bool IsScheduleModeChange
    {
        get => _scheduleMode == BackupScheduleMode.OnFileSystemChange;
        set { if (value) ScheduleMode = BackupScheduleMode.OnFileSystemChange; }
    }

    public string DailyScheduleTime
    {
        get => _dailyScheduleTime;
        set
        {
            if (SetProperty(ref _dailyScheduleTime, value))
            {
                if (_scheduleMode == BackupScheduleMode.Daily)
                {
                    UpdateScheduler();
                }
            }
        }
    }

    public string ScheduleStatusText
    {
        get => _scheduleStatusText;
        set => SetProperty(ref _scheduleStatusText, value);
    }

    // Commands
    public ICommand TriggerBackupCommand { get; }
    public ICommand CancelOperationCommand { get; }
    public ICommand TriggerRestoreCommand { get; }
    public ICommand OpenRestoreFolderCommand { get; }
    public ICommand OpenSelectedFileCommand { get; }
    public ICommand RestoreSingleFileCommand { get; }
    public ICommand ShowFileInFolderCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand RemoveFolderCommand { get; }
    public ICommand BrowseFolderCommand { get; }
    public ICommand BrowseRestoreDestinationCommand { get; }
    public ICommand TestTelegramConnectionCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand GenerateRecoveryKeyCommand { get; }
    public ICommand CompleteOnboardingCommand { get; }
    public ICommand ShowFileVersionsCommand { get; }
    public ICommand CloseVersionHistoryCommand { get; }
    public ICommand RestoreSelectedVersionCommand { get; }

    private readonly List<FileItemViewModel> _allManifestFiles = [];
    private SnapshotManifest? _latestManifest;
    private MasterKey? _unlockedMasterKey;
    private IStorageProvider _storageProvider;
    private readonly ICatalogRepository _catalogRepository;
    private readonly IBackupOrchestrator _backupOrchestrator;
    private readonly IRestoreOrchestrator _restoreOrchestrator;
    private readonly IRemoteCatalogDiscoveryService _discoveryService;
    private readonly ICredentialStoreService _credentialStoreService;
    private readonly Func<TelegramStorageConfiguration, IStorageProvider> _storageFactory;
    private readonly IFolderPickerService _folderPickerService;
    private readonly IProcessLauncher _processLauncher;
    private readonly IBackupScheduler _scheduler;

    public IStorageProvider StorageProvider => _storageProvider;

    private static void RunOnUi(Action action)
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    private static string ResolveCatalogDbPath()
    {
        var localAppDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BackupApp");
        var localAppDataDb = Path.Combine(localAppDataDir, "catalog.sqlite");

        var baseDirDb = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalog.sqlite");

        if (File.Exists(baseDirDb))
        {
            return baseDirDb;
        }

        Directory.CreateDirectory(localAppDataDir);
        return localAppDataDb;
    }

    public MainViewModel(
        IStorageProvider? storageProvider = null,
        ICatalogRepository? catalogRepository = null,
        IBackupOrchestrator? backupOrchestrator = null,
        IRestoreOrchestrator? restoreOrchestrator = null,
        IRemoteCatalogDiscoveryService? discoveryService = null,
        ICredentialStoreService? credentialStoreService = null,
        Func<TelegramStorageConfiguration, IStorageProvider>? storageFactory = null,
        IFolderPickerService? folderPickerService = null,
        IProcessLauncher? processLauncher = null,
        IBackupScheduler? scheduler = null)
    {
        _credentialStoreService = credentialStoreService ?? new CredentialStoreService();
        _storageFactory = storageFactory ?? (config => new TelegramStorageAdapter(config));
        _storageProvider = storageProvider ?? new InMemoryStorageProvider();
        _catalogRepository = catalogRepository ?? new SqliteCatalogRepository(ResolveCatalogDbPath());
        _backupOrchestrator = backupOrchestrator ?? new BackupOrchestrator();
        _restoreOrchestrator = restoreOrchestrator ?? new RestoreOrchestrator();
        _discoveryService = discoveryService ?? new RemoteCatalogDiscoveryService();
        _folderPickerService = folderPickerService ?? new WindowsFolderPickerService();
        _processLauncher = processLauncher ?? new WindowsProcessLauncher();
        _scheduler = scheduler ?? new BackupScheduler();

        _scheduler.BackupTriggered += async () =>
        {
            if (!IsBusy)
            {
                await RunBackupAsync().ConfigureAwait(false);
            }
        };

        TriggerBackupCommand = new AsyncRelayCommand(RunBackupAsync, () => !IsBusy);
        CancelOperationCommand = new RelayCommand(CancelOperation, () => IsBusy);
        TriggerRestoreCommand = new AsyncRelayCommand(RunRestoreAsync, () => !IsBusy && _latestManifest != null);
        OpenRestoreFolderCommand = new RelayCommand(OpenRestoreFolder);
        OpenSelectedFileCommand = new AsyncRelayCommand(OpenSelectedFileAsync);
        RestoreSingleFileCommand = new AsyncRelayCommand(RestoreSingleFileAsync);
        ShowFileInFolderCommand = new RelayCommand(ShowFileInFolder);
        AddFolderCommand = new RelayCommand(AddFolder);
        RemoveFolderCommand = new RelayCommand(RemoveFolder);
        BrowseFolderCommand = new RelayCommand(BrowseFolder);
        BrowseRestoreDestinationCommand = new RelayCommand(BrowseRestoreDestination);
        TestTelegramConnectionCommand = new AsyncRelayCommand(TestTelegramConnectionAsync);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        GenerateRecoveryKeyCommand = new RelayCommand(GenerateRecoveryKey);
        CompleteOnboardingCommand = new RelayCommand(CompleteOnboarding, () => HasSavedRecoveryPhrase && !string.IsNullOrWhiteSpace(MasterPassword));
        ShowFileVersionsCommand = new AsyncRelayCommand(async p =>
        {
            var f = p as FileItemViewModel ?? SelectedFile;
            if (f != null)
            {
                SelectedFile = f;
            }
            IsVersionHistoryVisible = true;
            await LoadFileVersionsAsync(SelectedFile).ConfigureAwait(true);
        });
        CloseVersionHistoryCommand = new RelayCommand(() =>
        {
            IsVersionHistoryVisible = false;
        });
        RestoreSelectedVersionCommand = new AsyncRelayCommand(async p =>
        {
            var ver = p as FileVersionItemViewModel ?? SelectedVersion;
            if (ver != null)
            {
                await RestoreSelectedVersionAsync(ver).ConfigureAwait(true);
            }
        }, _ => !IsBusy);

        // Default test root if none
        var sampleDocs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BackupAppTest");
        IncludedRoots.Add(sampleDocs);
        RestoreDestination = Path.Combine(Path.GetTempPath(), "BackupAppRestore");
    }

    public async Task InitializeAsync()
    {
        await _catalogRepository.InitializeAsync().ConfigureAwait(true);

        // Load saved backup roots from catalog if present
        var backupSets = await _catalogRepository.ListBackupSetsAsync().ConfigureAwait(true);
        var defaultSet = backupSets.FirstOrDefault(b => b.Name == "DefaultBackupSet");
        if (defaultSet != null && defaultSet.IncludedRoots != null && defaultSet.IncludedRoots.Count > 0)
        {
            IncludedRoots.Clear();
            foreach (var r in defaultSet.IncludedRoots)
            {
                IncludedRoots.Add(r);
            }
        }

        // Load saved Telegram credentials if present
        var savedCreds = _credentialStoreService.LoadTelegramCredentials();
        if (savedCreds != null && !string.IsNullOrWhiteSpace(savedCreds.BotToken) && !string.IsNullOrWhiteSpace(savedCreds.ChatId))
        {
            TelegramBotToken = savedCreds.BotToken;
            TelegramChatId = savedCreds.ChatId;
            var config = new TelegramStorageConfiguration(savedCreds.BotToken, savedCreds.ChatId);
            _storageProvider = _storageFactory(config);
            SettingsStatusMessage = "פרטי החיבור לטלגרם נטענו בהצלחה.";
        }

        // Ensure master key is initialized
        if (_unlockedMasterKey == null)
        {
            _unlockedMasterKey = MasterKey.Generate();
        }

        // Load backed-up files from catalog or remote discovery
        await LoadCatalogAsync().ConfigureAwait(true);
    }

    public async Task LoadCatalogAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var backupSets = await _catalogRepository.ListBackupSetsAsync(cancellationToken).ConfigureAwait(true);
            var allSnapshots = new List<SnapshotItemViewModel>();

            foreach (var bset in backupSets)
            {
                var snaps = await _catalogRepository.ListSnapshotsAsync(bset.Id, cancellationToken).ConfigureAwait(true);
                foreach (var s in snaps.Where(s => s.Status == SnapshotStatus.Committed))
                {
                    allSnapshots.Add(new SnapshotItemViewModel(s, bset.Id));
                }
            }

            var sortedSnapshots = allSnapshots.OrderByDescending(s => s.SnapshotNumber).ToList();

            RunOnUi(() =>
            {
                AvailableSnapshots.Clear();
                foreach (var s in sortedSnapshots)
                {
                    AvailableSnapshots.Add(s);
                }
            });

            if (sortedSnapshots.Count > 0)
            {
                var latest = sortedSnapshots[0];
                _selectedSnapshot = latest;
                OnPropertyChanged(nameof(SelectedSnapshot));
                OnPropertyChanged(nameof(HasSelectedSnapshot));
                await LoadSnapshotManifestAsync(latest).ConfigureAwait(true);
                LastBackupText = latest.CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
            }
            else if (_unlockedMasterKey != null)
            {
                // Fallback to remote discovery if catalog has no local snapshots (e.g. disaster recovery on clean machine)
                var manifests = await _discoveryService.DiscoverRemoteManifestsAsync(_storageProvider, _unlockedMasterKey, cancellationToken).ConfigureAwait(true);
                if (manifests.Count > 0)
                {
                    SetCurrentManifest(manifests[0]);
                    LastBackupText = manifests[0].CreatedAtUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
                }
            }
        }
        catch
        {
            // Catalog empty or initial startup
        }
    }

    public async Task LoadSnapshotManifestAsync(SnapshotItemViewModel snapshotItem)
    {
        try
        {
            var files = await _catalogRepository.GetSnapshotFilesAsync(snapshotItem.Snapshot.Id).ConfigureAwait(true);
            var manifestItems = files.Select(f => new SnapshotManifestItem(
                Path: f.Path.Value,
                SizeBytes: f.SizeBytes,
                ContentHashSha256: f.ContentHashSha256,
                Attributes: (long)f.Attributes,
                CreatedAtUtc: f.CreatedAtUtc,
                ModifiedUtc: f.ModifiedUtc,
                ChunkIds: f.ChunkRefs.Select(c => c.Value).ToList()
            )).ToList();

            var manifest = new SnapshotManifest(
                BackupSetId: snapshotItem.BackupSetId.ToString(),
                SnapshotId: snapshotItem.Snapshot.Id.ToString(),
                SnapshotNumber: snapshotItem.Snapshot.SnapshotNumber,
                CreatedAtUtc: snapshotItem.Snapshot.CreatedAtUtc,
                TotalFiles: snapshotItem.Snapshot.TotalFiles,
                TotalBytes: snapshotItem.Snapshot.TotalBytes,
                Items: manifestItems
            );

            SetCurrentManifest(manifest);
        }
        catch (Exception ex)
        {
            RestoreStatusMessage = $"שגיאה בטעינת נקודת הזמן: {ex.Message}";
        }
    }

    private async Task RunBackupAsync()
    {
        IsBusy = true;
        Status = ProtectionState.BackingUp;
        StatusTitle = "מבצע גיבוי...";
        StatusSubtitle = "הקבצים מוצפנים באפס-ידע ומועלים לשרת";
        ProgressPercent = 0;
        ProgressSummary = "מתחיל סריקה והצפנה...";

        _currentCts = new CancellationTokenSource();

        try
        {
            _unlockedMasterKey ??= MasterKey.Generate();

            var backupSets = await _catalogRepository.ListBackupSetsAsync(_currentCts.Token).ConfigureAwait(true);
            var backupSet = backupSets.FirstOrDefault(b => b.Name == "DefaultBackupSet");
            BackupSetId bsetId;
            if (backupSet == null)
            {
                bsetId = BackupSetId.New();
                backupSet = new BackupSet(bsetId, "DefaultBackupSet", IncludedRoots.ToList(), [], DateTimeOffset.UtcNow);
                await _catalogRepository.SaveBackupSetAsync(backupSet, _currentCts.Token).ConfigureAwait(true);
            }
            else
            {
                bsetId = backupSet.Id;
                backupSet = new BackupSet(bsetId, "DefaultBackupSet", IncludedRoots.ToList(), backupSet.ExcludedPatterns, backupSet.CreatedAtUtc);
                await _catalogRepository.SaveBackupSetAsync(backupSet, _currentCts.Token).ConfigureAwait(true);
            }

            BackupProgressReport? lastReport = null;
            CurrentSpeedFormatted = string.Empty;
            EtaFormatted = string.Empty;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var speedSamples = new Queue<(double ElapsedSec, long Bytes)>();

            var progressReporter = new Progress<BackupProgressReport>(report =>
            {
                lastReport = report;
                if (!string.IsNullOrEmpty(report.StatusMessage))
                {
                    CurrentProgressItem = report.StatusMessage;
                    ProgressSummary = report.StatusMessage;
                }
                else
                {
                    CurrentProgressItem = PathFormatter.FormatForRtl(report.CurrentFileName);
                    if (report.TotalBytesScanned > 0)
                    {
                        ProgressPercent = Math.Clamp(Math.Round((double)report.BytesProcessed / report.TotalBytesScanned * 100, 1), 0, 100);
                    }
                    else if (report.TotalFilesScanned > 0)
                    {
                        ProgressPercent = Math.Clamp(Math.Round((double)report.FilesProcessed / report.TotalFilesScanned * 100, 1), 0, 100);
                    }

                    ProgressSummary = $"{report.FilesProcessed} מתוך {report.TotalFilesScanned} קבצים | {PathFormatter.FormatBytes(report.BytesProcessed)} מתוך {PathFormatter.FormatBytes(report.TotalBytesScanned)} ({ProgressPercent}%)";

                    var nowSec = sw.Elapsed.TotalSeconds;
                    speedSamples.Enqueue((nowSec, report.BytesProcessed));

                    // Keep rolling window of the last 15 seconds
                    while (speedSamples.Count > 1 && (nowSec - speedSamples.Peek().ElapsedSec) > 15.0)
                    {
                        speedSamples.Dequeue();
                    }

                    double speedBytesPerSec = 0;
                    if (speedSamples.Count >= 2)
                    {
                        var oldest = speedSamples.Peek();
                        var deltaSec = nowSec - oldest.ElapsedSec;
                        var deltaBytes = report.BytesProcessed - oldest.Bytes;
                        if (deltaSec > 0.5 && deltaBytes > 0)
                        {
                            speedBytesPerSec = deltaBytes / deltaSec;
                        }
                    }

                    if (speedBytesPerSec <= 0 && nowSec > 1.0 && report.BytesProcessed > 0)
                    {
                        speedBytesPerSec = report.BytesProcessed / nowSec;
                    }

                    if (speedBytesPerSec > 0)
                    {
                        CurrentSpeedFormatted = PathFormatter.FormatBytes((long)speedBytesPerSec) + "/s";
                        var remainingBytes = Math.Max(0, report.TotalBytesScanned - report.BytesProcessed);
                        if (remainingBytes > 0)
                        {
                            var etaSec = (int)(remainingBytes / speedBytesPerSec);
                            var timeSpan = TimeSpan.FromSeconds(etaSec);
                            EtaFormatted = timeSpan.TotalHours >= 1
                                ? timeSpan.ToString(@"hh\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture) + " נותרו"
                                : timeSpan.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture) + " נותרו";
                        }
                        else
                        {
                            EtaFormatted = "מסיים...";
                        }
                    }
                    else
                    {
                        EtaFormatted = "מחשב...";
                    }
                }
            });

            var snapId = await _backupOrchestrator.RunBackupAsync(
                bsetId,
                _unlockedMasterKey,
                _storageProvider,
                _catalogRepository,
                options: BackupEngineOptions.Default,
                progress: progressReporter,
                cancellationToken: _currentCts.Token
            ).ConfigureAwait(true);

            // Reload catalog to refresh all backed-up files in Browse tab
            await LoadCatalogAsync(_currentCts.Token).ConfigureAwait(true);

            var scanned = lastReport?.TotalFilesScanned ?? 0;
            var uploaded = lastReport?.UploadedFilesCount ?? 0;
            var unchanged = lastReport?.UnchangedFilesCount ?? 0;

            Status = ProtectionState.Protected;
            StatusTitle = "הגיבוי הושלם בהצלחה!";
            StatusSubtitle = $"{scanned} קבצים נסרקו, {uploaded} קבצים חדשים הועלו, {unchanged} קבצים ללא שינוי (דולגו)";
            ProgressSummary = StatusSubtitle;
            LastBackupText = DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
        }
        catch (OperationCanceledException)
        {
            Status = ProtectionState.Warning;
            StatusTitle = "הגיבוי בוטל";
            StatusSubtitle = "הפעולה הופסקה על ידי המשתמש";
        }
        catch (Exception ex)
        {
            Status = ProtectionState.Error;
            StatusTitle = "שגיאה בגיבוי";
            StatusSubtitle = ex.Message;
        }
        finally
        {
            IsBusy = false;
            CurrentSpeedFormatted = string.Empty;
            EtaFormatted = string.Empty;
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }

    private async Task RunRestoreAsync()
    {
        if (_latestManifest == null || _unlockedMasterKey == null)
        {
            return;
        }

        var dest = string.IsNullOrWhiteSpace(RestoreDestination)
            ? Path.Combine(Path.GetTempPath(), "BackupAppRestore")
            : RestoreDestination.Trim();
        RestoreDestination = dest;

        try
        {
            if (!Directory.Exists(dest))
            {
                Directory.CreateDirectory(dest);
            }
        }
        catch (Exception ex)
        {
            Status = ProtectionState.Error;
            StatusTitle = "שגיאת הרשאות ביצירת תיקיית יעד";
            StatusSubtitle = $"לא ניתן ליצור את תיקיית היעד: {ex.Message}";
            RestoreStatusMessage = $"שגיאת הרשאות ביצירת התיקייה '{dest}': {ex.Message}";
            HasRestoreError = true;
            return;
        }

        IsBusy = true;
        IsRestoreCompleted = false;
        HasRestoreError = false;
        RestoreStatusMessage = string.Empty;
        Status = ProtectionState.Restoring;
        StatusTitle = "משחזר נתונים...";
        StatusSubtitle = "הקבצים מורדים מהאחסון, מאומתים ומשוחזרים לדיסק";
        ProgressPercent = 0;

        _currentCts = new CancellationTokenSource();

        try
        {
            var options = new RestoreOptions(
                DestinationRootPath: RestoreDestination,
                ConflictResolution: ConflictResolution,
                RestoreTimestamps: RestoreTimestamps
            );

            var progressReporter = new Progress<RestoreProgressReport>(report =>
            {
                CurrentProgressItem = PathFormatter.FormatForRtl(report.CurrentFileName);
                if (report.TotalFiles > 0)
                {
                    ProgressPercent = Math.Round((double)report.FilesProcessed / report.TotalFiles * 100, 1);
                }
                var phaseText = string.IsNullOrEmpty(report.CurrentPhase) ? string.Empty : $" [{report.CurrentPhase}]";
                ProgressSummary = $"{report.FilesProcessed} מתוך {report.TotalFiles} קבצים שוחזרו ({PathFormatter.FormatBytes(report.BytesProcessed)}){phaseText}";
            });

            await _restoreOrchestrator.RestoreAllAsync(
                _latestManifest,
                _unlockedMasterKey,
                _storageProvider,
                options,
                progress: progressReporter,
                cancellationToken: _currentCts.Token
            ).ConfigureAwait(true);

            IsRestoreCompleted = true;
            HasRestoreError = false;
            Status = ProtectionState.Protected;
            StatusTitle = "השחזור הושלם בהצלחה!";
            StatusSubtitle = $"הקבצים שוחזרו אל {PathFormatter.FormatForRtl(RestoreDestination)}";
            RestoreStatusMessage = $"כל הקבצים שוחזרו בהצלחה ואומתו ב-SHA-256 אל: {RestoreDestination}";
        }
        catch (OperationCanceledException)
        {
            Status = ProtectionState.Warning;
            StatusTitle = "השחזור בוטל";
            StatusSubtitle = "הפעולה הופסקה על ידי המשתמש";
            RestoreStatusMessage = "פעולת השחזור הופסקה על ידי המשתמש.";
            HasRestoreError = true;
        }
        catch (UnauthorizedAccessException ex)
        {
            Status = ProtectionState.Error;
            StatusTitle = "שגיאת הרשאות בשחזור";
            StatusSubtitle = "אין הרשאת כתיבה לתיקיית היעד. בחר תיקייה אחרת או הפעל כמנהל.";
            RestoreStatusMessage = $"שגיאת הרשאות: {ex.Message}";
            HasRestoreError = true;
        }
        catch (CryptographicException ex)
        {
            Status = ProtectionState.Error;
            StatusTitle = "שגיאת פענוח / שלמות נתונים";
            StatusSubtitle = "פענוח XChaCha20 או אימות שלמות SHA-256 נכשל על אחד המקטעים.";
            RestoreStatusMessage = $"שגיאת פענוח: {ex.Message}";
            HasRestoreError = true;
        }
        catch (KeyNotFoundException ex)
        {
            Status = ProtectionState.Error;
            StatusTitle = "מקטע לא נמצא בטלגרם";
            StatusSubtitle = "אחד המקטעים המוצפנים לא נמצא באחסון הטלגרם.";
            RestoreStatusMessage = $"שגיאת מקור נתונים: {ex.Message}";
            HasRestoreError = true;
        }
        catch (HttpRequestException ex)
        {
            Status = ProtectionState.Error;
            StatusTitle = "שגיאת תקשורת עם טלגרם";
            StatusSubtitle = "נכשל ניסיון הורדת המקטע המוצפן מהערוץ (ייתכן ניתוק רשת או בעיית שרת).";
            RestoreStatusMessage = $"שגיאת רשת/אחסון: {ex.Message}";
            HasRestoreError = true;
        }
        catch (Exception ex)
        {
            Status = ProtectionState.Error;
            StatusTitle = "שגיאה בשחזור";
            StatusSubtitle = ex.Message;
            RestoreStatusMessage = $"שגיאה בשחזור: {ex.Message}";
            HasRestoreError = true;
        }
        finally
        {
            IsBusy = false;
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }

    public void OpenRestoreFolder()
    {
        var dest = string.IsNullOrWhiteSpace(RestoreDestination)
            ? Path.Combine(Path.GetTempPath(), "BackupAppRestore")
            : RestoreDestination.Trim();

        if (Directory.Exists(dest))
        {
            _processLauncher.Start(dest);
        }
    }

    private async Task UpdatePreviewForSelectedFileAsync(FileItemViewModel? file)
    {
        if (file == null)
        {
            PreviewTextContent = string.Empty;
            PreviewImagePath = null;
            IsImagePreview = false;
            IsTextPreview = false;
            return;
        }

        IsPreviewLoading = true;
        try
        {
            string? localPath = null;
            foreach (var root in IncludedRoots)
            {
                var candidate = Path.Combine(root, file.RelativePath);
                if (File.Exists(candidate))
                {
                    localPath = candidate;
                    break;
                }
            }

            if (localPath == null)
            {
                var tempCandidate = Path.Combine(Path.GetTempPath(), "BackupAppPreview", file.RelativePath);
                if (File.Exists(tempCandidate))
                {
                    localPath = tempCandidate;
                }
            }

            var ext = Path.GetExtension(file.RelativePath).ToLowerInvariant();
            var imageExtensions = new HashSet<string> { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".ico", ".webp" };
            var textExtensions = new HashSet<string> { ".txt", ".log", ".json", ".xml", ".cs", ".md", ".ini", ".csv", ".sql", ".yaml", ".yml", ".xaml", ".config", ".html", ".htm", ".js", ".ts" };

            if (imageExtensions.Contains(ext))
            {
                if (localPath != null)
                {
                    PreviewImagePath = localPath;
                    IsImagePreview = true;
                    IsTextPreview = false;
                }
                else
                {
                    PreviewTextContent = "קובץ תמונה שמור ומאובטח בענן.\nלחץ '👁️ פתח קובץ' להורדה, פענוח והצגה.";
                    PreviewImagePath = null;
                    IsImagePreview = false;
                    IsTextPreview = true;
                }
            }
            else if (textExtensions.Contains(ext))
            {
                if (localPath != null)
                {
                    using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
                    var buffer = new char[4096];
                    var read = await reader.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(true);
                    var content = new string(buffer, 0, read);
                    if (stream.Length > buffer.Length)
                    {
                        content += "\n\n... [קובץ גדול: מוצג חלק מהתוכן]";
                    }
                    PreviewTextContent = content;
                }
                else
                {
                    PreviewTextContent = "קובץ טקסט שמור ומוצפן בענן.\nלחץ '👁️ פתח קובץ' להורדה, פענוח והצגה מלאה.";
                }
                PreviewImagePath = null;
                IsImagePreview = false;
                IsTextPreview = true;
            }
            else
            {
                PreviewTextContent = $"סוג קובץ ({ext}) שמור ומוצפן באפס-ידע.\nלחץ '👁️ פתח קובץ' לצפייה באמצעות יישום המערכת.";
                PreviewImagePath = null;
                IsImagePreview = false;
                IsTextPreview = true;
            }
        }
        catch (Exception ex)
        {
            PreviewTextContent = $"שגיאה בטעינת תצוגה מקדימה: {ex.Message}";
            IsImagePreview = false;
            IsTextPreview = true;
        }
        finally
        {
            IsPreviewLoading = false;
        }
    }

    public async Task OpenSelectedFileAsync(object? parameter = null)
    {
        var fileItem = parameter as FileItemViewModel ?? SelectedFile;
        if (fileItem == null || _latestManifest == null || _unlockedMasterKey == null)
        {
            return;
        }

        IsBusy = true;
        Status = ProtectionState.Restoring;
        StatusTitle = "משחזר קובץ לצפייה...";
        StatusSubtitle = $"מפענח את {fileItem.FormattedPath}";
        ProgressPercent = 0;

        try
        {
            var previewDir = Path.Combine(Path.GetTempPath(), "BackupAppPreview");
            if (!Directory.Exists(previewDir))
            {
                Directory.CreateDirectory(previewDir);
            }

            var options = new RestoreOptions(
                DestinationRootPath: previewDir,
                ConflictResolution: RestoreConflictResolution.Overwrite,
                RestoreTimestamps: true
            );

            var progressReporter = new Progress<RestoreProgressReport>(report =>
            {
                CurrentProgressItem = PathFormatter.FormatForRtl(report.CurrentFileName);
                var phaseText = string.IsNullOrEmpty(report.CurrentPhase) ? string.Empty : $" [{report.CurrentPhase}]";
                ProgressSummary = $"{phaseText}";
            });

            await _restoreOrchestrator.RestoreFileAsync(
                _latestManifest,
                fileItem.RelativePath,
                _unlockedMasterKey,
                _storageProvider,
                options,
                progress: progressReporter
            ).ConfigureAwait(true);

            var targetPath = RestoreOrchestrator.ValidateAndResolveTargetPath(previewDir, fileItem.RelativePath);

            Status = ProtectionState.Protected;
            StatusTitle = "הקובץ שוחזר ונפתח!";
            StatusSubtitle = targetPath;

            _processLauncher.Start(targetPath);
        }
        catch (Exception ex)
        {
            Status = ProtectionState.Error;
            StatusTitle = "שגיאה בפתיחת הקובץ";
            StatusSubtitle = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RestoreSingleFileAsync(object? parameter = null)
    {
        var fileItem = parameter as FileItemViewModel ?? SelectedFile;
        if (fileItem == null || _latestManifest == null || _unlockedMasterKey == null)
        {
            return;
        }

        var dest = string.IsNullOrWhiteSpace(RestoreDestination)
            ? Path.Combine(Path.GetTempPath(), "BackupAppRestore")
            : RestoreDestination.Trim();
        RestoreDestination = dest;

        if (!Directory.Exists(dest))
        {
            Directory.CreateDirectory(dest);
        }

        IsBusy = true;
        Status = ProtectionState.Restoring;
        StatusTitle = "משחזר קובץ יחיד...";
        StatusSubtitle = $"משחזר את {fileItem.FormattedPath}";
        ProgressPercent = 0;

        try
        {
            var options = new RestoreOptions(
                DestinationRootPath: dest,
                ConflictResolution: ConflictResolution,
                RestoreTimestamps: RestoreTimestamps
            );

            await _restoreOrchestrator.RestoreFileAsync(
                _latestManifest,
                fileItem.RelativePath,
                _unlockedMasterKey,
                _storageProvider,
                options
            ).ConfigureAwait(true);

            var targetPath = RestoreOrchestrator.ValidateAndResolveTargetPath(dest, fileItem.RelativePath);

            IsRestoreCompleted = true;
            HasRestoreError = false;
            Status = ProtectionState.Protected;
            StatusTitle = "הקובץ שוחזר בהצלחה!";
            StatusSubtitle = targetPath;
            RestoreStatusMessage = $"הקובץ שוחזר בהצלחה אל: {targetPath}";
        }
        catch (Exception ex)
        {
            Status = ProtectionState.Error;
            StatusTitle = "שגיאה בשחזור הקובץ";
            StatusSubtitle = ex.Message;
            RestoreStatusMessage = $"שגיאה בשחזור הקובץ: {ex.Message}";
            HasRestoreError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void ShowFileInFolder(object? parameter = null)
    {
        var fileItem = parameter as FileItemViewModel ?? SelectedFile;
        if (fileItem == null)
        {
            return;
        }

        // 1. Check if restored in preview directory
        var previewPath = Path.Combine(Path.GetTempPath(), "BackupAppPreview", fileItem.RelativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(previewPath))
        {
            _processLauncher.Start("explorer.exe", $"/select,\"{previewPath}\"");
            return;
        }

        // 2. Check if restored in RestoreDestination
        if (!string.IsNullOrWhiteSpace(RestoreDestination))
        {
            var restorePath = Path.Combine(RestoreDestination, fileItem.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(restorePath))
            {
                _processLauncher.Start("explorer.exe", $"/select,\"{restorePath}\"");
                return;
            }
        }

        // 3. Check if exists in any included roots (local original)
        foreach (var root in IncludedRoots)
        {
            var localPath = Path.Combine(root, fileItem.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(localPath))
            {
                _processLauncher.Start("explorer.exe", $"/select,\"{localPath}\"");
                return;
            }
        }

        // 4. Fallback: if restore destination exists, open it
        if (!string.IsNullOrWhiteSpace(RestoreDestination) && Directory.Exists(RestoreDestination))
        {
            _processLauncher.Start(RestoreDestination);
        }
        else
        {
            StatusTitle = "הקובץ עדיין לא שוחזר לדיסק";
            StatusSubtitle = "השתמש ב'פתח קובץ' או 'שחזר קובץ זה' כדי לשחזרו תחילה.";
        }
    }

    public async Task LoadFileVersionsAsync(FileItemViewModel? fileItem = null)
    {
        fileItem ??= SelectedFile;
        if (fileItem == null)
        {
            RunOnUi(() =>
            {
                SelectedFileVersions.Clear();
                SelectedVersion = null;
            });
            return;
        }

        try
        {
            var canonical = CanonicalPath.From(fileItem.RelativePath);
            var versions = await _catalogRepository.GetFileVersionsAsync(canonical).ConfigureAwait(true);

            RunOnUi(() =>
            {
                SelectedFileVersions.Clear();
                int idx = versions.Count;
                foreach (var ver in versions)
                {
                    SelectedFileVersions.Add(new FileVersionItemViewModel(ver, idx--));
                }
                SelectedVersion = SelectedFileVersions.FirstOrDefault();
                IsVersionHistoryVisible = true;
            });
        }
        catch (Exception ex)
        {
            Status = ProtectionState.Error;
            StatusTitle = "שגיאה בטעינת היסטוריית גרסאות";
            StatusSubtitle = ex.Message;
        }
    }

    public async Task RestoreSelectedVersionAsync(FileVersionItemViewModel? versionItem = null)
    {
        versionItem ??= SelectedVersion;
        if (versionItem == null || _unlockedMasterKey == null)
        {
            return;
        }

        var dest = string.IsNullOrWhiteSpace(RestoreDestination)
            ? Path.Combine(Path.GetTempPath(), "BackupAppRestore")
            : RestoreDestination.Trim();
        RestoreDestination = dest;

        if (!Directory.Exists(dest))
        {
            Directory.CreateDirectory(dest);
        }

        IsBusy = true;
        Status = ProtectionState.Restoring;
        StatusTitle = "משחזר גרסה נבחרת...";
        StatusSubtitle = $"משחזר את {versionItem.FormattedPath} ({versionItem.BackupDateFormatted})";
        ProgressPercent = 0;

        try
        {
            var ver = versionItem.Version;
            var item = new SnapshotManifestItem(
                Path: ver.Path.Value,
                SizeBytes: ver.SizeBytes,
                ContentHashSha256: ver.ContentHashSha256,
                Attributes: (long)ver.Attributes,
                CreatedAtUtc: ver.CreatedAtUtc,
                ModifiedUtc: ver.ModifiedUtc,
                ChunkIds: ver.ChunkRefs.Select(c => c.Value).ToList()
            );

            var tempManifest = new SnapshotManifest(
                BackupSetId: Guid.Empty.ToString(),
                SnapshotId: ver.SnapshotId.Value.ToString("D"),
                SnapshotNumber: 0,
                CreatedAtUtc: ver.CreatedAtUtc,
                TotalFiles: 1,
                TotalBytes: ver.SizeBytes,
                Items: [item]
            );

            var options = new RestoreOptions(
                DestinationRootPath: dest,
                ConflictResolution: ConflictResolution,
                RestoreTimestamps: RestoreTimestamps
            );

            await _restoreOrchestrator.RestoreFileAsync(
                tempManifest,
                ver.Path.Value,
                _unlockedMasterKey,
                _storageProvider,
                options
            ).ConfigureAwait(true);

            var targetPath = RestoreOrchestrator.ValidateAndResolveTargetPath(dest, ver.Path.Value);

            IsRestoreCompleted = true;
            HasRestoreError = false;
            Status = ProtectionState.Protected;
            StatusTitle = "הגרסה שוחזרה בהצלחה!";
            StatusSubtitle = $"{targetPath} ({versionItem.BackupDateFormatted})";
            RestoreStatusMessage = $"הגרסה מתאריך {versionItem.BackupDateFormatted} שוחזרה בהצלחה אל: {targetPath}";
        }
        catch (Exception ex)
        {
            Status = ProtectionState.Error;
            StatusTitle = "שגיאה בשחזור גרסה";
            StatusSubtitle = ex.Message;
            RestoreStatusMessage = $"שגיאה בשחזור גרסה: {ex.Message}";
            HasRestoreError = true;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CancelOperation()
    {
        _currentCts?.Cancel();
    }

    private void SetCurrentManifest(SnapshotManifest manifest)
    {
        RunOnUi(() =>
        {
            _latestManifest = manifest;
            TotalFilesCount = (int)manifest.TotalFiles;
            TotalSizeFormatted = PathFormatter.FormatBytes(manifest.TotalBytes);

            _allManifestFiles.Clear();
            foreach (var item in manifest.Items)
            {
                _allManifestFiles.Add(new FileItemViewModel(item));
            }

            FilterFiles();
        });
    }

    private void FilterFiles()
    {
        RunOnUi(() =>
        {
            BrowsedFiles.Clear();
            var query = SearchQuery?.Trim();

            foreach (var item in _allManifestFiles)
            {
                if (string.IsNullOrEmpty(query) ||
                    item.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    item.FormattedPath.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    BrowsedFiles.Add(item);
                }
            }
        });
    }

    private void AddFolder()
    {
        if (string.IsNullOrWhiteSpace(NewRootPath))
        {
            return;
        }

        var folder = NewRootPath.Trim();
        if (IncludedRoots.Contains(folder))
        {
            SettingsStatusMessage = "תיקייה זו כבר קיימת ברשימת הגיבוי.";
            return;
        }

        IncludedRoots.Add(folder);
        NewRootPath = string.Empty;
        SettingsStatusMessage = $"התיקייה נוספה בהצלחה: {PathFormatter.FormatForRtl(folder)}";
        _ = PersistRootsConfigurationAsync();
        if (_scheduleMode == BackupScheduleMode.OnFileSystemChange)
        {
            UpdateScheduler();
        }
    }

    private void RemoveFolder(object? parameter)
    {
        var path = parameter as string ?? SelectedRootPath;
        if (!string.IsNullOrWhiteSpace(path))
        {
            if (IncludedRoots.Remove(path))
            {
                if (SelectedRootPath == path)
                {
                    SelectedRootPath = null;
                }
                SettingsStatusMessage = $"התיקייה הוסרה בהצלחה: {PathFormatter.FormatForRtl(path)}";
                _ = PersistRootsConfigurationAsync();
                if (_scheduleMode == BackupScheduleMode.OnFileSystemChange)
                {
                    UpdateScheduler();
                }
            }
        }
    }

    private void BrowseFolder()
    {
        var initialDir = Directory.Exists(NewRootPath) ? NewRootPath : null;
        var folder = _folderPickerService.PickFolder(initialDir);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            NewRootPath = folder;
            AddFolder();
        }
    }

    private void BrowseRestoreDestination()
    {
        var initialDir = Directory.Exists(RestoreDestination) ? RestoreDestination : null;
        var folder = _folderPickerService.PickFolder(initialDir);
        if (!string.IsNullOrWhiteSpace(folder))
        {
            RestoreDestination = folder;
        }
    }

    public async Task PersistRootsConfigurationAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var backupSets = await _catalogRepository.ListBackupSetsAsync(cancellationToken).ConfigureAwait(false);
            var backupSet = backupSets.FirstOrDefault(b => b.Name == "DefaultBackupSet");
            if (backupSet == null)
            {
                var bsetId = BackupSetId.New();
                backupSet = new BackupSet(bsetId, "DefaultBackupSet", IncludedRoots.ToList(), [], DateTimeOffset.UtcNow);
            }
            else
            {
                backupSet = new BackupSet(backupSet.Id, "DefaultBackupSet", IncludedRoots.ToList(), backupSet.ExcludedPatterns, backupSet.CreatedAtUtc);
            }
            await _catalogRepository.SaveBackupSetAsync(backupSet, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to persist backup roots configuration: {ex.Message}");
        }
    }

    private async Task TestTelegramConnectionAsync()
    {
        if (string.IsNullOrWhiteSpace(TelegramBotToken) || string.IsNullOrWhiteSpace(TelegramChatId))
        {
            SettingsStatusMessage = "אנא הזן טוקן ו-Chat ID לבדיקה.";
            return;
        }

        SettingsStatusMessage = "בודק חיבור מול טלגרם...";
        try
        {
            var config = new TelegramStorageConfiguration(TelegramBotToken.Trim(), TelegramChatId.Trim());
            var provider = _storageFactory(config);
            if (provider is TelegramStorageAdapter adapter)
            {
                await adapter.ValidateConnectionAsync().ConfigureAwait(true);
            }
            else
            {
                await provider.GetCapabilitiesAsync().ConfigureAwait(true);
            }

            _storageProvider = provider;
            _credentialStoreService.SaveTelegramCredentials(new TelegramCredentials(TelegramBotToken.Trim(), TelegramChatId.Trim()));

            SettingsStatusMessage = "חיבור טלגרם אומת בהצלחה והוגדר כאחסון הראשי!";
        }
        catch (ProviderAuthenticationException paEx)
        {
            SettingsStatusMessage = $"שגיאת אימות מול טלגרם: {paEx.Message}";
        }
        catch (HttpRequestException httpEx)
        {
            SettingsStatusMessage = $"שגיאת תקשורת עם טלגרם: {httpEx.Message}";
        }
        catch (Exception ex)
        {
            SettingsStatusMessage = $"שגיאה בבדיקת חיבור: {ex.Message}";
        }
    }

    private void ToggleTheme()
    {
        var newTheme = ThemeManager.CurrentTheme == AppTheme.Light ? AppTheme.Dark : AppTheme.Light;
        ThemeManager.ApplyTheme(newTheme);
    }

    private void GenerateRecoveryKey()
    {
        var recoveryService = new RecoveryKeyService();
        RecoveryPhrase = recoveryService.GenerateRecoveryKey(out _);
    }

    private void CompleteOnboarding()
    {
        IsOnboardingOpen = false;
        Status = ProtectionState.Protected;
        StatusTitle = "ההגדרה הראשונית הושלמה";
        StatusSubtitle = "מוכן לביצוע גיבוי ראשון";
    }

    public void UpdateScheduler()
    {
        var dailyTime = TimeSpan.FromHours(2);
        if (TimeSpan.TryParse(_dailyScheduleTime, CultureInfo.InvariantCulture, out var parsed))
        {
            dailyTime = parsed;
        }

        var config = new ScheduleConfiguration(
            Mode: _scheduleMode,
            DailyTime: dailyTime,
            ChangeDebounce: TimeSpan.FromSeconds(10),
            WatchPaths: IncludedRoots.ToList()
        );

        _scheduler.Configure(config);

        if (_scheduleMode == BackupScheduleMode.Manual)
        {
            _scheduler.StopScheduler();
            ScheduleStatusText = "התזמון כבוי (ידני בלבד)";
        }
        else
        {
            _scheduler.StartScheduler();
            ScheduleStatusText = _scheduleMode switch
            {
                BackupScheduleMode.Hourly => "גיבוי אוטומטי פעיל: יופעל מדי שעה",
                BackupScheduleMode.Daily => $"גיבוי אוטומטי פעיל: מדי יום בשעה {_dailyScheduleTime}",
                BackupScheduleMode.OnFileSystemChange => "ניטור שינויים פעיל בזמן אמת (סריקה והעלאה אוטומטית)",
                _ => "פעיל ברקע"
            };
        }
    }

    public void Dispose()
    {
        _scheduler.Dispose();
        _currentCts?.Dispose();
        _currentCts = null;
        _unlockedMasterKey?.Dispose();
        _unlockedMasterKey = null;
    }
}
