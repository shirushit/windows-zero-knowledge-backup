using System.Security.Cryptography;
using System.Text;

namespace BackupApp.Crypto;

public sealed class MasterKey : IDisposable
{
    public const int KeySizeBytes = 32;
    public const string ContentContext = "backupapp-content-v1";
    public const string ManifestContext = "backupapp-manifest-v1";
    public const string IndexContext = "backupapp-index-v1";

    private readonly byte[] _key;
    private bool _disposed;

    public MasterKey(byte[] keyBytes)
    {
        ArgumentNullException.ThrowIfNull(keyBytes);
        if (keyBytes.Length != KeySizeBytes)
        {
            throw new ArgumentException($"Master key must be exactly {KeySizeBytes} bytes.", nameof(keyBytes));
        }

        _key = (byte[])keyBytes.Clone();
    }

    public static MasterKey Generate()
    {
        var randomBytes = RandomNumberGenerator.GetBytes(KeySizeBytes);
        try
        {
            return new MasterKey(randomBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(randomBytes);
        }
    }

    public byte[] DeriveSubkey(string contextInfo, int outputLength = KeySizeBytes)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(contextInfo);

        var infoBytes = Encoding.UTF8.GetBytes(contextInfo);
        var derived = new byte[outputLength];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            _key,
            derived,
            salt: ReadOnlySpan<byte>.Empty,
            info: infoBytes
        );
        return derived;
    }

    public byte[] DeriveContentKey() => DeriveSubkey(ContentContext);

    public byte[] DeriveManifestKey() => DeriveSubkey(ManifestContext);

    public byte[] DeriveIndexKey() => DeriveSubkey(IndexContext);

    public byte[] ExportRawKey()
    {
        ThrowIfDisposed();
        return (byte[])_key.Clone();
    }

    public ReadOnlySpan<byte> AsSpan()
    {
        ThrowIfDisposed();
        return _key.AsSpan();
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            CryptographicOperations.ZeroMemory(_key);
            _disposed = true;
        }
    }
}
