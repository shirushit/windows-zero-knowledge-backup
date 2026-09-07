using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BackupApp.Crypto;

public sealed class WrappedKeyEnvelope
{
    public const byte CurrentVersion = 1;
    private const string KeyWrapAad = "backupapp-wrapped-key-v1";

    [JsonPropertyName("version")]
    public byte Version { get; init; } = CurrentVersion;

    [JsonPropertyName("kdfProfile")]
    public byte KdfProfile { get; init; } = (byte)KdfProfileId.Argon2idStandard;

    [JsonPropertyName("memorySizeKiB")]
    public long MemorySizeKiB { get; init; }

    [JsonPropertyName("numberOfPasses")]
    public int NumberOfPasses { get; init; }

    [JsonPropertyName("degreeOfParallelism")]
    public int DegreeOfParallelism { get; init; }

    [JsonPropertyName("salt")]
    public string SaltBase64 { get; init; } = string.Empty;

    [JsonPropertyName("nonce")]
    public string NonceBase64 { get; init; } = string.Empty;

    [JsonPropertyName("ciphertextWithTag")]
    public string CiphertextWithTagBase64 { get; init; } = string.Empty;

    public static WrappedKeyEnvelope Wrap(MasterKey masterKey, string password, KdfParameters? kdfParams = null)
    {
        ArgumentNullException.ThrowIfNull(masterKey);
        ArgumentNullException.ThrowIfNull(password);

        var kdf = kdfParams ?? KdfParameters.CreateDefault();
        var kek = kdf.DeriveKey(password);

        try
        {
            var crypto = new CryptoService();
            var aad = Encoding.UTF8.GetBytes(KeyWrapAad);
            var envelope = crypto.Encrypt(masterKey.AsSpan(), kek, aad);

            return new WrappedKeyEnvelope
            {
                Version = CurrentVersion,
                KdfProfile = (byte)kdf.ProfileId,
                MemorySizeKiB = kdf.MemorySizeKiB,
                NumberOfPasses = kdf.NumberOfPasses,
                DegreeOfParallelism = kdf.DegreeOfParallelism,
                SaltBase64 = Convert.ToBase64String(kdf.Salt),
                NonceBase64 = Convert.ToBase64String(envelope.Nonce),
                CiphertextWithTagBase64 = Convert.ToBase64String(envelope.CiphertextWithTag)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
        }
    }

    public MasterKey Unwrap(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        if (Version != CurrentVersion)
        {
            throw new CryptographicException($"Unsupported wrapped key version: {Version}.");
        }

        var salt = Convert.FromBase64String(SaltBase64);
        var nonce = Convert.FromBase64String(NonceBase64);
        var ciphertextWithTag = Convert.FromBase64String(CiphertextWithTagBase64);

        var kdf = new KdfParameters(
            (KdfProfileId)KdfProfile,
            salt,
            MemorySizeKiB,
            NumberOfPasses,
            DegreeOfParallelism
        );

        var kek = kdf.DeriveKey(password);
        byte[]? decryptedRaw = null;
        try
        {
            var crypto = new CryptoService();
            var envelope = new EncryptedEnvelope(
                CurrentVersion,
                EncryptedEnvelope.CipherXChaCha20Poly1305,
                nonce,
                ciphertextWithTag
            );
            var aad = Encoding.UTF8.GetBytes(KeyWrapAad);
            decryptedRaw = crypto.Decrypt(envelope, kek, aad);

            return new MasterKey(decryptedRaw);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(kek);
            if (decryptedRaw != null)
            {
                CryptographicOperations.ZeroMemory(decryptedRaw);
            }
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, WrappedKeyJsonContext.Default.WrappedKeyEnvelope);

    public static WrappedKeyEnvelope FromJson(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return JsonSerializer.Deserialize(json, WrappedKeyJsonContext.Default.WrappedKeyEnvelope)
            ?? throw new CryptographicException("Failed to deserialize wrapped key envelope.");
    }
}

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(WrappedKeyEnvelope))]
internal sealed partial class WrappedKeyJsonContext : JsonSerializerContext
{
}
