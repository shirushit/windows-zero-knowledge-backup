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
}
