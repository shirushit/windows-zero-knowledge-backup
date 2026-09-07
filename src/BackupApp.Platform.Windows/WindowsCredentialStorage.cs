using System.Security.Cryptography;
using System.Text;

namespace BackupApp.Platform.Windows;

public interface ICredentialStorage
{
    byte[] ProtectData(byte[] userData);
    byte[] UnprotectData(byte[] encryptedData);
}

public sealed class DpapiCredentialStorage : ICredentialStorage
{
    private readonly byte[]? _entropy;

    public DpapiCredentialStorage(byte[]? optionalEntropy = null)
    {
        _entropy = optionalEntropy;
    }

    public byte[] ProtectData(byte[] userData)
    {
        return ProtectedData.Protect(userData, _entropy, DataProtectionScope.CurrentUser);
    }

    public byte[] UnprotectData(byte[] encryptedData)
    {
        return ProtectedData.Unprotect(encryptedData, _entropy, DataProtectionScope.CurrentUser);
    }
}
