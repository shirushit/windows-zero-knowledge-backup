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
    public void EncryptedEnvelope_ShouldHoldCryptoPayload()
    {
        var salt = new byte[16];
        var nonce = new byte[12];
        var cipher = new byte[32];
        var envelope = new EncryptedEnvelope(1, salt, nonce, cipher);

        envelope.FormatVersion.Should().Be(1);
        envelope.Salt.Length.Should().Be(16);
        envelope.Nonce.Length.Should().Be(12);
        envelope.CiphertextWithTag.Length.Should().Be(32);
    }
}
