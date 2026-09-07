using System.Security.Cryptography;
using System.Text;
using BackupApp.Crypto;
using FluentAssertions;
using Xunit;

namespace BackupApp.CryptoTests;

public class CryptoHierarchyTests
{
    [Fact]
    public void MasterKey_ShouldGenerateUniqueKeys_AndZeroOnDispose()
    {
        using var key1 = MasterKey.Generate();
        using var key2 = MasterKey.Generate();

        key1.ExportRawKey().Should().NotBeEquivalentTo(key2.ExportRawKey());
        key1.ExportRawKey().Length.Should().Be(32);

        var rawBefore = key1.ExportRawKey();
        key1.Dispose();

        var act = () => key1.DeriveContentKey();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void MasterKey_ShouldDeriveSeparatedRoleKeys()
    {
        using var master = MasterKey.Generate();

        var contentKey = master.DeriveContentKey();
        var manifestKey = master.DeriveManifestKey();
        var indexKey = master.DeriveIndexKey();

        contentKey.Length.Should().Be(32);
        manifestKey.Length.Should().Be(32);
        indexKey.Length.Should().Be(32);

        contentKey.Should().NotBeEquivalentTo(manifestKey);
        manifestKey.Should().NotBeEquivalentTo(indexKey);
        contentKey.Should().NotBeEquivalentTo(indexKey);
    }

    [Fact]
    public void WrappedKeyEnvelope_WrapAndUnwrap_ShouldSucceedWithCorrectPassword()
    {
        using var originalMaster = MasterKey.Generate();
        const string password = "StrongUserPassword#2026!";

        var wrapped = WrappedKeyEnvelope.Wrap(originalMaster, password);
        wrapped.Should().NotBeNull();
        wrapped.SaltBase64.Should().NotBeNullOrEmpty();
        wrapped.NonceBase64.Should().NotBeNullOrEmpty();
        wrapped.CiphertextWithTagBase64.Should().NotBeNullOrEmpty();

        var json = wrapped.ToJson();
        var fromJson = WrappedKeyEnvelope.FromJson(json);

        using var unwrappedMaster = fromJson.Unwrap(password);
        unwrappedMaster.ExportRawKey().Should().BeEquivalentTo(originalMaster.ExportRawKey());
    }

    [Fact]
    public void WrappedKeyEnvelope_PasswordChange_ShouldPreserveMasterKeyWithoutReencryptingContent()
    {
        using var master = MasterKey.Generate();
        const string oldPassword = "OldPassword123!";
        const string newPassword = "NewSecretPassword456$";

        var wrappedOld = WrappedKeyEnvelope.Wrap(master, oldPassword);

        // User changes password: unwrap with old, wrap with new
        using var unlocked = wrappedOld.Unwrap(oldPassword);
        var wrappedNew = WrappedKeyEnvelope.Wrap(unlocked, newPassword);

        // Unlock with new password
        using var restoredMaster = wrappedNew.Unwrap(newPassword);
        restoredMaster.ExportRawKey().Should().BeEquivalentTo(master.ExportRawKey());

        // Derived content key remains identical across password changes
        restoredMaster.DeriveContentKey().Should().BeEquivalentTo(master.DeriveContentKey());
    }

    [Fact]
    public void RecoveryKeyService_GenerateAndValidate_ShouldFormatAndValidateChecksum()
    {
        var recoveryService = new RecoveryKeyService();
        var formatted = recoveryService.GenerateRecoveryKey(out var rawSecret);

        formatted.Should().NotBeNullOrEmpty();
        formatted.Should().Contain("-");

        var parsedSecret = recoveryService.ParseAndValidateRecoveryKey(formatted);
        parsedSecret.Should().BeEquivalentTo(rawSecret);

        // Invalid checksum check: alter last character
        var chars = formatted.ToCharArray();
        chars[^1] = chars[^1] == 'A' ? 'B' : 'A';
        var tamperedKey = new string(chars);

        var act = () => recoveryService.ParseAndValidateRecoveryKey(tamperedKey);
        act.Should().Throw<ArgumentException>().WithMessage("*checksum*");
    }

    [Fact]
    public void RecoveryKeyService_WrapAndUnwrapMasterKey_ShouldRecoverMasterKey()
    {
        var recoveryService = new RecoveryKeyService();
        using var master = MasterKey.Generate();

        var recoveryPhrase = recoveryService.GenerateRecoveryKey(out var recoverySecret);
        var recoveryEnvelope = recoveryService.WrapMasterKeyWithRecoverySecret(master, recoverySecret);

        using var recoveredMaster = recoveryService.UnwrapMasterKeyWithRecoverySecret(recoveryPhrase, recoveryEnvelope);
        recoveredMaster.ExportRawKey().Should().BeEquivalentTo(master.ExportRawKey());
    }

    [Fact]
    public void ManifestCryptoService_EncryptAndDecrypt_ShouldPreserveStructureAndVerifyAad()
    {
        var crypto = new CryptoService();
        var manifestCrypto = new ManifestCryptoService(crypto);
        using var master = MasterKey.Generate();

        var manifest = new SnapshotManifest(
            BackupSetId: "bset_001",
            SnapshotId: "snap_001",
            SnapshotNumber: 1,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            TotalFiles: 2,
            TotalBytes: 1024,
            Items:
            [
                new SnapshotManifestItem("documents/report.pdf", 512, "hashA", 32, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ["chk_01"]),
                new SnapshotManifestItem("photos/vacation.jpg", 512, "hashB", 32, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ["chk_02"])
            ]
        );

        var envelope = manifestCrypto.EncryptManifest(manifest, master);
        envelope.Should().NotBeNull();

        var decrypted = manifestCrypto.DecryptManifest(envelope, master, "bset_001", 1);
        decrypted.BackupSetId.Should().Be("bset_001");
        decrypted.SnapshotNumber.Should().Be(1);
        decrypted.TotalFiles.Should().Be(2);
        decrypted.Items.Should().HaveCount(2);
        decrypted.Items[0].Path.Should().Be("documents/report.pdf");
    }

    [Fact]
    public void WindowsCredentialStorage_ShouldProtectAndRetrieveSecret()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), $"dpapi_test_{Guid.NewGuid():N}");
        try
        {
            var storage = new WindowsCredentialStorage(tempDir);
            const string keyName = "telegram_bot_token";
            const string tokenValue = "123456789:ABCdefGHI_secret_token_value";

            storage.StoreSecret(keyName, tokenValue);
            var retrieved = storage.RetrieveSecret(keyName);
            retrieved.Should().Be(tokenValue);

            storage.RemoveSecret(keyName);
            storage.RetrieveSecret(keyName).Should().BeNull();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
