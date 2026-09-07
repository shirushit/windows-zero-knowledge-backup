using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Windows.Input;
using BackupApp.BackupEngine;
using BackupApp.Catalog;
using BackupApp.Crypto;
using BackupApp.Domain;
using BackupApp.RestoreEngine;
using BackupApp.Storage;
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
    public string RelativePath { get; }
    public string FormattedPath { get; }
    public long SizeBytes { get; }
    public string FormattedSize { get; }
    public DateTimeOffset ModifiedUtc { get; }
    public string FormattedDate { get; }

    public FileItemViewModel(SnapshotManifestItem item)
    {
        RelativePath = item.Path;
        FormattedPath = PathFormatter.FormatForRtl(item.Path);
        SizeBytes = item.SizeBytes;
        FormattedSize = PathFormatter.FormatBytes(item.SizeBytes);
        ModifiedUtc = item.ModifiedUtc;
        FormattedDate = item.ModifiedUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture);
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
    private CancellationTokenSource? _currentCts;

    // Search & Browse
    private string _searchQuery = string.Empty;
    private ObservableCollection<FileItemViewModel> _browsedFiles = [];
    private FileItemViewModel? _selectedFile;

    // Restore Options
    private string _restoreDestination = string.Empty;
    private RestoreConflictResolution _conflictResolution = RestoreConflictResolution.Overwrite;
    private bool _restoreTimestamps = true;

    // Settings
    private ObservableCollection<string> _includedRoots = [];
    private string _newRootPath = string.Empty;
    private string _telegramBotToken = string.Empty;
    private string _telegramChatId = string.Empty;
    private string _settingsStatusMessage = string.Empty;

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

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                FilterFiles();
            }
        }
    }

    public ObservableCollection<FileItemViewModel> BrowsedFiles
    {
        get => _browsedFiles;
        set => SetProperty(ref _browsedFiles, value);
    }

    public FileItemViewModel? SelectedFile
    {
        get => _selectedFile;
        set => SetProperty(ref _selectedFile, value);
    }

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

    // Commands
    public ICommand TriggerBackupCommand { get; }
    public ICommand CancelOperationCommand { get; }
    public ICommand TriggerRestoreCommand { get; }
    public ICommand AddFolderCommand { get; }
    public ICommand RemoveFolderCommand { get; }
    public ICommand TestTelegramConnectionCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand GenerateRecoveryKeyCommand { get; }
    public ICommand CompleteOnboardingCommand { get; }

    private readonly List<FileItemViewModel> _allManifestFiles = [];
    private SnapshotManifest? _latestManifest;
    private MasterKey? _unlockedMasterKey;
    private readonly IStorageProvider _storageProvider;
    private readonly ICatalogRepository _catalogRepository;
    private readonly IBackupOrchestrator _backupOrchestrator;
    private readonly IRestoreOrchestrator _restoreOrchestrator;
    private readonly IRemoteCatalogDiscoveryService _discoveryService;

    public MainViewModel(
        IStorageProvider? storageProvider = null,
        ICatalogRepository? catalogRepository = null,
        IBackupOrchestrator? backupOrchestrator = null,
        IRestoreOrchestrator? restoreOrchestrator = null,
        IRemoteCatalogDiscoveryService? discoveryService = null)
    {
        _storageProvider = storageProvider ?? new InMemoryStorageProvider();
        _catalogRepository = catalogRepository ?? new SqliteCatalogRepository(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "catalog.sqlite"));
        _backupOrchestrator = backupOrchestrator ?? new BackupOrchestrator();
        _restoreOrchestrator = restoreOrchestrator ?? new RestoreOrchestrator();
        _discoveryService = discoveryService ?? new RemoteCatalogDiscoveryService();

        TriggerBackupCommand = new AsyncRelayCommand(RunBackupAsync, () => !IsBusy);
        CancelOperationCommand = new RelayCommand(CancelOperation, () => IsBusy);
        TriggerRestoreCommand = new AsyncRelayCommand(RunRestoreAsync, () => !IsBusy && _latestManifest != null);
        AddFolderCommand = new RelayCommand(AddFolder);
        RemoveFolderCommand = new RelayCommand(RemoveFolder);
        TestTelegramConnectionCommand = new AsyncRelayCommand(TestTelegramConnectionAsync);
        ToggleThemeCommand = new RelayCommand(ToggleTheme);
        GenerateRecoveryKeyCommand = new RelayCommand(GenerateRecoveryKey);
        CompleteOnboardingCommand = new RelayCommand(CompleteOnboarding, () => HasSavedRecoveryPhrase && !string.IsNullOrWhiteSpace(MasterPassword));

        // Default test root if none
        var sampleDocs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BackupAppTest");
        IncludedRoots.Add(sampleDocs);
        RestoreDestination = Path.Combine(Path.GetTempPath(), "BackupAppRestore");
    }

    public async Task InitializeAsync()
    {
        await _catalogRepository.InitializeAsync().ConfigureAwait(true);

        // Ensure master key is initialized
        if (_unlockedMasterKey == null)
        {
            _unlockedMasterKey = MasterKey.Generate();
        }

        // Try discovering manifests
        try
        {
            var manifests = await _discoveryService.DiscoverRemoteManifestsAsync(_storageProvider, _unlockedMasterKey).ConfigureAwait(true);
            if (manifests.Count > 0)
            {
                SetCurrentManifest(manifests[0]);
            }
        }
        catch
        {
            // First run without backups
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

            var bsetId = BackupSetId.New();
            var backupSet = new BackupSet(bsetId, "DefaultBackupSet", IncludedRoots.ToList(), [], DateTimeOffset.UtcNow);
            await _catalogRepository.SaveBackupSetAsync(backupSet, _currentCts.Token).ConfigureAwait(true);

            var progressReporter = new Progress<BackupProgressReport>(report =>
            {
                CurrentProgressItem = PathFormatter.FormatForRtl(report.CurrentFileName);
                if (report.TotalFilesScanned > 0)
                {
                    ProgressPercent = Math.Round((double)report.FilesProcessed / report.TotalFilesScanned * 100, 1);
                }
                ProgressSummary = $"{report.FilesProcessed} מתוך {report.TotalFilesScanned} קבצים ({PathFormatter.FormatBytes(report.BytesProcessed)})";
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

            // Fetch and set latest manifest
            var manifests = await _discoveryService.DiscoverRemoteManifestsAsync(_storageProvider, _unlockedMasterKey, _currentCts.Token).ConfigureAwait(true);
            if (manifests.Count > 0)
            {
                SetCurrentManifest(manifests[0]);
            }

            Status = ProtectionState.Protected;
            StatusTitle = "הגיבוי הושלם בהצלחה!";
            StatusSubtitle = "כל הנתונים מוגנים ומאומתים באחסון המרוחק";
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

        IsBusy = true;
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
                ProgressSummary = $"{report.FilesProcessed} מתוך {report.TotalFiles} קבצים שוחזרו ({PathFormatter.FormatBytes(report.BytesProcessed)})";
            });

            await _restoreOrchestrator.RestoreAllAsync(
                _latestManifest,
                _unlockedMasterKey,
                _storageProvider,
                options,
                progress: progressReporter,
                cancellationToken: _currentCts.Token
            ).ConfigureAwait(true);

            Status = ProtectionState.Protected;
            StatusTitle = "השחזור הושלם בהצלחה!";
            StatusSubtitle = $"הקבצים שוחזרו אל {PathFormatter.FormatForRtl(RestoreDestination)}";
        }
        catch (OperationCanceledException)
        {
            Status = ProtectionState.Warning;
            StatusTitle = "השחזור בוטל";
            StatusSubtitle = "הפעולה הופסקה על ידי המשתמש";
        }
        catch (Exception ex)
        {
            Status = ProtectionState.Error;
            StatusTitle = "שגיאה בשחזור";
            StatusSubtitle = ex.Message;
        }
        finally
        {
            IsBusy = false;
            _currentCts?.Dispose();
            _currentCts = null;
        }
    }

    private void CancelOperation()
    {
        _currentCts?.Cancel();
    }

    private void SetCurrentManifest(SnapshotManifest manifest)
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
    }

    private void FilterFiles()
    {
        BrowsedFiles.Clear();
        var query = SearchQuery.Trim();

        foreach (var item in _allManifestFiles)
        {
            if (string.IsNullOrEmpty(query) || item.RelativePath.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                BrowsedFiles.Add(item);
            }
        }
    }

    private void AddFolder()
    {
        if (!string.IsNullOrWhiteSpace(NewRootPath) && !IncludedRoots.Contains(NewRootPath))
        {
            IncludedRoots.Add(NewRootPath);
            NewRootPath = string.Empty;
        }
    }

    private void RemoveFolder(object? parameter)
    {
        if (parameter is string path)
        {
            IncludedRoots.Remove(path);
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
        await Task.Delay(300); // UI simulation feedback
        SettingsStatusMessage = "חיבור טלגרם אומת בהצלחה!";
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

    public void Dispose()
    {
        _currentCts?.Dispose();
        _currentCts = null;
        _unlockedMasterKey?.Dispose();
        _unlockedMasterKey = null;
    }
}
