using System.Security.Cryptography;
using System.Text;
using BackupApp.Crypto;
using FluentAssertions;
using Xunit;

namespace BackupApp.CryptoTests;

public class Gate2VerificationTests
{
    private readonly CryptoService _crypto = new();
    private readonly RecoveryKeyService _recovery = new();

    [Fact]
    public void Gate2_PlaintextFixtureNeverAppearsInRemotePayload()
    {
        using var master = MasterKey.Generate();
        var contentKey = master.DeriveContentKey();

        // 1. Fixture containing distinct canary markers, Hebrew text, and binary data
        const string canaryString = "CANARY_SENSITIVE_RECORDS_XYZ_12345_SECRET";
        const string hebrewCanary = "מידע סודי ביותר - מזהה ייחודי 987654321";
        var fixtureContent = $"{canaryString}\n{hebrewCanary}\nTimestamp:{DateTimeOffset.UtcNow:O}";
        var plaintextBytes = Encoding.UTF8.GetBytes(fixtureContent);

        // 2. Encrypt into remote-ready payload
        var aad = Encoding.UTF8.GetBytes("chunk-sha256-id-001");
        var envelope = _crypto.Encrypt(plaintextBytes, contentKey, aad);
        var remotePayload = envelope.ToBytes();

        // 3. Verify that plaintext nowhere appears in remote-ready payload
        var canaryBytes = Encoding.UTF8.GetBytes(canaryString);
        var hebrewBytes = Encoding.UTF8.GetBytes(hebrewCanary);

        ContainsSubsequence(remotePayload, canaryBytes).Should().BeFalse(
            "Plaintext canary ASCII string must never appear in remote payload");
        ContainsSubsequence(remotePayload, hebrewBytes).Should().BeFalse(
            "Plaintext Hebrew UTF-8 string must never appear in remote payload");

        // 4. Verify authentic decryption yields exact original fixture
        var decrypted = _crypto.Decrypt(remotePayload, contentKey, aad);
        Encoding.UTF8.GetString(decrypted).Should().Be(fixtureContent);
    }

    [Fact]
    public void Gate2_TamperingIsReliablyDetectedAcrossAllVectors()
    {
        using var master = MasterKey.Generate();
        var contentKey = master.DeriveContentKey();
        var plaintext = Encoding.UTF8.GetBytes("Critical system state that must never be forged.");
        var aad = Encoding.UTF8.GetBytes("chunk-aad-002");

        var envelope = _crypto.Encrypt(plaintext, contentKey, aad);
        var rawPayload = envelope.ToBytes();

        // Tamper vector A: single bit flip in ciphertext
        var tamperedCipher = (byte[])rawPayload.Clone();
        tamperedCipher[^20] ^= 0x01;
        var actCipher = () => _crypto.Decrypt(tamperedCipher, contentKey, aad);
        actCipher.Should().Throw<CryptographicException>();

        // Tamper vector B: single bit flip in Poly1305 MAC tag
        var tamperedTag = (byte[])rawPayload.Clone();
        tamperedTag[^1] ^= 0x80;
        var actTag = () => _crypto.Decrypt(tamperedTag, contentKey, aad);
        actTag.Should().Throw<CryptographicException>();

        // Tamper vector C: swapped AAD
        var actAad = () => _crypto.Decrypt(rawPayload, contentKey, Encoding.UTF8.GetBytes("spoofed-aad"));
        actAad.Should().Throw<CryptographicException>();

        // Tamper vector D: truncated payload
        var truncated = rawPayload[..^1];
        var actTruncated = () => _crypto.Decrypt(truncated, contentKey, aad);
        actTruncated.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Gate2_FullDisasterRecoveryCycleWithOfflineRecoveryKey()
    {
        // 1. Initial user setup: generate master key, wrap with password, and generate recovery key
        using var originalMaster = MasterKey.Generate();
        const string userPassword = "MyInitialPassword2026!";
        var passwordEnvelope = WrappedKeyEnvelope.Wrap(originalMaster, userPassword);

        // Generate offline recovery material
        var recoveryPhrase = _recovery.GenerateRecoveryKey(out var recoverySecret);
        var recoveryEnvelope = _recovery.WrapMasterKeyWithRecoverySecret(originalMaster, recoverySecret);

        // 2. Backup data created and encrypted
        var contentKey = originalMaster.DeriveContentKey();
        var manifestKey = originalMaster.DeriveManifestKey();

        var originalFileData = Encoding.UTF8.GetBytes("Vital legal document contents 2026.");
        var chunkEnvelope = _crypto.Encrypt(originalFileData, contentKey, Encoding.UTF8.GetBytes("chunk-100"));
        var remoteChunkBytes = chunkEnvelope.ToBytes();

        var manifest = new SnapshotManifest(
            BackupSetId: "disaster_recovery_set",
            SnapshotId: "snap_01",
            SnapshotNumber: 1,
            CreatedAtUtc: DateTimeOffset.UtcNow,
            TotalFiles: 1,
            TotalBytes: originalFileData.Length,
            Items: [new SnapshotManifestItem("documents/vital.doc", originalFileData.Length, "hash100", 32, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, ["chunk-100"])]
        );
        var manifestCrypto = new ManifestCryptoService(_crypto);
        var remoteManifestEnvelope = manifestCrypto.EncryptManifest(manifest, originalMaster);
        var remoteManifestBytes = remoteManifestEnvelope.ToBytes();

        // 3. SIMULATE COMPLETE DISASTER:
        // PC destroyed. Original password is FORGOTTEN.
        // User has only: recoveryPhrase + remote files (chunks + manifests + recoveryEnvelope).
        using var recoveredMaster = _recovery.UnwrapMasterKeyWithRecoverySecret(recoveryPhrase, recoveryEnvelope);
        recoveredMaster.Should().NotBeNull();
        recoveredMaster.ExportRawKey().Should().BeEquivalentTo(originalMaster.ExportRawKey());

        // 4. Recover manifest
        var recoveredManifest = manifestCrypto.DecryptManifest(
            remoteManifestBytes,
            recoveredMaster,
            "disaster_recovery_set",
            1
        );
        recoveredManifest.TotalFiles.Should().Be(1);
        recoveredManifest.Items[0].Path.Should().Be("documents/vital.doc");

        // 5. Recover chunk and decrypt original file
        var recoveredContentKey = recoveredMaster.DeriveContentKey();
        var restoredFileBytes = _crypto.Decrypt(remoteChunkBytes, recoveredContentKey, Encoding.UTF8.GetBytes("chunk-100"));

        restoredFileBytes.Should().BeEquivalentTo(originalFileData);

        // 6. User sets a new password to re-wrap the recovered master key
        const string brandNewPassword = "NewlyEstablishedPassword2026$!";
        var newPasswordEnvelope = WrappedKeyEnvelope.Wrap(recoveredMaster, brandNewPassword);

        using var newlyUnlockedMaster = newPasswordEnvelope.Unwrap(brandNewPassword);
        newlyUnlockedMaster.ExportRawKey().Should().BeEquivalentTo(originalMaster.ExportRawKey());
    }

    private static bool ContainsSubsequence(byte[] haystack, byte[] needle)
    {
        if (needle.Length > haystack.Length)
        {
            return false;
        }

        for (int i = 0; i <= haystack.Length - needle.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }
            if (match)
            {
                return true;
            }
        }
        return false;
    }
}
