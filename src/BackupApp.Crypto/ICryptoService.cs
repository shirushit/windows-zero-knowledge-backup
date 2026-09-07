using System.Security.Cryptography;
using NSec.Cryptography;

namespace BackupApp.Crypto;

public interface ICryptoService
{
    byte[] DeriveKey(string password, byte[] salt, KdfParameters? parameters = null);
    EncryptedEnvelope Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default);
    byte[] Decrypt(EncryptedEnvelope envelope, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default);
    byte[] Decrypt(ReadOnlySpan<byte> envelopeBytes, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default);
}

public sealed class CryptoService : ICryptoService
{
    private static readonly AeadAlgorithm Algorithm = AeadAlgorithm.XChaCha20Poly1305;

    public const byte CurrentFormatVersion = EncryptedEnvelope.CurrentFormatVersion;

    public byte[] DeriveKey(string password, byte[] salt, KdfParameters? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(salt);

        var kdf = parameters ?? KdfParameters.CreateDefault(salt);
        return kdf.DeriveKey(password);
    }

    public EncryptedEnvelope Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default)
    {
        if (key.Length != Algorithm.KeySize)
        {
            throw new ArgumentException($"Key must be exactly {Algorithm.KeySize} bytes.", nameof(key));
        }

        var nonce = RandomNumberGenerator.GetBytes(Algorithm.NonceSize);

        using var nsecKey = Key.Import(Algorithm, key, KeyBlobFormat.RawSymmetricKey);
        var ciphertextWithTag = Algorithm.Encrypt(nsecKey, nonce, associatedData, plaintext);

        return new EncryptedEnvelope(
            CurrentFormatVersion,
            EncryptedEnvelope.CipherXChaCha20Poly1305,
            nonce,
            ciphertextWithTag
        );
    }

    public byte[] Decrypt(EncryptedEnvelope envelope, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (key.Length != Algorithm.KeySize)
        {
            throw new ArgumentException($"Key must be exactly {Algorithm.KeySize} bytes.", nameof(key));
        }

        if (envelope.CipherId != EncryptedEnvelope.CipherXChaCha20Poly1305)
        {
            throw new CryptographicException($"Unsupported cipher ID: {envelope.CipherId}.");
        }

        using var nsecKey = Key.Import(Algorithm, key, KeyBlobFormat.RawSymmetricKey);
        var plaintext = Algorithm.Decrypt(nsecKey, envelope.Nonce, associatedData, envelope.CiphertextWithTag);

        if (plaintext == null)
        {
            throw new CryptographicException("AEAD authentication verification failed: data is tampered, corrupted, or key is invalid.");
        }

        return plaintext;
    }

    public byte[] Decrypt(ReadOnlySpan<byte> envelopeBytes, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData = default)
    {
        var envelope = EncryptedEnvelope.FromBytes(envelopeBytes);
        return Decrypt(envelope, key, associatedData);
    }
}
