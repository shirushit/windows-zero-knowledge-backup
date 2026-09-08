using BackupApp.Catalog;
using BackupApp.Domain;
using BackupApp.Storage;
using BackupApp.UI.Services;
using BackupApp.UI.ViewModels;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class Gate6VerificationTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _sourceDir;
    private readonly string _restoreDir;
    private readonly string _catalogDbPath;

    public Gate6VerificationTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"gate6_ui_{Guid.NewGuid():N}");
        _sourceDir = Path.Combine(_testRoot, "source_data");
        _restoreDir = Path.Combine(_testRoot, "restored_via_ui");
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
    public async Task Gate6_CoreBackupAndRestore_CompletedWithoutCli_TruthfulStatusAndRtlChecksPass()
    {
        // -------------------------------------------------------------
        // STEP 1: SETUP TEST DATA WITH MIXED HEBREW/ENGLISH PATHS
        // -------------------------------------------------------------
        var subDir = Path.Combine(_sourceDir, "תיקיית מסמכים 2026");
        Directory.CreateDirectory(subDir);

        var docPath = Path.Combine(subDir, "דוח פעילות רבעוני.txt");
        var engPath = Path.Combine(_sourceDir, "system_specs.json");

        await File.WriteAllTextAsync(docPath, "תוכן רגיש בעברית - גיבוי מאובטח");
        await File.WriteAllTextAsync(engPath, "{\"version\": \"1.0.0\", \"encrypted\": true}");

        // -------------------------------------------------------------
        // STEP 2: VERIFY RTL / LRM PATH FORMATTING
        // -------------------------------------------------------------
        var formattedRtl = PathFormatter.FormatForRtl(docPath);
        formattedRtl.Should().StartWith(PathFormatter.Lrm.ToString());
        formattedRtl.Should().EndWith(PathFormatter.Lrm.ToString());
        formattedRtl.Should().Contain("דוח פעילות רבעוני.txt");

        // -------------------------------------------------------------
        // STEP 3: EXECUTE BACKUP VIA UI VIEWMODEL (NO CLI)
        // -------------------------------------------------------------
        var storage = new InMemoryStorageProvider();
        var catalog = new SqliteCatalogRepository(_catalogDbPath);

        using var viewModel = new MainViewModel(storage, catalog)
        {
            RestoreDestination = _restoreDir
        };
        viewModel.IncludedRoots.Clear();
        viewModel.IncludedRoots.Add(_sourceDir);

        await viewModel.InitializeAsync();

        // Check truthful initial status
        viewModel.Status.Should().Be(ProtectionState.Protected);
        viewModel.IsBusy.Should().BeFalse();

        // Run backup through UI command
        await ((AsyncRelayCommand)viewModel.TriggerBackupCommand).ExecuteAsync(null);

        // Verify truthful completed status
        viewModel.IsBusy.Should().BeFalse();
        viewModel.Status.Should().Be(ProtectionState.Protected);
        viewModel.StatusTitle.Should().Contain("הושלם בהצלחה");
        viewModel.TotalFilesCount.Should().Be(2);
        viewModel.BrowsedFiles.Should().HaveCount(2);

        // -------------------------------------------------------------
        // STEP 4: VERIFY FILE SEARCH IN UI
        // -------------------------------------------------------------
        viewModel.SearchQuery = "פעילות";
        viewModel.BrowsedFiles.Should().ContainSingle()
            .Which.RelativePath.Should().Contain("דוח פעילות רבעוני.txt");

        viewModel.SearchQuery = "system_specs";
        viewModel.BrowsedFiles.Should().ContainSingle()
            .Which.RelativePath.Should().Contain("system_specs.json");

        viewModel.SearchQuery = string.Empty;
        viewModel.BrowsedFiles.Should().HaveCount(2);

        // -------------------------------------------------------------
        // STEP 5: EXECUTE RESTORE VIA UI VIEWMODEL (NO CLI)
        // -------------------------------------------------------------
        viewModel.TriggerRestoreCommand.CanExecute(null).Should().BeTrue();
        await ((AsyncRelayCommand)viewModel.TriggerRestoreCommand).ExecuteAsync(null);

        viewModel.IsBusy.Should().BeFalse();
        viewModel.Status.Should().Be(ProtectionState.Protected);
        viewModel.StatusTitle.Should().Contain("השחזור הושלם בהצלחה");

        // -------------------------------------------------------------
        // STEP 6: VERIFY RESTORED FILES
        // -------------------------------------------------------------
        var restoredDoc = Path.Combine(_restoreDir, "תיקיית מסמכים 2026", "דוח פעילות רבעוני.txt");
        var restoredEng = Path.Combine(_restoreDir, "system_specs.json");

        File.Exists(restoredDoc).Should().BeTrue("Hebrew document must be restored accurately by UI");
        File.Exists(restoredEng).Should().BeTrue("English spec file must be restored accurately by UI");

        var restoredDocContent = await File.ReadAllTextAsync(restoredDoc);
        var restoredEngContent = await File.ReadAllTextAsync(restoredEng);

        restoredDocContent.Should().Be("תוכן רגיש בעברית - גיבוי מאובטח");
        restoredEngContent.Should().Be("{\"version\": \"1.0.0\", \"encrypted\": true}");
    }
}
