using BackupApp.Crypto;
using FluentAssertions;
using Xunit;

namespace BackupApp.CryptoTests;

public class CryptoBoundaryTests
{
    [Fact]
    public void CryptoService_Version_ShouldBeCurrent()
    {
        CryptoService.CurrentFormatVersion.Should().Be(1);
    }

    [Fact]
    public void EncryptedEnvelope_ShouldHoldCryptoPayload_AndSerializeRoundtrip()
    {
        var nonce = new byte[24];
        var cipher = new byte[32];
        var envelope = new EncryptedEnvelope(1, 1, nonce, cipher);

        envelope.FormatVersion.Should().Be(1);
        envelope.CipherId.Should().Be(1);
        envelope.Nonce.Length.Should().Be(24);
        envelope.CiphertextWithTag.Length.Should().Be(32);

        var bytes = envelope.ToBytes();
        bytes.Length.Should().Be(EncryptedEnvelope.HeaderLength + 32);

        var roundtripped = EncryptedEnvelope.FromBytes(bytes);
        roundtripped.FormatVersion.Should().Be(1);
        roundtripped.CipherId.Should().Be(1);
        roundtripped.Nonce.Should().BeEquivalentTo(nonce);
        roundtripped.CiphertextWithTag.Should().BeEquivalentTo(cipher);
    }

    [Fact]
    public void NSec_Primitives_ShouldBeAvailable()
    {
        var aead = NSec.Cryptography.AeadAlgorithm.XChaCha20Poly1305;
        aead.Should().NotBeNull();
        aead.NonceSize.Should().Be(24);
        aead.KeySize.Should().Be(32);
        aead.TagSize.Should().Be(16);
    }

    [Fact]
    public void NSec_Argon2id_ShouldDeriveKey()
    {
        var parameters = new NSec.Cryptography.Argon2Parameters
        {
            DegreeOfParallelism = 1,
            MemorySize = 65536, // 64 MB (65,536 KiB)
            NumberOfPasses = 3
        };
        var kdf = new NSec.Cryptography.Argon2id(parameters);
        var password = System.Text.Encoding.UTF8.GetBytes("test-password");
        var salt = new byte[16];

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var derived = kdf.DeriveBytes(password, salt, 32);
        sw.Stop();

        derived.Should().HaveCount(32);
        sw.ElapsedMilliseconds.Should().BeLessThan(2000); // 70-150ms on modern PC
    }

    [Fact]
    public void NSec_XChaCha20Poly1305_EncryptAndDecrypt()
    {
        var aead = NSec.Cryptography.AeadAlgorithm.XChaCha20Poly1305;
        var rawKey = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(rawKey);

        using var key = NSec.Cryptography.Key.Import(aead, rawKey, NSec.Cryptography.KeyBlobFormat.RawSymmetricKey);
        var nonce = new byte[24];
        System.Security.Cryptography.RandomNumberGenerator.Fill(nonce);

        var plaintext = System.Text.Encoding.UTF8.GetBytes("Super secret payload");
        var aad = System.Text.Encoding.UTF8.GetBytes("Associated metadata");

        var ciphertextWithTag = aead.Encrypt(key, nonce, aad, plaintext);
        ciphertextWithTag.Length.Should().Be(plaintext.Length + aead.TagSize);

        var decrypted = aead.Decrypt(key, nonce, aad, ciphertextWithTag);
        decrypted.Should().NotBeNull();
        decrypted.Should().BeEquivalentTo(plaintext);
    }
}
