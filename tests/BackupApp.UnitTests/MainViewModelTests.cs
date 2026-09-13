using System.Text;
using BackupApp.BackupEngine;
using BackupApp.Catalog;
using BackupApp.Crypto;
using BackupApp.Domain;
using BackupApp.RestoreEngine;
using BackupApp.Storage;
using BackupApp.Storage.Telegram;
using BackupApp.UI.Services;
using BackupApp.UI.ViewModels;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class MainViewModelTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _sourceDir;
    private readonly string _restoreDir;
    private readonly string _catalogDbPath;

    public MainViewModelTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"ui_vm_tests_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_testRoot, "source");
        _restoreDir = Path.Combine(_testRoot, "restore");
        _catalogDbPath = Path.Combine(_testRoot, "catalog.sqlite");

        Directory.CreateDirectory(_sourceDir);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_testRoot))
        {
            try
            {
                Directory.Delete(_testRoot, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public async Task MainViewModel_InitialState_ShouldBeProtectedOrIdle()
    {
        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);

        using var vm = new MainViewModel(storage, catalog);
        await vm.InitializeAsync();

        vm.Status.Should().Be(ProtectionState.Protected);
        vm.TotalFilesCount.Should().Be(0);
        vm.BrowsedFiles.Should().BeEmpty();
    }

    [Fact]
    public async Task MainViewModel_BackupAndRestoreWorkflow_ShouldExecuteCleanlyThroughUI()
    {
        // 1. Create files in source directory
        var file1 = Path.Combine(_sourceDir, "document.txt");
        var file2 = Path.Combine(_sourceDir, "חשבון.txt");
        await File.WriteAllTextAsync(file1, "Document Content 2026");
        await File.WriteAllTextAsync(file2, "מידע בעברית");

        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);

        using var vm = new MainViewModel(storage, catalog)
        {
            RestoreDestination = _restoreDir
        };
        vm.IncludedRoots.Clear();
        vm.IncludedRoots.Add(_sourceDir);

        await vm.InitializeAsync();

        // 2. Trigger Backup via UI Command
        vm.TriggerBackupCommand.CanExecute(null).Should().BeTrue();
        await ((AsyncRelayCommand)vm.TriggerBackupCommand).ExecuteAsync(null);

        vm.Status.Should().Be(ProtectionState.Protected);
        vm.StatusTitle.Should().Contain("הושלם");
        vm.TotalFilesCount.Should().Be(2);
        vm.BrowsedFiles.Should().HaveCount(2);

        // 3. Search files via UI
        vm.SearchQuery = "חשבון";
        vm.BrowsedFiles.Should().ContainSingle().Which.RelativePath.Should().Contain("חשבון");

        vm.SearchQuery = string.Empty;
        vm.BrowsedFiles.Should().HaveCount(2);

        // 4. Trigger Restore via UI Command
        vm.TriggerRestoreCommand.CanExecute(null).Should().BeTrue();
        await ((AsyncRelayCommand)vm.TriggerRestoreCommand).ExecuteAsync(null);

        vm.Status.Should().Be(ProtectionState.Protected);
        vm.StatusTitle.Should().Contain("הושלם");

        // Verify restored files exist
        var restoredFile1 = Path.Combine(_restoreDir, "document.txt");
        var restoredFile2 = Path.Combine(_restoreDir, "חשבון.txt");

        File.Exists(restoredFile1).Should().BeTrue();
        File.Exists(restoredFile2).Should().BeTrue();

        (await File.ReadAllTextAsync(restoredFile1)).Should().Be("Document Content 2026");
        (await File.ReadAllTextAsync(restoredFile2)).Should().Be("מידע בעברית");
    }

    [Fact]
    public void MainViewModel_FolderManagement_ShouldAddAndRemoveFolders()
    {
        using var vm = new MainViewModel();
        vm.IncludedRoots.Clear();

        vm.NewRootPath = @"C:\TestFolder";
        vm.AddFolderCommand.Execute(null);

        vm.IncludedRoots.Should().ContainSingle().Which.Should().Be(@"C:\TestFolder");
        vm.NewRootPath.Should().BeEmpty();

        vm.RemoveFolderCommand.Execute(@"C:\TestFolder");
        vm.IncludedRoots.Should().BeEmpty();
    }

    [Fact]
    public async Task MainViewModel_TestTelegramConnection_WhenEmpty_ShouldSetWarningMessage()
    {
        using var vm = new MainViewModel();
        vm.TelegramBotToken = "";
        vm.TelegramChatId = "";

        await ((AsyncRelayCommand)vm.TestTelegramConnectionCommand).ExecuteAsync(null);

        vm.SettingsStatusMessage.Should().Contain("אנא הזן");
    }

    [Fact]
    public async Task MainViewModel_TestTelegramConnection_WhenSuccessful_ShouldSaveCredentialsAndSwitchStorage()
    {
        var credDir = Path.Combine(_testRoot, "creds");
        Directory.CreateDirectory(credDir);
        var credPath = Path.Combine(credDir, "credentials.dat");
        var credService = new CredentialStoreService(credPath);

        var mockStorage = new InMemoryStorageProvider();
        using var vm = new MainViewModel(
            storageProvider: new InMemoryStorageProvider(),
            catalogRepository: new SqliteCatalogRepository(_catalogDbPath),
            credentialStoreService: credService,
            storageFactory: _ => mockStorage);

        vm.TelegramBotToken = "12345:TOKEN";
        vm.TelegramChatId = "-99999";

        await ((AsyncRelayCommand)vm.TestTelegramConnectionCommand).ExecuteAsync(null);

        vm.SettingsStatusMessage.Should().Contain("אומת בהצלחה");
        vm.StorageProvider.Should().BeSameAs(mockStorage);

        var saved = credService.LoadTelegramCredentials();
        saved.Should().NotBeNull();
        saved!.BotToken.Should().Be("12345:TOKEN");
        saved.ChatId.Should().Be("-99999");
    }

    [Fact]
    public async Task MainViewModel_InitializeAsync_WhenCredentialsExist_ShouldAutoLoad()
    {
        var credDir = Path.Combine(_testRoot, "creds_autoload");
        Directory.CreateDirectory(credDir);
        var credPath = Path.Combine(credDir, "credentials.dat");
        var credService = new CredentialStoreService(credPath);
        credService.SaveTelegramCredentials(new TelegramCredentials("SAVED_TOKEN", "SAVED_CHAT"));

        var mockStorage = new InMemoryStorageProvider();
        using var vm = new MainViewModel(
            storageProvider: new InMemoryStorageProvider(),
            catalogRepository: new SqliteCatalogRepository(_catalogDbPath),
            credentialStoreService: credService,
            storageFactory: _ => mockStorage);

        await vm.InitializeAsync();

        vm.TelegramBotToken.Should().Be("SAVED_TOKEN");
        vm.TelegramChatId.Should().Be("SAVED_CHAT");
        vm.StorageProvider.Should().BeSameAs(mockStorage);
        vm.SettingsStatusMessage.Should().Contain("נטענו בהצלחה");
    }

    [Fact]
    public async Task MainViewModel_LoadCatalogOnInitialization_ShouldPopulateBrowsedFilesAndSupportEmptyQuery()
    {
        var catalog = new SqliteCatalogRepository(_catalogDbPath);
        await catalog.InitializeAsync();

        // Seed a backup set, snapshot, and file versions
        var bsetId = BackupSetId.New();
        var bset = new BackupSet(bsetId, "DefaultBackupSet", ["C:\\Data"], [], DateTimeOffset.UtcNow);
        await catalog.SaveBackupSetAsync(bset);

        var snap = new Snapshot(SnapshotId.New(), bsetId, 1, DateTimeOffset.UtcNow, SnapshotStatus.Committed, 2, 2048, null);
        await catalog.CreateSnapshotAsync(snap);

        var entry1 = new FileEntry(FileEntryId.New(), bsetId, snap.Id, DateTimeOffset.UtcNow);
        var entry2 = new FileEntry(FileEntryId.New(), bsetId, snap.Id, DateTimeOffset.UtcNow);
        var ver1 = new FileVersion(FileVersionId.New(), entry1.Id, snap.Id, CanonicalPath.From("folder/doc1.txt"), 1024, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "hash1", FileEntryAttributes.None, []);
        var ver2 = new FileVersion(FileVersionId.New(), entry2.Id, snap.Id, CanonicalPath.From("photos/pic.jpg"), 1024, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "hash2", FileEntryAttributes.None, []);

        await catalog.SaveFileEntriesAndVersionsAsync([entry1, entry2], [ver1, ver2]);
        await catalog.CommitSnapshotAsync(snap.Id, 2, 2048);

        // Now initialize MainViewModel pointing to this catalog
        using var vm = new MainViewModel(
            storageProvider: new InMemoryStorageProvider(),
            catalogRepository: catalog);

        await vm.InitializeAsync();

        // Must be automatically loaded on startup!
        vm.TotalFilesCount.Should().Be(2);
        vm.BrowsedFiles.Should().HaveCount(2);
        vm.TriggerRestoreCommand.CanExecute(null).Should().BeTrue();

        // Empty search query must show all files
        vm.SearchQuery = "";
        vm.BrowsedFiles.Should().HaveCount(2);

        // Filtered search query
        vm.SearchQuery = "doc1";
        vm.BrowsedFiles.Should().ContainSingle().Which.RelativePath.Should().Contain("doc1.txt");

        // Back to empty search query
        vm.SearchQuery = null!;
        vm.BrowsedFiles.Should().HaveCount(2);
    }

    [Fact]
    public void MainViewModel_BrowseFolder_ShouldPickFolderAndAddToList()
    {
        var mockPicker = new MockFolderPickerService(@"C:\Users\Alice\Projects");
        using var vm = new MainViewModel(folderPickerService: mockPicker);
        vm.IncludedRoots.Clear();

        vm.BrowseFolderCommand.Execute(null);

        vm.IncludedRoots.Should().ContainSingle().Which.Should().Be(@"C:\Users\Alice\Projects");
        vm.SettingsStatusMessage.Should().Contain("נוספה בהצלחה");
    }

    [Fact]
    public void MainViewModel_BrowseFolder_WhenCanceled_ShouldNotModifyList()
    {
        var mockPicker = new MockFolderPickerService(null);
        using var vm = new MainViewModel(folderPickerService: mockPicker);
        vm.IncludedRoots.Clear();

        vm.BrowseFolderCommand.Execute(null);

        vm.IncludedRoots.Should().BeEmpty();
    }

    [Fact]
    public void MainViewModel_AddFolder_ShouldRejectDuplicatesAndWhitespace()
    {
        using var vm = new MainViewModel();
        vm.IncludedRoots.Clear();
        vm.NewRootPath = @"C:\Folder1";
        vm.AddFolderCommand.Execute(null);

        vm.IncludedRoots.Should().HaveCount(1);

        // Attempt duplicate
        vm.NewRootPath = @"C:\Folder1";
        vm.AddFolderCommand.Execute(null);
        vm.IncludedRoots.Should().HaveCount(1);
        vm.SettingsStatusMessage.Should().Contain("כבר קיימת");

        // Attempt whitespace
        vm.NewRootPath = "   ";
        vm.AddFolderCommand.Execute(null);
        vm.IncludedRoots.Should().HaveCount(1);
    }

    [Fact]
    public void MainViewModel_RemoveFolder_ByParameterOrSelection_ShouldRemoveProperly()
    {
        using var vm = new MainViewModel();
        vm.IncludedRoots.Clear();
        vm.IncludedRoots.Add(@"C:\FolderA");
        vm.IncludedRoots.Add(@"C:\FolderB");

        // Remove by selection
        vm.SelectedRootPath = @"C:\FolderA";
        vm.RemoveFolderCommand.Execute(null);

        vm.IncludedRoots.Should().ContainSingle().Which.Should().Be(@"C:\FolderB");
        vm.SelectedRootPath.Should().BeNull();
        vm.SettingsStatusMessage.Should().Contain("הוסרה בהצלחה");

        // Remove by parameter
        vm.RemoveFolderCommand.Execute(@"C:\FolderB");
        vm.IncludedRoots.Should().BeEmpty();
    }

    [Fact]
    public void MainViewModel_BrowseRestoreDestination_ShouldUpdateDestination()
    {
        var mockPicker = new MockFolderPickerService(@"D:\RestoredData");
        using var vm = new MainViewModel(folderPickerService: mockPicker);

        vm.BrowseRestoreDestinationCommand.Execute(null);

        vm.RestoreDestination.Should().Be(@"D:\RestoredData");
    }

    [Fact]
    public async Task MainViewModel_FolderManagement_ShouldPersistAndReloadFromCatalog()
    {
        var catalog = new SqliteCatalogRepository(_catalogDbPath);
        await catalog.InitializeAsync();

        using (var vm1 = new MainViewModel(catalogRepository: catalog))
        {
            await vm1.InitializeAsync();
            vm1.IncludedRoots.Clear();
            vm1.NewRootPath = @"C:\PersistedFolder1";
            vm1.AddFolderCommand.Execute(null);
            vm1.NewRootPath = @"C:\PersistedFolder2";
            vm1.AddFolderCommand.Execute(null);

            // Wait brief moment for async persist
            await vm1.PersistRootsConfigurationAsync();
        }

        // Initialize a new VM instance on the same catalog
        using (var vm2 = new MainViewModel(catalogRepository: catalog))
        {
            await vm2.InitializeAsync();

            vm2.IncludedRoots.Should().HaveCount(2);
            vm2.IncludedRoots.Should().Contain(@"C:\PersistedFolder1");
            vm2.IncludedRoots.Should().Contain(@"C:\PersistedFolder2");

            // Now remove one folder
            vm2.RemoveFolderCommand.Execute(@"C:\PersistedFolder1");
            await vm2.PersistRootsConfigurationAsync();
        }

        // Initialize a third VM instance to verify deletion persisted
        using (var vm3 = new MainViewModel(catalogRepository: catalog))
        {
            await vm3.InitializeAsync();

            vm3.IncludedRoots.Should().ContainSingle().Which.Should().Be(@"C:\PersistedFolder2");
        }
    }
    [Fact]
    public async Task MainViewModel_OpenSelectedFile_ShouldRestoreToPreviewAndLaunchProcess()
    {
        var file1 = Path.Combine(_sourceDir, "document.txt");
        await File.WriteAllTextAsync(file1, "Preview Content 2026");

        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);
        var mockLauncher = new MockProcessLauncher();

        using var vm = new MainViewModel(
            storageProvider: storage,
            catalogRepository: catalog,
            processLauncher: mockLauncher);

        vm.IncludedRoots.Clear();
        vm.IncludedRoots.Add(_sourceDir);
        await vm.InitializeAsync();

        // Run backup first so we have a manifest and master key
        await ((AsyncRelayCommand)vm.TriggerBackupCommand).ExecuteAsync(null);
        vm.BrowsedFiles.Should().NotBeEmpty();

        var selected = vm.BrowsedFiles.First(f => f.RelativePath.Contains("document.txt"));
        vm.SelectedFile = selected;

        // Execute OpenSelectedFileCommand
        await ((AsyncRelayCommand)vm.OpenSelectedFileCommand).ExecuteAsync(selected);

        mockLauncher.LaunchedFiles.Should().ContainSingle();
        var launched = mockLauncher.LaunchedFiles[0];
        launched.Should().Contain("BackupAppPreview");
        File.Exists(launched).Should().BeTrue();
        (await File.ReadAllTextAsync(launched)).Should().Be("Preview Content 2026");
    }

    [Fact]
    public async Task MainViewModel_RestoreSingleFile_ShouldRestoreToDestinationAndReportSuccess()
    {
        var file1 = Path.Combine(_sourceDir, "single_restore.txt");
        await File.WriteAllTextAsync(file1, "Single Restore Content");

        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);
        var mockLauncher = new MockProcessLauncher();

        using var vm = new MainViewModel(
            storageProvider: storage,
            catalogRepository: catalog,
            processLauncher: mockLauncher)
        {
            RestoreDestination = _restoreDir
        };

        vm.IncludedRoots.Clear();
        vm.IncludedRoots.Add(_sourceDir);
        await vm.InitializeAsync();

        await ((AsyncRelayCommand)vm.TriggerBackupCommand).ExecuteAsync(null);

        var item = vm.BrowsedFiles.First(f => f.RelativePath.Contains("single_restore.txt"));
        await ((AsyncRelayCommand)vm.RestoreSingleFileCommand).ExecuteAsync(item);

        vm.IsRestoreCompleted.Should().BeTrue();
        vm.HasRestoreError.Should().BeFalse();
        vm.HasRestoreStatusMessage.Should().BeTrue();
        vm.RestoreStatusMessage.Should().Contain("הקובץ שוחזר בהצלחה");

        var restored = Path.Combine(_restoreDir, "single_restore.txt");
        File.Exists(restored).Should().BeTrue();
        (await File.ReadAllTextAsync(restored)).Should().Be("Single Restore Content");
    }

    [Fact]
    public void MainViewModel_ShowFileInFolder_WhenLocalFileExists_ShouldLaunchExplorerWithSelect()
    {
        var mockLauncher = new MockProcessLauncher();
        using var vm = new MainViewModel(processLauncher: mockLauncher);

        var localFile = Path.Combine(_sourceDir, "existing.txt");
        File.WriteAllText(localFile, "Test");

        vm.IncludedRoots.Clear();
        vm.IncludedRoots.Add(_sourceDir);

        var manifestItem = new SnapshotManifestItem("existing.txt", 4, "hash", 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, []);
        var item = new FileItemViewModel(manifestItem);
        vm.ShowFileInFolderCommand.Execute(item);

        mockLauncher.LaunchedProcesses.Should().ContainSingle();
        var (file, args) = mockLauncher.LaunchedProcesses[0];
        file.Should().Be("explorer.exe");
        args.Should().Contain("/select,");
        args.Should().Contain("existing.txt");
    }

    [Fact]
    public void MainViewModel_OpenRestoreFolder_WhenDirectoryExists_ShouldLaunchFolder()
    {
        var mockLauncher = new MockProcessLauncher();
        Directory.CreateDirectory(_restoreDir);

        using var vm = new MainViewModel(processLauncher: mockLauncher)
        {
            RestoreDestination = _restoreDir
        };

        vm.OpenRestoreFolderCommand.Execute(null);

        mockLauncher.LaunchedFiles.Should().ContainSingle().Which.Should().Be(_restoreDir);
    }

    [Fact]
    public async Task MainViewModel_Restore_WhenDirectoryDoesNotExist_ShouldAutoCreateAndRestore()
    {
        var autoCreatedDir = Path.Combine(_testRoot, "auto_created_sub", "deep_restore");
        Directory.Exists(autoCreatedDir).Should().BeFalse();

        var file1 = Path.Combine(_sourceDir, "sample.txt");
        await File.WriteAllTextAsync(file1, "Auto Created Directory Test");

        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);

        using var vm = new MainViewModel(storageProvider: storage, catalogRepository: catalog)
        {
            RestoreDestination = autoCreatedDir
        };
        vm.IncludedRoots.Clear();
        vm.IncludedRoots.Add(_sourceDir);

        await vm.InitializeAsync();
        await ((AsyncRelayCommand)vm.TriggerBackupCommand).ExecuteAsync(null);

        // Execute full restore
        await ((AsyncRelayCommand)vm.TriggerRestoreCommand).ExecuteAsync(null);

        Directory.Exists(autoCreatedDir).Should().BeTrue();
        var restoredFile = Path.Combine(autoCreatedDir, "sample.txt");
        File.Exists(restoredFile).Should().BeTrue();
        (await File.ReadAllTextAsync(restoredFile)).Should().Be("Auto Created Directory Test");
        vm.IsRestoreCompleted.Should().BeTrue();
    }

    [Fact]
    public async Task MainViewModel_VersionHistory_ShouldDisplayVersionsAndRestoreHistoricalVersion()
    {
        var file1 = Path.Combine(_sourceDir, "history_doc.txt");
        await File.WriteAllTextAsync(file1, "First Version Content");

        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);

        using var vm = new MainViewModel(storageProvider: storage, catalogRepository: catalog)
        {
            RestoreDestination = _restoreDir
        };
        vm.IncludedRoots.Clear();
        vm.IncludedRoots.Add(_sourceDir);

        await vm.InitializeAsync();

        // 1. Initial backup
        await ((AsyncRelayCommand)vm.TriggerBackupCommand).ExecuteAsync(null);

        // 2. Modify file and perform second backup (incremental)
        await Task.Delay(20);
        await File.WriteAllTextAsync(file1, "Second Version Content Modified");
        File.SetLastWriteTimeUtc(file1, DateTime.UtcNow.AddMinutes(5));
        await ((AsyncRelayCommand)vm.TriggerBackupCommand).ExecuteAsync(null);

        // 3. Select file in browse tab and show file versions
        var fileItem = vm.BrowsedFiles.FirstOrDefault(f => f.RelativePath.Contains("history_doc.txt"));
        fileItem.Should().NotBeNull();
        vm.SelectedFile = fileItem;

        await ((AsyncRelayCommand)vm.ShowFileVersionsCommand).ExecuteAsync(fileItem);

        vm.IsVersionHistoryVisible.Should().BeTrue();
        vm.SelectedFileVersions.Should().HaveCount(2);

        // Version 0 should be latest (second), Version 1 should be oldest (first)
        var oldestVersion = vm.SelectedFileVersions.Last();
        vm.SelectedVersion = oldestVersion;

        // 4. Restore the older historical version directly
        await ((AsyncRelayCommand)vm.RestoreSelectedVersionCommand).ExecuteAsync(oldestVersion);

        vm.IsRestoreCompleted.Should().BeTrue();
        var restoredPath = Path.Combine(_restoreDir, "history_doc.txt");
        File.Exists(restoredPath).Should().BeTrue();
        (await File.ReadAllTextAsync(restoredPath)).Should().Be("First Version Content");
    }
}

public class MockFolderPickerService : IFolderPickerService
{
    public string? SelectedFolder { get; set; }
    public string? LastInitialDirectory { get; private set; }

    public MockFolderPickerService(string? selectedFolder = null)
    {
        SelectedFolder = selectedFolder;
    }

    public string? PickFolder(string? initialDirectory = null)
    {
        LastInitialDirectory = initialDirectory;
        return SelectedFolder;
    }
}

public class MockProcessLauncher : IProcessLauncher
{
    public List<string> LaunchedFiles { get; } = [];
    public List<(string FileName, string Arguments)> LaunchedProcesses { get; } = [];

    public void Start(string fileName, string? arguments = null)
    {
        if (string.IsNullOrEmpty(arguments))
        {
            LaunchedFiles.Add(fileName);
        }
        else
        {
            LaunchedProcesses.Add((fileName, arguments));
        }
    }
}
