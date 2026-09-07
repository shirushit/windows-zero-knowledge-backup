using BackupApp.BackupEngine.Scanner;
using BackupApp.Domain;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class FileDiscoveryScannerTests : IDisposable
{
    private readonly string _testRoot;

    public FileDiscoveryScannerTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), $"scanner_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void PathNormalizer_ShouldResolveValidRelativePath()
    {
        var filePath = Path.Combine(_testRoot, "sub", "doc.txt");
        var canonical = PathNormalizer.ToCanonicalPath(_testRoot, filePath);

        canonical.Value.Should().Be("sub/doc.txt");
    }

    [Fact]
    public void PathNormalizer_ShouldHandleHebrewFilenames()
    {
        var filePath = Path.Combine(_testRoot, "מסמכים", "דוח_סופי.pdf");
        var canonical = PathNormalizer.ToCanonicalPath(_testRoot, filePath);

        canonical.Value.Should().Be("מסמכים/דוח_סופי.pdf");
    }

    [Fact]
    public void PathNormalizer_ShouldRejectPathOutsideRoot()
    {
        var outside = Path.Combine(Path.GetTempPath(), "outside.txt");
        var act = () => PathNormalizer.ToCanonicalPath(_testRoot, outside);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*not under root*");
    }

    [Fact]
    public void ExclusionFilter_ShouldFilterPatternsCorrectly()
    {
        var filter = new ExclusionFilter(["*.tmp", "Thumbs.db", "node_modules/**", "temp/*"]);

        filter.IsExcluded(CanonicalPath.From("file.tmp")).Should().BeTrue();
        filter.IsExcluded(CanonicalPath.From("Thumbs.db")).Should().BeTrue();
        filter.IsExcluded(CanonicalPath.From("sub/Thumbs.db")).Should().BeTrue();
        filter.IsExcluded(CanonicalPath.From("node_modules/express/index.js")).Should().BeTrue();
        filter.IsExcluded(CanonicalPath.From("temp/cache.dat")).Should().BeTrue();

        filter.IsExcluded(CanonicalPath.From("documents/resume.pdf")).Should().BeFalse();
        filter.IsExcluded(CanonicalPath.From("photos/vacation.jpg")).Should().BeFalse();
    }

    [Fact]
    public async Task FileDiscoveryScanner_ShouldDiscoverIncludedFilesAndSkipExcluded()
    {
        // Setup folder structure
        var docsDir = Path.Combine(_testRoot, "docs");
        var tempDir = Path.Combine(_testRoot, "temp");
        Directory.CreateDirectory(docsDir);
        Directory.CreateDirectory(tempDir);

        File.WriteAllText(Path.Combine(_testRoot, "root.txt"), "hello root");
        File.WriteAllText(Path.Combine(docsDir, "report.pdf"), "content report");
        File.WriteAllText(Path.Combine(docsDir, "draft.tmp"), "temp file");
        File.WriteAllText(Path.Combine(docsDir, "Thumbs.db"), "dummy db");
        File.WriteAllText(Path.Combine(tempDir, "cached.bin"), "cached data");

        var filter = new ExclusionFilter(["*.tmp", "Thumbs.db", "temp/**"]);
        var scanner = new FileDiscoveryScanner();

        var discovered = new List<DiscoveredFile>();
        await foreach (var file in scanner.DiscoverFilesAsync(_testRoot, filter))
        {
            discovered.Add(file);
        }

        var relativePaths = discovered.Select(d => d.Path.Value).ToList();

        relativePaths.Should().Contain("root.txt");
        relativePaths.Should().Contain("docs/report.pdf");

        relativePaths.Should().NotContain("docs/draft.tmp");
        relativePaths.Should().NotContain("docs/Thumbs.db");
        relativePaths.Should().NotContain("temp/cached.bin");
    }

    [Fact]
    public async Task FileDiscoveryScanner_ShouldThrowWhenRootDoesNotExist()
    {
        var scanner = new FileDiscoveryScanner();
        var nonExistent = Path.Combine(_testRoot, "non_existent");
        var filter = new ExclusionFilter();

        var act = async () =>
        {
            await foreach (var _ in scanner.DiscoverFilesAsync(nonExistent, filter))
            {
            }
        };

        await act.Should().ThrowAsync<DirectoryNotFoundException>();
    }
}
