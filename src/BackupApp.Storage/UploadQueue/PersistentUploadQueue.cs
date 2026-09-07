using System.Text.Json;
using System.Text.Json.Serialization;
using BackupApp.Domain;

namespace BackupApp.Storage.UploadQueue;

public enum UploadItemStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}

public sealed record UploadQueueItem(
    string QueueItemId,
    string ObjectIdValue,
    string PayloadFilePath,
    string JobIdValue,
    UploadItemStatus Status,
    int Attempts,
    string? LastError,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc
);

public interface IUploadQueueService
{
    Task EnqueueAsync(ObjectId objectId, string payloadFilePath, JobId jobId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<UploadQueueItem>> GetPendingItemsAsync(int maxCount, CancellationToken cancellationToken = default);
    Task MarkInProgressAsync(string queueItemId, CancellationToken cancellationToken = default);
    Task MarkCompletedAsync(string queueItemId, CancellationToken cancellationToken = default);
    Task MarkFailedAsync(string queueItemId, string errorMessage, bool canRetry, CancellationToken cancellationToken = default);
    Task RecoverStaleItemsAsync(TimeSpan staleTimeout, CancellationToken cancellationToken = default);
    Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default);
}

public sealed class UploadQueueService : IUploadQueueService, IDisposable
{
    private readonly string _queueFilePath;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<UploadQueueItem> _items = [];
    private bool _disposed;

    public UploadQueueService(string? queueFilePath = null)
    {
        _queueFilePath = queueFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BackupApp",
            "upload_queue.json"
        );

        var dir = Path.GetDirectoryName(_queueFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        LoadFromFile();
    }

    public async Task EnqueueAsync(ObjectId objectId, string payloadFilePath, JobId jobId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = DateTimeOffset.UtcNow;
            var item = new UploadQueueItem(
                QueueItemId: Guid.NewGuid().ToString("N"),
                ObjectIdValue: objectId.Value,
                PayloadFilePath: payloadFilePath,
                JobIdValue: jobId.Value.ToString(),
                Status: UploadItemStatus.Pending,
                Attempts: 0,
                LastError: null,
                CreatedAtUtc: now,
                UpdatedAtUtc: now
            );

            _items.Add(item);
            await SaveToFileAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<UploadQueueItem>> GetPendingItemsAsync(int maxCount, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _items
                .Where(i => i.Status == UploadItemStatus.Pending)
                .Take(maxCount)
                .ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task MarkInProgressAsync(string queueItemId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var idx = _items.FindIndex(i => i.QueueItemId == queueItemId);
            if (idx >= 0)
            {
                var cur = _items[idx];
                _items[idx] = cur with
                {
                    Status = UploadItemStatus.InProgress,
                    Attempts = cur.Attempts + 1,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                await SaveToFileAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task MarkCompletedAsync(string queueItemId, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var idx = _items.FindIndex(i => i.QueueItemId == queueItemId);
            if (idx >= 0)
            {
                var cur = _items[idx];
                _items[idx] = cur with
                {
                    Status = UploadItemStatus.Completed,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                await SaveToFileAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task MarkFailedAsync(string queueItemId, string errorMessage, bool canRetry, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var idx = _items.FindIndex(i => i.QueueItemId == queueItemId);
            if (idx >= 0)
            {
                var cur = _items[idx];
                _items[idx] = cur with
                {
                    Status = canRetry ? UploadItemStatus.Pending : UploadItemStatus.Failed,
                    LastError = errorMessage,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                };
                await SaveToFileAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RecoverStaleItemsAsync(TimeSpan staleTimeout, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var cutoff = DateTimeOffset.UtcNow - staleTimeout;
            bool modified = false;

            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Status == UploadItemStatus.InProgress && _items[i].UpdatedAtUtc < cutoff)
                {
                    _items[i] = _items[i] with
                    {
                        Status = UploadItemStatus.Pending,
                        UpdatedAtUtc = DateTimeOffset.UtcNow
                    };
                    modified = true;
                }
            }

            if (modified)
            {
                await SaveToFileAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _items.Count(i => i.Status == UploadItemStatus.Pending || i.Status == UploadItemStatus.InProgress);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void LoadFromFile()
    {
        if (File.Exists(_queueFilePath))
        {
            try
            {
                var json = File.ReadAllText(_queueFilePath);
                _items = JsonSerializer.Deserialize(json, UploadQueueJsonContext.Default.ListUploadQueueItem) ?? [];
            }
            catch
            {
                _items = [];
            }
        }
    }

    private async Task SaveToFileAsync(CancellationToken cancellationToken)
    {
        var tempPath = _queueFilePath + ".tmp";
        var json = JsonSerializer.Serialize(_items, UploadQueueJsonContext.Default.ListUploadQueueItem);
        await File.WriteAllTextAsync(tempPath, json, cancellationToken).ConfigureAwait(false);
        File.Move(tempPath, _queueFilePath, overwrite: true);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _lock.Dispose();
            _disposed = true;
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(List<UploadQueueItem>))]
internal sealed partial class UploadQueueJsonContext : JsonSerializerContext
{
}
