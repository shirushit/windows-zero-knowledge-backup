using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace BackupApp.Crypto;

public interface ICredentialStorage
{
    byte[] ProtectData(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> optionalEntropy = default);
    byte[] UnprotectData(ReadOnlySpan<byte> encryptedData, ReadOnlySpan<byte> optionalEntropy = default);
    void StoreSecret(string keyName, string secret);
    string? RetrieveSecret(string keyName);
    void RemoveSecret(string keyName);
}

[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialStorage : ICredentialStorage
{
    private readonly string _storageDirectory;

    public WindowsCredentialStorage(string? storageDirectory = null)
    {
        _storageDirectory = storageDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BackupApp",
            "Security"
        );

        if (!Directory.Exists(_storageDirectory))
        {
            Directory.CreateDirectory(_storageDirectory);
        }
    }

    public byte[] ProtectData(ReadOnlySpan<byte> plaintext, ReadOnlySpan<byte> optionalEntropy = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI is only supported on Windows.");
        }

        byte[]? entropyArray = optionalEntropy.IsEmpty ? null : optionalEntropy.ToArray();
        return ProtectedData.Protect(plaintext.ToArray(), entropyArray, DataProtectionScope.CurrentUser);
    }

    public byte[] UnprotectData(ReadOnlySpan<byte> encryptedData, ReadOnlySpan<byte> optionalEntropy = default)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("DPAPI is only supported on Windows.");
        }

        byte[]? entropyArray = optionalEntropy.IsEmpty ? null : optionalEntropy.ToArray();
        return ProtectedData.Unprotect(encryptedData.ToArray(), entropyArray, DataProtectionScope.CurrentUser);
    }

    public void StoreSecret(string keyName, string secret)
    {
        ArgumentNullException.ThrowIfNull(keyName);
        ArgumentNullException.ThrowIfNull(secret);

        var filePath = GetSecretFilePath(keyName);
        var plaintextBytes = Encoding.UTF8.GetBytes(secret);
        try
        {
            var protectedBytes = ProtectData(plaintextBytes);
            File.WriteAllBytes(filePath, protectedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    public string? RetrieveSecret(string keyName)
    {
        ArgumentNullException.ThrowIfNull(keyName);

        var filePath = GetSecretFilePath(keyName);
        if (!File.Exists(filePath))
        {
            return null;
        }

        var protectedBytes = File.ReadAllBytes(filePath);
        var decryptedBytes = UnprotectData(protectedBytes);
        try
        {
            return Encoding.UTF8.GetString(decryptedBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(decryptedBytes);
        }
    }

    public void RemoveSecret(string keyName)
    {
        ArgumentNullException.ThrowIfNull(keyName);
        var filePath = GetSecretFilePath(keyName);
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }

    private string GetSecretFilePath(string keyName)
    {
        var safeName = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(keyName))) + ".dpapi";
        return Path.Combine(_storageDirectory, safeName);
    }
}
