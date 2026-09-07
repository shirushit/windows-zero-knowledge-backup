using System.Security.Cryptography;
using System.Text;
using BackupApp.Crypto;
using BackupApp.Domain;
using BackupApp.Storage;
using BackupApp.Storage.UploadQueue;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class Gate3VerificationTests
{
    [Fact]
    public async Task Gate3_EncryptedObjects_SurviveUploadDownloadInterruptionRetry_AndVerifyByteForByte()
    {
        // 1. Prepare Cryptographic Foundation
        using var master = MasterKey.Generate();
        var contentKey = master.DeriveContentKey();
        var crypto = new CryptoService();

        // 2. Prepare 3 Test Fixtures (Small text, Hebrew unicode text, and binary payload)
        var fixture1Plaintext = Encoding.UTF8.GetBytes("Small plaintext document content 2026.");
        var fixture2Plaintext = Encoding.UTF8.GetBytes("קובץ בעברית עם סימנים מיוחדים: !@#$%^&*()_+ בדיקת הצפנה ושחזור נתונים");
        var fixture3Plaintext = RandomNumberGenerator.GetBytes(64 * 1024); // 64KB binary buffer

        var fixtures = new Dictionary<string, byte[]>
        {
            ["fixture1"] = fixture1Plaintext,
            ["fixture2"] = fixture2Plaintext,
            ["fixture3"] = fixture3Plaintext
        };

        // 3. Encrypt fixtures into remote-ready envelopes
        var encryptedEnvelopes = new Dictionary<ObjectId, byte[]>();
        foreach (var (key, data) in fixtures)
        {
            var id = ObjectId.FromHex(Convert.ToHexString(SHA256.HashData(data)));
            var aad = Encoding.UTF8.GetBytes($"aad-{key}");
            var envelope = crypto.Encrypt(data, contentKey, aad);
            encryptedEnvelopes[id] = envelope.ToBytes();
        }

        // 4. Configure Storage with Simulated Transient Failure (Fail 2 times per object before succeeding)
        var storage = new InMemoryStorageProvider
        {
            FailNTimesPerObject = 2,
            SimulatedLatencyMs = 1
        };

        var retryPolicy = new StorageRetryPolicy(maxRetries: 4, initialDelay: TimeSpan.FromMilliseconds(5));

        // 5. Configure Persistent Upload Queue
        var tempQueueFile = Path.Combine(Path.GetTempPath(), $"gate3_queue_{Guid.NewGuid():N}.json");
        var tempFiles = new List<string>();

        try
        {
            using (var queue = new UploadQueueService(tempQueueFile))
            {
                var jobId = JobId.New();

                foreach (var (id, encryptedBytes) in encryptedEnvelopes)
                {
                    var tempFilePath = Path.Combine(Path.GetTempPath(), $"payload_{id.Value}.tmp");
                    await File.WriteAllBytesAsync(tempFilePath, encryptedBytes);
                    tempFiles.Add(tempFilePath);

                    await queue.EnqueueAsync(id, tempFilePath, jobId);
                }

                // Process queue with retry policy
                var pendingItems = await queue.GetPendingItemsAsync(10);
                pendingItems.Should().HaveCount(3);

                int retryCallbacks = 0;
                foreach (var item in pendingItems)
                {
                    await queue.MarkInProgressAsync(item.QueueItemId);

                    var id = ObjectId.FromHex(item.ObjectIdValue);
                    var payloadBytes = await File.ReadAllBytesAsync(item.PayloadFilePath);
                    using var stream = new MemoryStream(payloadBytes);

                    var descriptor = await retryPolicy.ExecuteWithRetryAsync(
                        ct => storage.PutObjectAsync(id, stream, cancellationToken: ct),
                        onRetry: (ex, attempt, delay) => retryCallbacks++
                    );

                    descriptor.Should().NotBeNull();
                    await queue.MarkCompletedAsync(item.QueueItemId);
                }

                // Each object failed 2 times, so 3 * 2 = 6 retries occurred
                retryCallbacks.Should().Be(6);

                var remainingPending = await queue.GetPendingCountAsync();
                remainingPending.Should().Be(0);
            }

            // 6. Download objects from storage and decrypt
            foreach (var (key, originalData) in fixtures)
            {
                var id = ObjectId.FromHex(Convert.ToHexString(SHA256.HashData(originalData)));
                var exists = await storage.ExistsAsync(id);
                exists.Should().BeTrue();

                using var downloadedStream = await storage.GetObjectAsync(id);
                using var memory = new MemoryStream();
                await downloadedStream.CopyToAsync(memory);

                var downloadedBytes = memory.ToArray();

                // Decrypt envelope
                var aad = Encoding.UTF8.GetBytes($"aad-{key}");
                var decryptedBytes = crypto.Decrypt(downloadedBytes, contentKey, aad);

                // 7. Verify byte-for-byte exact equality
                decryptedBytes.Should().BeEquivalentTo(originalData,
                    $"Decrypted data for {key} must exactly match original fixture bytes");
            }
        }
        finally
        {
            if (File.Exists(tempQueueFile))
            {
                File.Delete(tempQueueFile);
            }
            foreach (var file in tempFiles)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }
    }
}
