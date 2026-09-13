using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BackupApp.Crypto;

public sealed record EncryptedEnvelope
{
    public const uint MagicHeader = 0x4E454142; // "BAEN" in little-endian (0x42, 0x41, 0x45, 0x4E)
    public const byte CurrentFormatVersion = 1;
    public const byte CipherXChaCha20Poly1305 = 1;
    public const int NonceLength = 24;
    public const int TagLength = 16;
    public const int HeaderLength = 4 + 1 + 1 + NonceLength + 4; // Magic(4) + Version(1) + Cipher(1) + Nonce(24) + Length(4) = 34 bytes

    public byte FormatVersion { get; }
    public byte CipherId { get; }
    public byte[] Nonce { get; }
    public byte[] CiphertextWithTag { get; }

    public EncryptedEnvelope(byte formatVersion, byte cipherId, byte[] nonce, byte[] ciphertextWithTag)
    {
        ArgumentNullException.ThrowIfNull(nonce);
        ArgumentNullException.ThrowIfNull(ciphertextWithTag);

        if (nonce.Length != NonceLength)
        {
            throw new ArgumentException($"Nonce must be exactly {NonceLength} bytes.", nameof(nonce));
        }

        if (ciphertextWithTag.Length < TagLength)
        {
            throw new ArgumentException($"Ciphertext must be at least tag length ({TagLength} bytes).", nameof(ciphertextWithTag));
        }

        FormatVersion = formatVersion;
        CipherId = cipherId;
        Nonce = (byte[])nonce.Clone();
        CiphertextWithTag = (byte[])ciphertextWithTag.Clone();
    }

    public byte[] ToBytes()
    {
        var totalLength = HeaderLength + CiphertextWithTag.Length;
        var buffer = new byte[totalLength];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span[..4], MagicHeader);
        span[4] = FormatVersion;
        span[5] = CipherId;
        Nonce.CopyTo(span.Slice(6, NonceLength));
        BinaryPrimitives.WriteInt32LittleEndian(span.Slice(6 + NonceLength, 4), CiphertextWithTag.Length);
        CiphertextWithTag.CopyTo(span[(6 + NonceLength + 4)..]);

        return buffer;
    }

    public static EncryptedEnvelope FromBytes(ReadOnlySpan<byte> data)
    {
        if (data.Length < HeaderLength + TagLength)
        {
            throw new CryptographicException("Data is too short to be a valid encrypted envelope.");
        }

        var magic = BinaryPrimitives.ReadUInt32LittleEndian(data[..4]);
        if (magic != MagicHeader)
        {
            throw new CryptographicException("Invalid magic header for encrypted envelope.");
        }

        var version = data[4];
        if (version != CurrentFormatVersion)
        {
            throw new CryptographicException($"Unsupported envelope version: {version}.");
        }

        var cipherId = data[5];
        if (cipherId != CipherXChaCha20Poly1305)
        {
            throw new CryptographicException($"Unsupported cipher ID: {cipherId}.");
        }

        var nonce = data.Slice(6, NonceLength).ToArray();
        var cipherLength = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(6 + NonceLength, 4));

        if (cipherLength < TagLength || data.Length != HeaderLength + cipherLength)
        {
            throw new CryptographicException("Corrupt envelope: ciphertext length mismatch or truncation.");
        }

        var ciphertextWithTag = data[(6 + NonceLength + 4)..].ToArray();
        return new EncryptedEnvelope(version, cipherId, nonce, ciphertextWithTag);
    }

    public override string ToString()
    {
        return $"[EncryptedEnvelope v{FormatVersion} Cipher={CipherId} CiphertextBytes={CiphertextWithTag.Length}]";
    }
}
