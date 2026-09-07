using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace BackupApp.Crypto;

public interface IRecoveryKeyService
{
    string GenerateRecoveryKey(out byte[] rawSecret);
    byte[] ParseAndValidateRecoveryKey(string formattedKey);
    WrappedKeyEnvelope WrapMasterKeyWithRecoverySecret(MasterKey masterKey, byte[] recoverySecret);
    MasterKey UnwrapMasterKeyWithRecoverySecret(string formattedRecoveryKey, WrappedKeyEnvelope recoveryEnvelope);
}

public sealed class RecoveryKeyService : IRecoveryKeyService
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
    public const string RecoveryWrapAad = "backupapp-recovery-wrap-v1";
    public const int EntropyBytes = 32;

    public string GenerateRecoveryKey(out byte[] rawSecret)
    {
        rawSecret = RandomNumberGenerator.GetBytes(EntropyBytes);
        var crc = ComputeCrc16(rawSecret);

        var payload = new byte[EntropyBytes + 2];
        rawSecret.CopyTo(payload, 0);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(EntropyBytes, 2), crc);

        return FormatBase32(payload);
    }

    public byte[] ParseAndValidateRecoveryKey(string formattedKey)
    {
        ArgumentNullException.ThrowIfNull(formattedKey);

        var cleaned = formattedKey.Replace("-", string.Empty, StringComparison.Ordinal)
                                  .Replace(" ", string.Empty, StringComparison.Ordinal)
                                  .Trim()
                                  .ToUpperInvariant();

        var bytes = DecodeBase32(cleaned);
        if (bytes.Length != EntropyBytes + 2)
        {
            throw new ArgumentException($"Invalid recovery key length. Expected {EntropyBytes + 2} decoded bytes.", nameof(formattedKey));
        }

        var secret = bytes[..EntropyBytes];
        var expectedCrc = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(EntropyBytes, 2));
        var actualCrc = ComputeCrc16(secret);

        if (expectedCrc != actualCrc)
        {
            throw new ArgumentException("Recovery key checksum verification failed. Please check for typing mistakes.", nameof(formattedKey));
        }

        return secret;
    }

    public WrappedKeyEnvelope WrapMasterKeyWithRecoverySecret(MasterKey masterKey, byte[] recoverySecret)
    {
        ArgumentNullException.ThrowIfNull(masterKey);
        ArgumentNullException.ThrowIfNull(recoverySecret);

        if (recoverySecret.Length != EntropyBytes)
        {
            throw new ArgumentException($"Recovery secret must be {EntropyBytes} bytes.", nameof(recoverySecret));
        }

        // Derive KEK via HKDF-SHA256 from the high-entropy recovery secret
        var kek = new byte[32];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            recoverySecret,
            kek,
            salt: ReadOnlySpan<byte>.Empty,
            info: Encoding.UTF8.GetBytes(RecoveryWrapAad)
        );

        try
        {
            var crypto = new CryptoService();
            var aad = Encoding.UTF8.GetBytes(RecoveryWrapAad);
            var envelope = crypto.Encrypt(masterKey.AsSpan(), kek, aad);

            return new WrappedKeyEnvelope
            {
                Version = WrappedKeyEnvelope.CurrentVersion,
                KdfProfile = 0, // 0 = Direct HKDF from high-entropy recovery secret
                MemorySizeKiB = 0,
                NumberOfPasses = 0,
                DegreeOfParallelism = 0,
                SaltBase64 = string.Empty,
                NonceBase64 = Convert.ToBase64String(envelope.Nonce),
                CiphertextWithTagBase64 = Convert.ToBase64String(envelope.CiphertextWithTag)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    public MasterKey UnwrapMasterKeyWithRecoverySecret(string formattedRecoveryKey, WrappedKeyEnvelope recoveryEnvelope)
    {
        ArgumentNullException.ThrowIfNull(recoveryEnvelope);

        var secret = ParseAndValidateRecoveryKey(formattedRecoveryKey);
        var kek = new byte[32];
        HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            secret,
            kek,
            salt: ReadOnlySpan<byte>.Empty,
            info: Encoding.UTF8.GetBytes(RecoveryWrapAad)
        );

        byte[]? decrypted = null;
        try
        {
            var nonce = Convert.FromBase64String(recoveryEnvelope.NonceBase64);
            var ciphertext = Convert.FromBase64String(recoveryEnvelope.CiphertextWithTagBase64);

            var crypto = new CryptoService();
            var envelope = new EncryptedEnvelope(
                WrappedKeyEnvelope.CurrentVersion,
                EncryptedEnvelope.CipherXChaCha20Poly1305,
                nonce,
                ciphertext
            );

            var aad = Encoding.UTF8.GetBytes(RecoveryWrapAad);
            decrypted = crypto.Decrypt(envelope, kek, aad);

            return new MasterKey(decrypted);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
            CryptographicOperations.ZeroMemory(kek);
            if (decrypted != null)
            {
                CryptographicOperations.ZeroMemory(decrypted);
            }
        }
    }

    private static string FormatBase32(byte[] data)
    {
        var sb = new StringBuilder();
        int bitBuffer = 0;
        int bitCount = 0;

        foreach (var b in data)
        {
            bitBuffer = (bitBuffer << 8) | b;
            bitCount += 8;

            while (bitCount >= 5)
            {
                bitCount -= 5;
                int index = (bitBuffer >> bitCount) & 0x1F;
                sb.Append(Base32Alphabet[index]);
            }
        }

        if (bitCount > 0)
        {
            int index = (bitBuffer << (5 - bitCount)) & 0x1F;
            sb.Append(Base32Alphabet[index]);
        }

        // Format in groups of 5 with dashes (e.g. XXXXX-XXXXX-...)
        var formatted = new StringBuilder();
        for (int i = 0; i < sb.Length; i++)
        {
            if (i > 0 && i % 5 == 0)
            {
                formatted.Append('-');
            }
            formatted.Append(sb[i]);
        }
        return formatted.ToString();
    }

    private static byte[] DecodeBase32(string base32)
    {
        var result = new List<byte>();
        int bitBuffer = 0;
        int bitCount = 0;

        foreach (var c in base32)
        {
            int val = Base32Alphabet.IndexOf(c, StringComparison.Ordinal);
            if (val < 0)
            {
                throw new ArgumentException($"Invalid Base32 character '{c}'.", nameof(base32));
            }

            bitBuffer = (bitBuffer << 5) | val;
            bitCount += 5;

            if (bitCount >= 8)
            {
                bitCount -= 8;
                result.Add((byte)((bitBuffer >> bitCount) & 0xFF));
            }
        }

        return result.ToArray();
    }

    private static ushort ComputeCrc16(byte[] data)
    {
        // Standard CCITT CRC16
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (int i = 0; i < 8; i++)
            {
                if ((crc & 0x8000) != 0)
                {
                    crc = (ushort)((crc << 1) ^ 0x1021);
                }
                else
                {
                    crc <<= 1;
                }
            }
        }
        return crc;
    }
}
