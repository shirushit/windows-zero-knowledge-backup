using BackupApp.Domain;

namespace BackupApp.Storage;

public record StorageCapabilities(
    long MaxObjectSizeBytes,
    bool SupportsDeletion,
    bool SupportsRangeRequests
);

public record RemoteObjectDescriptor(
    ObjectId Id,
    long SizeBytes,
    string ChecksumSha256,
    DateTimeOffset UploadedAt
);

public interface IStorageProvider
{
    string ProviderId { get; }
    Task<StorageCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default);
    Task<RemoteObjectDescriptor> PutObjectAsync(ObjectId id, Stream contentStream, CancellationToken cancellationToken = default);
    Task<Stream> GetObjectAsync(ObjectId id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(ObjectId id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ObjectId>> EnumerateCatalogAnchorsAsync(CancellationToken cancellationToken = default);
}
