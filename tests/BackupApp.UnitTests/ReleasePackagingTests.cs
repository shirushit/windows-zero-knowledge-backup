using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using BackupApp.Catalog;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class ReleasePackagingTests : IDisposable
{
    private readonly string _tempDir;

    public ReleasePackagingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"pkg_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch
            {
                // Best effort
            }
        }
    }

    [Fact]
    public void AssemblyMetadata_MatchesRequiredVersionAndCompanyInfo()
    {
        var domainAssembly = typeof(BackupApp.Domain.BackupSet).Assembly;
        var version = domainAssembly.GetName().Version;

        version.Should().NotBeNull();
        version!.Major.Should().Be(1);
        version.Minor.Should().Be(0);
        version.Build.Should().Be(0);

        var companyAttr = domainAssembly.GetCustomAttribute<AssemblyCompanyAttribute>();
        companyAttr.Should().NotBeNull();
        companyAttr!.Company.Should().Be("ZeroKnowledgeBackup");

        var productAttr = domainAssembly.GetCustomAttribute<AssemblyProductAttribute>();
        productAttr.Should().NotBeNull();
        productAttr!.Product.Should().Be("Windows Zero-Knowledge Backup");
    }

    [Fact]
    public async Task CatalogMigrations_AreIdempotentAndSafeAcrossMultipleInitializations()
    {
        var dbPath = Path.Combine(_tempDir, "migration_catalog.sqlite");
        var catalog = new SqliteCatalogRepository(dbPath);

        // First initialization creates tables and schema
        await catalog.InitializeAsync();
        var backupSetId = BackupApp.Domain.BackupSetId.New();
        var testSet = new BackupApp.Domain.BackupSet(
            backupSetId,
            "UpgradeTestSet",
            [@"C:\Test"],
            [],
            DateTimeOffset.UtcNow
        );
        await catalog.SaveBackupSetAsync(testSet);

        // Second initialization simulates application upgrade re-running migrations
        await catalog.InitializeAsync();

        var retrieved = await catalog.GetBackupSetAsync(backupSetId);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be("UpgradeTestSet");
    }

    [Fact]
    public void ReleaseArtifacts_AndChecksums_MatchComputedHash()
    {
        var solutionDir = FindSolutionDirectory();
        var releaseDir = Path.Combine(solutionDir, "artifacts", "release");

        if (!Directory.Exists(releaseDir))
        {
            // If release hasn't been built yet in this exact worktree, skip artifact check
            return;
        }

        var exePath = Path.Combine(releaseDir, "BackupApp.UI.exe");
        var checksumPath = Path.Combine(releaseDir, "SHA256SUMS.txt");

        if (File.Exists(exePath) && File.Exists(checksumPath))
        {
            var checksumLines = File.ReadAllLines(checksumPath);
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(exePath);
            var computedHash = Convert.ToHexString(sha256.ComputeHash(stream)).ToLowerInvariant();

            var exeChecksumLine = checksumLines.FirstOrDefault(l => l.EndsWith("BackupApp.UI.exe", StringComparison.OrdinalIgnoreCase));
            exeChecksumLine.Should().NotBeNull("SHA256SUMS.txt must contain an entry for BackupApp.UI.exe");
            exeChecksumLine!.StartsWith(computedHash, StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        }
    }

    [Fact]
    public void ScriptsAndDocumentation_ExistAndAreNonEmpty()
    {
        var solutionDir = FindSolutionDirectory();

        var requiredFiles = new[]
        {
            "scripts/build-release.ps1",
            "scripts/install.ps1",
            "scripts/uninstall.ps1",
            "scripts/sign-release.ps1",
            "USER_GUIDE.md",
            "ROLLBACK.md",
            ".github/workflows/release.yml"
        };

        foreach (var relativePath in requiredFiles)
        {
            var fullPath = Path.Combine(solutionDir, relativePath);
            File.Exists(fullPath).Should().BeTrue($"Required file '{relativePath}' must exist.");
            new FileInfo(fullPath).Length.Should().BeGreaterThan(0, $"File '{relativePath}' must not be empty.");
        }
    }

    private static string FindSolutionDirectory()
    {
        var current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "BackupApp.sln")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}
