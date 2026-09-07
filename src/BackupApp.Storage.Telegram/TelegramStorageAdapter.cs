using BackupApp.Domain;

namespace BackupApp.Storage.Telegram;

public sealed class TelegramStorageAdapter : IStorageProvider
{
    public string ProviderId => "telegram";

    public Task<StorageCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        // Telegram bot API limit: 50MB per file; Telegram client API limit: up to 2GB/4GB
        return Task.FromResult(new StorageCapabilities(
            MaxObjectSizeBytes: 50 * 1024 * 1024,
            SupportsDeletion: false,
            SupportsRangeRequests: false
        ));
    }

    public Task<RemoteObjectDescriptor> PutObjectAsync(ObjectId id, Stream contentStream, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Telegram adapter will be implemented in Phase 3.");
    }

    public Task<Stream> GetObjectAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Telegram adapter will be implemented in Phase 3.");
    }

    public Task<bool> ExistsAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Telegram adapter will be implemented in Phase 3.");
    }

    public Task<IReadOnlyList<ObjectId>> EnumerateCatalogAnchorsAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("Telegram adapter will be implemented in Phase 3.");
    }
}
