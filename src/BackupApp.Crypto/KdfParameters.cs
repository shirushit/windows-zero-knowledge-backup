using System.Security.Cryptography;
using System.Text;
using NSec.Cryptography;

namespace BackupApp.Crypto;

public enum KdfProfileId : byte
{
    Argon2idStandard = 1,
    Argon2idHigh = 2
}

public sealed class KdfParameters
{
    public const int DefaultSaltSize = 16;
    public const int DefaultKeySize = 32;

    public KdfProfileId ProfileId { get; }
    public byte[] Salt { get; }
    public long MemorySizeKiB { get; }
    public int NumberOfPasses { get; }
    public int DegreeOfParallelism { get; }

    public KdfParameters(
        KdfProfileId profileId,
        byte[] salt,
        long memorySizeKiB,
        int numberOfPasses,
        int degreeOfParallelism)
    {
        ArgumentNullException.ThrowIfNull(salt);
        if (salt.Length < 16)
        {
            throw new ArgumentException("Salt must be at least 16 bytes.", nameof(salt));
        }

        ProfileId = profileId;
        Salt = (byte[])salt.Clone();
        MemorySizeKiB = memorySizeKiB;
        NumberOfPasses = numberOfPasses;
        DegreeOfParallelism = degreeOfParallelism;
    }

    public static KdfParameters CreateDefault(byte[]? salt = null)
    {
        var finalSalt = salt ?? RandomNumberGenerator.GetBytes(DefaultSaltSize);
        // RFC 9106 recommended interactive profile (64 MB, 3 passes, 1 thread)
        return new KdfParameters(
            profileId: KdfProfileId.Argon2idStandard,
            salt: finalSalt,
            memorySizeKiB: 65536, // 64 MB
            numberOfPasses: 3,
            degreeOfParallelism: 1
        );
    }

    public byte[] DeriveKey(string password, int outputKeyLength = DefaultKeySize)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (outputKeyLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(outputKeyLength), "Output key length must be positive.");
        }

        var parameters = new Argon2Parameters
        {
            DegreeOfParallelism = DegreeOfParallelism,
            MemorySize = MemorySizeKiB,
            NumberOfPasses = NumberOfPasses
        };

        var argon2 = new Argon2id(parameters);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        try
        {
            return argon2.DeriveBytes(passwordBytes, Salt, outputKeyLength);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
        }
    }
}
