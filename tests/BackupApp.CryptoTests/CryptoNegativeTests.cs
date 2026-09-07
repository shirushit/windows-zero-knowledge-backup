using System.Security.Cryptography;
using System.Text;
using BackupApp.Crypto;
using FluentAssertions;
using Xunit;

namespace BackupApp.CryptoTests;

public class CryptoNegativeTests
{
    private readonly CryptoService _crypto = new();

    [Fact]
    public void Decrypt_WithWrongKey_ShouldThrowCryptographicException()
    {
        var key1 = RandomNumberGenerator.GetBytes(32);
        var key2 = RandomNumberGenerator.GetBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("Confidential data payload");

        var envelope = _crypto.Encrypt(plaintext, key1);

        var act = () => _crypto.Decrypt(envelope, key2);
        act.Should().Throw<CryptographicException>()
           .WithMessage("*tampered*");
    }

    [Fact]
    public void Decrypt_WithTamperedCiphertext_ShouldThrowCryptographicException()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("Confidential data payload");

        var envelope = _crypto.Encrypt(plaintext, key);

        // Flip a bit in the ciphertext portion
        var tamperedCiphertext = (byte[])envelope.CiphertextWithTag.Clone();
        tamperedCiphertext[0] ^= 0x01;

        var tamperedEnvelope = new EncryptedEnvelope(
            envelope.FormatVersion,
            envelope.CipherId,
            envelope.Nonce,
            tamperedCiphertext
        );

        var act = () => _crypto.Decrypt(tamperedEnvelope, key);
        act.Should().Throw<CryptographicException>()
           .WithMessage("*tampered*");
    }

    [Fact]
    public void Decrypt_WithTamperedAuthTag_ShouldThrowCryptographicException()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("Confidential data payload");

        var envelope = _crypto.Encrypt(plaintext, key);

        // Flip a bit in the Poly1305 tag (last 16 bytes)
        var tamperedCiphertext = (byte[])envelope.CiphertextWithTag.Clone();
        tamperedCiphertext[^1] ^= 0x01;

        var tamperedEnvelope = new EncryptedEnvelope(
            envelope.FormatVersion,
            envelope.CipherId,
            envelope.Nonce,
            tamperedCiphertext
        );

        var act = () => _crypto.Decrypt(tamperedEnvelope, key);
        act.Should().Throw<CryptographicException>()
           .WithMessage("*tampered*");
    }

    [Fact]
    public void Decrypt_WithWrongAssociatedData_ShouldThrowCryptographicException()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("Payload requiring authentication");
        var originalAad = Encoding.UTF8.GetBytes("context:backup-set-001:chunk-005");
        var spoofedAad = Encoding.UTF8.GetBytes("context:backup-set-999:chunk-005");

        var envelope = _crypto.Encrypt(plaintext, key, originalAad);

        var act = () => _crypto.Decrypt(envelope, key, spoofedAad);
        act.Should().Throw<CryptographicException>()
           .WithMessage("*tampered*");
    }

    [Fact]
    public void Decrypt_WithCorruptedNonce_ShouldThrowCryptographicException()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("Payload with corrupted nonce");

        var envelope = _crypto.Encrypt(plaintext, key);

        var corruptedNonce = (byte[])envelope.Nonce.Clone();
        corruptedNonce[0] ^= 0xFF;

        var tamperedEnvelope = new EncryptedEnvelope(
            envelope.FormatVersion,
            envelope.CipherId,
            corruptedNonce,
            envelope.CiphertextWithTag
        );

        var act = () => _crypto.Decrypt(tamperedEnvelope, key);
        act.Should().Throw<CryptographicException>()
           .WithMessage("*tampered*");
    }

    [Fact]
    public void EnvelopeSerialization_WithTruncatedPayload_ShouldThrowCryptographicException()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("Some message");

        var envelope = _crypto.Encrypt(plaintext, key);
        var serialized = envelope.ToBytes();

        // Truncate by 5 bytes
        var truncated = serialized[..^5];

        var act = () => EncryptedEnvelope.FromBytes(truncated);
        act.Should().Throw<CryptographicException>()
           .WithMessage("*mismatch or truncation*");
    }

    [Fact]
    public void EnvelopeSerialization_WithInvalidMagicHeader_ShouldThrowCryptographicException()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("Valid payload");

        var envelope = _crypto.Encrypt(plaintext, key);
        var serialized = envelope.ToBytes();

        // Corrupt magic header
        serialized[0] ^= 0xFF;

        var act = () => EncryptedEnvelope.FromBytes(serialized);
        act.Should().Throw<CryptographicException>()
           .WithMessage("*Invalid magic header*");
    }

    [Fact]
    public void EnvelopeSerialization_WithUnsupportedVersion_ShouldThrowCryptographicException()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var plaintext = Encoding.UTF8.GetBytes("Valid payload");

        var envelope = _crypto.Encrypt(plaintext, key);
        var serialized = envelope.ToBytes();

        // Change version byte from 1 to 99
        serialized[4] = 99;

        var act = () => EncryptedEnvelope.FromBytes(serialized);
        act.Should().Throw<CryptographicException>()
           .WithMessage("*Unsupported envelope version*");
    }

    [Fact]
    public void WrappedKeyEnvelope_WithWrongPassword_ShouldThrowCryptographicException()
    {
        using var master = MasterKey.Generate();
        var wrapped = WrappedKeyEnvelope.Wrap(master, "CorrectPassword123!");

        var act = () => wrapped.Unwrap("WrongPassword999!");
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void RecoveryKeyService_WithWrongRecoveryPhrase_ShouldThrowCryptographicException()
    {
        var service = new RecoveryKeyService();
        using var master = MasterKey.Generate();

        _ = service.GenerateRecoveryKey(out var secret1);
        var phrase2 = service.GenerateRecoveryKey(out _);

        var envelope = service.WrapMasterKeyWithRecoverySecret(master, secret1);

        // Attempting to unwrap with a different (but validly formatted) recovery phrase
        var act = () => service.UnwrapMasterKeyWithRecoverySecret(phrase2, envelope);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void ManifestCryptoService_WithSpoofedBackupSetId_ShouldFailAuthentication()
    {
        var manifestCrypto = new ManifestCryptoService(_crypto);
        using var master = MasterKey.Generate();

        var manifest = new SnapshotManifest(
            BackupSetId: "legitimate_bset",
            SnapshotId: "snap_1",
            SnapshotNumber: 1,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            TotalFiles: 1,
            TotalBytes: 100,
            Items: [new SnapshotManifestItem("file.txt", 100, "hash", 0, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ["c1"])]
        );

        var envelope = manifestCrypto.EncryptManifest(manifest, master);

        // Attacker attempts to replay manifest against different backup set
        var act = () => manifestCrypto.DecryptManifest(envelope, master, "spoofed_bset", 1);
        act.Should().Throw<CryptographicException>();
    }
}
