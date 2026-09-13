using System.Buffers.Binary;
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

public sealed record ManifestPackage(string BackupSetId, long SnapshotNumber, EncryptedEnvelope Envelope)
{
    public const uint MagicHeader = 0x4D414D42; // "BMAM" in little-endian
    public const byte CurrentVersion = 1;

    public byte[] ToBytes()
    {
        var setIdBytes = Encoding.UTF8.GetBytes(BackupSetId);
        var envelopeBytes = Envelope.ToBytes();
        var totalLen = 4 + 1 + 2 + setIdBytes.Length + 8 + 4 + envelopeBytes.Length;
        var buffer = new byte[totalLen];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span[..4], MagicHeader);
        span[4] = CurrentVersion;
        BinaryPrimitives.WriteUInt16LittleEndian(span.Slice(5, 2), (ushort)setIdBytes.Length);
        setIdBytes.CopyTo(span.Slice(7, setIdBytes.Length));
        int offset = 7 + setIdBytes.Length;
        BinaryPrimitives.WriteInt64LittleEndian(span.Slice(offset, 8), SnapshotNumber);
        offset += 8;
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(offset, 4), envelopeBytes.Length);
        offset += 4;
        envelopeBytes.CopyTo(span[offset..]);

        return buffer;
    }

    public static ManifestPackage FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 + 1 + 2 + 8 + 4)
        {
            throw new CryptographicException("Manifest package is too short.");
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        if (magic != MagicHeader)
        {
            throw new CryptographicException("Invalid manifest package magic header.");
        }

        var version = data[4];
        if (version != CurrentVersion)
        {
            throw new CryptographicException($"Unsupported manifest package version {version}.");
        }

        var setIdLen = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(5, 2));
        if (data.Length < 7 + setIdLen + 8 + 4)
        {
            throw new CryptographicException("Manifest package data is truncated.");
        }

        var setId = Encoding.UTF8.GetString(data.Slice(7, setIdLen));
        int offset = 7 + setIdLen;
        var snapshotNumber = BinaryPrimitives.ReadInt64LittleEndian(data.Slice(offset, 8));
        offset += 8;
        var envelopeLen = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        offset += 4;

        if (data.Length < offset + envelopeLen)
        {
            throw new CryptographicException("Manifest package envelope data is truncated.");
        }

        var envelope = EncryptedEnvelope.FromBytes(data.Slice(offset, envelopeLen));
        return new ManifestPackage(setId, snapshotNumber, envelope);
    }
}

public interface IManifestCryptoService
{
    EncryptedEnvelope EncryptManifest(SnapshotManifest manifest, MasterKey masterKey);
    byte[] CreateManifestPackageBytes(SnapshotManifest manifest, MasterKey masterKey);
    SnapshotManifest DecryptManifest(EncryptedEnvelope envelope, MasterKey masterKey, string backupSetId, long snapshotNumber);
    SnapshotManifest DecryptManifest(ReadOnlySpan<byte> envelopeBytes, MasterKey masterKey, string backupSetId, long snapshotNumber);
    SnapshotManifest DecryptPackage(ReadOnlySpan<byte> packageBytes, MasterKey masterKey, string? fallbackBackupSetId = null, long? fallbackSnapshotNumber = null);
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
        byte[]? plaintext = null;
        try
        {
            var json = JsonSerializer.Serialize(manifest, ManifestJsonContext.Default.SnapshotManifest);
            plaintext = Encoding.UTF8.GetBytes(json);
            var aad = Encoding.UTF8.GetBytes(BuildAssociatedData(manifest.BackupSetId, manifest.SnapshotNumber));

            return _crypto.Encrypt(plaintext, manifestKey, aad);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(manifestKey);
            if (plaintext != null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    public SnapshotManifest DecryptManifest(EncryptedEnvelope envelope, MasterKey masterKey, string backupSetId, long snapshotNumber)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(masterKey);
        ArgumentNullException.ThrowIfNull(backupSetId);

        var manifestKey = masterKey.DeriveManifestKey();
        byte[]? plaintextBytes = null;
        try
        {
            var aad = Encoding.UTF8.GetBytes(BuildAssociatedData(backupSetId, snapshotNumber));
            plaintextBytes = _crypto.Decrypt(envelope, manifestKey, aad);
            var json = Encoding.UTF8.GetString(plaintextBytes);

            return JsonSerializer.Deserialize(json, ManifestJsonContext.Default.SnapshotManifest)
                ?? throw new CryptographicException("Failed to deserialize decrypted manifest payload.");
        }
        finally
        {
            CryptographicOperations.ZeroMemory(manifestKey);
            if (plaintextBytes != null)
            {
                CryptographicOperations.ZeroMemory(plaintextBytes);
            }
        }
    }

    public SnapshotManifest DecryptManifest(ReadOnlySpan<byte> envelopeBytes, MasterKey masterKey, string backupSetId, long snapshotNumber)
    {
        var envelope = EncryptedEnvelope.FromBytes(envelopeBytes);
        return DecryptManifest(envelope, masterKey, backupSetId, snapshotNumber);
    }

    public byte[] CreateManifestPackageBytes(SnapshotManifest manifest, MasterKey masterKey)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(masterKey);

        var envelope = EncryptManifest(manifest, masterKey);
        var pkg = new ManifestPackage(manifest.BackupSetId, manifest.SnapshotNumber, envelope);
        return pkg.ToBytes();
    }

    public SnapshotManifest DecryptPackage(ReadOnlySpan<byte> packageBytes, MasterKey masterKey, string? fallbackBackupSetId = null, long? fallbackSnapshotNumber = null)
    {
        ArgumentNullException.ThrowIfNull(masterKey);

        if (packageBytes.Length >= 4 && BinaryPrimitives.ReadUInt32LittleEndian(packageBytes[..4]) == ManifestPackage.MagicHeader)
        {
            var pkg = ManifestPackage.FromBytes(packageBytes);
            return DecryptManifest(pkg.Envelope, masterKey, pkg.BackupSetId, pkg.SnapshotNumber);
        }

        if (fallbackBackupSetId != null && fallbackSnapshotNumber.HasValue)
        {
            return DecryptManifest(packageBytes, masterKey, fallbackBackupSetId, fallbackSnapshotNumber.Value);
        }

        throw new CryptographicException("Cannot decrypt raw envelope without known backupSetId and snapshotNumber for AAD verification.");
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(SnapshotManifest))]
internal sealed partial class ManifestJsonContext : JsonSerializerContext
{
}
