using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BackupApp.Domain;

namespace BackupApp.Crypto;

public sealed record SnapshotManifestItem(
    string Path,
    long SizeBytes,
    string ContentHashSha256,
    long Attributes,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ModifiedUtc,
    IReadOnlyList<string> ChunkIds
);

public sealed record SnapshotManifest(
    string BackupSetId,
    string SnapshotId,
    long SnapshotNumber,
    DateTimeOffset CreatedAtUtc,
    long TotalFiles,
    long TotalBytes,
    IReadOnlyList<SnapshotManifestItem> Items
);

public interface IManifestCryptoService
{
    EncryptedEnvelope EncryptManifest(SnapshotManifest manifest, MasterKey masterKey);
    SnapshotManifest DecryptManifest(EncryptedEnvelope envelope, MasterKey masterKey, string backupSetId, long snapshotNumber);
    SnapshotManifest DecryptManifest(ReadOnlySpan<byte> envelopeBytes, MasterKey masterKey, string backupSetId, long snapshotNumber);
}

public sealed class ManifestCryptoService : IManifestCryptoService
{
    private readonly ICryptoService _crypto;

    public ManifestCryptoService(ICryptoService? crypto = null)
    {
        _crypto = crypto ?? new CryptoService();
    }

    public static string BuildAssociatedData(string backupSetId, long snapshotNumber)
    {
        return $"backupapp-manifest-v1:{backupSetId}:{snapshotNumber}";
    }

    public EncryptedEnvelope EncryptManifest(SnapshotManifest manifest, MasterKey masterKey)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(masterKey);

        var manifestKey = masterKey.DeriveManifestKey();
        try
        {
            var json = JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.SnapshotManifest);
            var plaintext = Encoding.UTF8.GetBytes(json);
            var aad = Encoding.UTF8.GetBytes(BuildAssociatedData(manifest.BackupSetId, manifest.SnapshotNumber));

            return _crypto.Encrypt(plaintext, manifestKey, aad);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(manifestKey);
        }
    }

    public SnapshotManifest DecryptManifest(EncryptedEnvelope envelope, MasterKey masterKey, string backupSetId, long snapshotNumber)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(masterKey);
        ArgumentNullException.ThrowIfNull(backupSetId);

        var manifestKey = masterKey.DeriveManifestKey();
        try
        {
            var aad = Encoding.UTF8.GetBytes(BuildAssociatedData(backupSetId, snapshotNumber));
            var plaintextBytes = _crypto.Decrypt(envelope, manifestKey, aad);
            var json = Encoding.UTF8.GetString(plaintextBytes);

            return JsonSerializer.Deserialize(json, ManifestJsonContext.Default.SnapshotManifest)
                ?? throw new CryptographicException("Failed to deserialize decrypted manifest payload.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(manifestKey);
        }
    }

    public SnapshotManifest DecryptManifest(ReadOnlySpan<byte> envelopeBytes, MasterKey masterKey, string backupSetId, long snapshotNumber)
    {
        var envelope = EncryptedEnvelope.FromBytes(envelopeBytes);
        return DecryptManifest(envelope, masterKey, backupSetId, snapshotNumber);
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(SnapshotManifest))]
internal sealed partial class ManifestJsonContext : JsonSerializerContext
{
}
