namespace BackupApp.Crypto;

public record EncryptedEnvelope(
    byte FormatVersion,
    byte[] Salt,
    byte[] Nonce,
    byte[] CiphertextWithTag
);

public interface ICryptoService
{
    byte[] DeriveKey(string password, byte[] salt);
    EncryptedEnvelope Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData);
    byte[] Decrypt(EncryptedEnvelope envelope, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData);
}

public sealed class CryptoService : ICryptoService
{
    public const byte CurrentFormatVersion = 1;

    public byte[] DeriveKey(string password, byte[] salt)
    {
        throw new NotImplementedException("Argon2id derivation will be implemented in Phase 2.");
    }

    public EncryptedEnvelope Encrypt(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData)
    {
        throw new NotImplementedException("AEAD encryption envelope will be implemented in Phase 2.");
    }

    public byte[] Decrypt(EncryptedEnvelope envelope, ReadOnlySpan<byte> key, ReadOnlySpan<byte> associatedData)
    {
        throw new NotImplementedException("AEAD decryption envelope will be implemented in Phase 2.");
    }
}
