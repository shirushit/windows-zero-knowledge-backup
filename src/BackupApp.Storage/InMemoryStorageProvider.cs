using System.Collections.Concurrent;
using System.Security.Cryptography;
using BackupApp.Domain;

namespace BackupApp.Storage;

public sealed class InMemoryStorageProvider : IStorageProvider
{
    private readonly ConcurrentDictionary<string, (byte[] Data, string Hash, DateTimeOffset UploadedAt)> _storage = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _failureCounters = new(StringComparer.Ordinal);
    private readonly List<ObjectId> _catalogAnchors = [];
    private readonly object _lock = new();

    public string ProviderId => "in-memory";
    public int SimulatedLatencyMs { get; set; }
    public bool IsDisconnected { get; set; }
    public int FailNTimesPerObject { get; set; }

    public Task<StorageCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new StorageCapabilities(
            MaxObjectSizeBytes: 100 * 1024 * 1024, // 100MB
            SupportsDeletion: true,
            SupportsRangeRequests: true,
            RecommendedChunkSizeBytes: 8 * 1024 * 1024 // 8MB
        ));
    }

    public async Task<RemoteObjectDescriptor> PutObjectAsync(
        ObjectId id,
        Stream contentStream,
        IProgress<long>? progress = null,
        bool isCatalogAnchor = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentStream);
        CheckConnectivity();
        await SimulateDelayAsync(cancellationToken).ConfigureAwait(false);
        CheckSimulatedFailure(id);

        using var memory = new MemoryStream();
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[64 * 1024];
        int bytesRead;
        long totalBytes = 0;

        while ((bytesRead = await contentStream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            memory.Write(buffer, 0, bytesRead);
            sha.AppendData(buffer, 0, bytesRead);
            totalBytes += bytesRead;
            progress?.Report(totalBytes);
        }

        var hashHex = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        _storage[id.Value] = (memory.ToArray(), hashHex, now);

        lock (_lock)
        {
            if (!_catalogAnchors.Contains(id) && (isCatalogAnchor || id.Value.Contains("manifest", StringComparison.OrdinalIgnoreCase)))
            {
                _catalogAnchors.Add(id);
            }
        }

        return new RemoteObjectDescriptor(id, totalBytes, hashHex, now, $"mem://{id.Value}");
    }

    public async Task<Stream> GetObjectAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        CheckConnectivity();
        await SimulateDelayAsync(cancellationToken).ConfigureAwait(false);
        CheckSimulatedFailure(id);

        if (!_storage.TryGetValue(id.Value, out var item))
        {
            throw new KeyNotFoundException($"Object '{id.Value}' was not found in storage.");
        }

        return new MemoryStream(item.Data, writable: false);
    }

    public async Task<bool> ExistsAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        CheckConnectivity();
        await SimulateDelayAsync(cancellationToken).ConfigureAwait(false);
        return _storage.ContainsKey(id.Value);
    }

    public async Task<bool> DeleteAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        CheckConnectivity();
        await SimulateDelayAsync(cancellationToken).ConfigureAwait(false);
        var removed = _storage.TryRemove(id.Value, out _);
        lock (_lock)
        {
            _catalogAnchors.Remove(id);
        }
        return removed;
    }

    public async Task<IReadOnlyList<ObjectId>> EnumerateCatalogAnchorsAsync(CancellationToken cancellationToken = default)
    {
        CheckConnectivity();
        await SimulateDelayAsync(cancellationToken).ConfigureAwait(false);
        lock (_lock)
        {
            return _catalogAnchors.ToList();
        }
    }

    public void RegisterCatalogAnchor(ObjectId id)
    {
        lock (_lock)
        {
            if (!_catalogAnchors.Contains(id))
            {
                _catalogAnchors.Add(id);
            }
        }
    }

    public byte[]? GetStoredBytes(ObjectId id)
    {
        return _storage.TryGetValue(id.Value, out var item) ? (byte[])item.Data.Clone() : null;
    }

    private void CheckConnectivity()
    {
        if (IsDisconnected)
        {
            throw new HttpRequestException("Simulated network disconnection: storage provider is unreachable.");
        }
    }

    private void CheckSimulatedFailure(ObjectId id)
    {
        if (FailNTimesPerObject > 0)
        {
            var attempts = _failureCounters.AddOrUpdate(id.Value, 1, (_, count) => count + 1);
            if (attempts <= FailNTimesPerObject)
            {
                throw new IOException($"Simulated transient network failure on object '{id.Value}' (Attempt {attempts}/{FailNTimesPerObject}).");
            }
        }
    }

    private async Task SimulateDelayAsync(CancellationToken cancellationToken)
    {
        if (SimulatedLatencyMs > 0)
        {
            await Task.Delay(SimulatedLatencyMs, cancellationToken).ConfigureAwait(false);
        }
    }
}
