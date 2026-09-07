using System.Security.Cryptography;
using System.Text;
using BackupApp.BackupEngine.Capture;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class StableFileCaptureTests : IDisposable
{
    private readonly string _testDir;
    private readonly StableFileCaptureService _captureService;

    public StableFileCaptureTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"capture_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _captureService = new StableFileCaptureService();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch
        {
            // Best effort
        }
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task StreamingHasher_ShouldComputeKnownSha256()
    {
        var inputBytes = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");
        using var stream = new MemoryStream(inputBytes);

        var hash = await StreamingHasher.ComputeHashSha256Async(stream);
        hash.Should().Be("d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592");
    }

    [Fact]
    public async Task StableFileCapture_SmallFile_ShouldSucceedWithOneChunk()
    {
        var filePath = Path.Combine(_testDir, "sample.txt");
        var content = "This is a simple test document for zero-knowledge backup capture.";
        await File.WriteAllTextAsync(filePath, content);

        var result = await _captureService.CaptureFileAsync(filePath);

        result.Status.Should().Be(StableCaptureStatus.Success);
        result.SizeBytes.Should().Be(Encoding.UTF8.GetByteCount(content));
        result.ContentHashSha256.Should().NotBeNullOrWhiteSpace();
        result.Chunks.Should().ContainSingle();
        result.Chunks[0].PlaintextSha256.Should().Be(result.ContentHashSha256);
    }

    [Fact]
    public async Task StableFileCapture_EmptyFile_ShouldSucceedWithEmptyChunk()
    {
        var filePath = Path.Combine(_testDir, "empty.bin");
        await File.WriteAllBytesAsync(filePath, []);

        var result = await _captureService.CaptureFileAsync(filePath);

        result.Status.Should().Be(StableCaptureStatus.Success);
        result.SizeBytes.Should().Be(0);
        var expectedEmptyHash = Convert.ToHexString(SHA256.HashData([])).ToLowerInvariant();
        result.ContentHashSha256.Should().Be(expectedEmptyHash);
        result.Chunks.Should().ContainSingle();
    }

    [Fact]
    public async Task StableFileCapture_MultiChunk_ShouldSplitAccordingToSize()
    {
        var filePath = Path.Combine(_testDir, "larger.bin");
        var randomData = new byte[150 * 1024]; // 150 KB
        Random.Shared.NextBytes(randomData);
        await File.WriteAllBytesAsync(filePath, randomData);

        // Max chunk size 64 KB -> should yield 3 chunks: 64KB, 64KB, 22KB
        var result = await _captureService.CaptureFileAsync(filePath, maxChunkSizeBytes: 64 * 1024);

        result.Status.Should().Be(StableCaptureStatus.Success);
        result.SizeBytes.Should().Be(150 * 1024);
        result.Chunks.Should().HaveCount(3);
        result.Chunks[0].SizeBytes.Should().Be(64 * 1024);
        result.Chunks[1].SizeBytes.Should().Be(64 * 1024);
        result.Chunks[2].SizeBytes.Should().Be(22 * 1024);
        result.Chunks.Select(c => c.Position).Should().ContainInOrder(0, 1, 2);
    }

    [Fact]
    public async Task StableFileCapture_NonExistentFile_ShouldReturnNotFound()
    {
        var missingPath = Path.Combine(_testDir, "missing.dat");
        var result = await _captureService.CaptureFileAsync(missingPath);

        result.Status.Should().Be(StableCaptureStatus.NotFound);
        result.ErrorMessage.Should().Contain("not found");
    }

    [Fact]
    public async Task StableFileCapture_LockedFile_ShouldReportLocked()
    {
        var lockedPath = Path.Combine(_testDir, "locked.dat");
        await File.WriteAllTextAsync(lockedPath, "locked content");

        // Lock file exclusively with FileShare.None
        await using var lockStream = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        var result = await _captureService.CaptureFileAsync(lockedPath, maxRetries: 0);

        result.Status.Should().Be(StableCaptureStatus.Locked);
        result.ErrorMessage.Should().Contain("locked");
    }
}
