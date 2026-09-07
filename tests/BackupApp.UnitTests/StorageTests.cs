using System.Text;
using BackupApp.Domain;
using BackupApp.Storage;
using BackupApp.Storage.UploadQueue;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class StorageTests
{
    [Fact]
    public async Task InMemoryStorageProvider_PutAndGet_ShouldSucceedWithExactBytes()
    {
        var provider = new InMemoryStorageProvider();
        var id = ObjectId.FromHex("abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789");
        var content = Encoding.UTF8.GetBytes("Test storage payload data");
        using var stream = new MemoryStream(content);

        long lastProgress = 0;
        var progress = new Progress<long>(p => lastProgress = p);

        var descriptor = await provider.PutObjectAsync(id, stream, progress);
        descriptor.Id.Should().Be(id);
        descriptor.SizeBytes.Should().Be(content.Length);
        lastProgress.Should().Be(content.Length);

        var exists = await provider.ExistsAsync(id);
        exists.Should().BeTrue();

        using var downloadedStream = await provider.GetObjectAsync(id);
        using var memory = new MemoryStream();
        await downloadedStream.CopyToAsync(memory);

        memory.ToArray().Should().BeEquivalentTo(content);

        var deleted = await provider.DeleteAsync(id);
        deleted.Should().BeTrue();

        var existsAfter = await provider.ExistsAsync(id);
        existsAfter.Should().BeFalse();
    }

    [Fact]
    public async Task InMemoryStorageProvider_Disconnected_ShouldThrowHttpRequestException()
    {
        var provider = new InMemoryStorageProvider { IsDisconnected = true };
        var id = ObjectId.FromHex("1111111111111111111111111111111111111111111111111111111111111111");
        using var stream = new MemoryStream(new byte[10]);

        var act = () => provider.PutObjectAsync(id, stream);
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task StorageRetryPolicy_ShouldRetryTransientErrorsAndSucceed()
    {
        var retryPolicy = new StorageRetryPolicy(maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(10));
        int attempts = 0;

        var result = await retryPolicy.ExecuteWithRetryAsync(ct =>
        {
            attempts++;
            if (attempts < 3)
            {
                throw new IOException("Simulated transient connection reset.");
            }
            return Task.FromResult(42);
        });

        result.Should().Be(42);
        attempts.Should().Be(3);
    }

    [Fact]
    public async Task StorageRetryPolicy_NonTransientError_ShouldNotRetry()
    {
        var retryPolicy = new StorageRetryPolicy(maxRetries: 3, initialDelay: TimeSpan.FromMilliseconds(10));
        int attempts = 0;

        var act = () => retryPolicy.ExecuteWithRetryAsync<int>(ct =>
        {
            attempts++;
            throw new InvalidOperationException("Fatal logic error.");
        });

        await act.Should().ThrowAsync<InvalidOperationException>();
        attempts.Should().Be(1);
    }

    [Fact]
    public async Task UploadQueueService_LifecycleAndPersistence_ShouldSurviveRestarts()
    {
        var queueFile = Path.Combine(Path.GetTempPath(), $"queue_test_{Guid.NewGuid():N}.json");
        try
        {
            using (var queue1 = new UploadQueueService(queueFile))
            {
                var id = ObjectId.FromHex("2222222222222222222222222222222222222222222222222222222222222222");
                var jobId = JobId.New();
                await queue1.EnqueueAsync(id, "C:\\temp\\chunk1.bin", jobId);

                var pending = await queue1.GetPendingItemsAsync(10);
                pending.Should().ContainSingle();
                pending[0].ObjectIdValue.Should().Be(id.Value);

                await queue1.MarkInProgressAsync(pending[0].QueueItemId);
            }

            // Re-open in a new queue service instance (simulating app restart after crash)
            using (var queue2 = new UploadQueueService(queueFile))
            {
                // InProgress item from previous crashed session should be recovered to Pending
                await queue2.RecoverStaleItemsAsync(TimeSpan.Zero);

                var pendingAfterCrash = await queue2.GetPendingItemsAsync(10);
                pendingAfterCrash.Should().ContainSingle();
                pendingAfterCrash[0].Attempts.Should().Be(1);

                await queue2.MarkCompletedAsync(pendingAfterCrash[0].QueueItemId);

                var pendingFinal = await queue2.GetPendingItemsAsync(10);
                pendingFinal.Should().BeEmpty();
            }
        }
        finally
        {
            if (File.Exists(queueFile))
            {
                File.Delete(queueFile);
            }
        }
    }
}
