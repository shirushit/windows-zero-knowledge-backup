using System.Security.Cryptography;
using BackupApp.Crypto;
using BackupApp.Storage;

namespace BackupApp.RestoreEngine;

public interface IRemoteCatalogDiscoveryService
{
    Task<IReadOnlyList<SnapshotManifest>> DiscoverRemoteManifestsAsync(
        IStorageProvider storageProvider,
        MasterKey masterKey,
        CancellationToken cancellationToken = default
    );
}

public sealed class RemoteCatalogDiscoveryService : IRemoteCatalogDiscoveryService
{
    private readonly IManifestCryptoService _manifestCrypto;

    public RemoteCatalogDiscoveryService(IManifestCryptoService? manifestCrypto = null)
    {
        _manifestCrypto = manifestCrypto ?? new ManifestCryptoService();
    }

    public async Task<IReadOnlyList<SnapshotManifest>> DiscoverRemoteManifestsAsync(
        IStorageProvider storageProvider,
        MasterKey masterKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(storageProvider);
        ArgumentNullException.ThrowIfNull(masterKey);

        var anchorIds = await storageProvider.EnumerateCatalogAnchorsAsync(cancellationToken).ConfigureAwait(false);
        var manifests = new List<SnapshotManifest>();

        foreach (var anchorId in anchorIds)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await using var stream = await storageProvider.GetObjectAsync(anchorId, cancellationToken).ConfigureAwait(false);
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
                var packageBytes = ms.ToArray();

                var manifest = _manifestCrypto.DecryptPackage(packageBytes, masterKey);
                manifests.Add(manifest);
            }
            catch (CryptographicException)
            {
                // Anchor may belong to a different key or be corrupted; continue discovering others
                continue;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Skip unreadable anchor in discovery
                continue;
            }
        }

        return manifests.OrderByDescending(m => m.SnapshotNumber).ToList();
    }
}
