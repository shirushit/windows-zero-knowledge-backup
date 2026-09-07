using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BackupApp.Platform.Windows;

namespace BackupApp.UI.Services;

public sealed record TelegramCredentials(string BotToken, string ChatId);

public interface ICredentialStoreService
{
    void SaveTelegramCredentials(TelegramCredentials credentials);
    TelegramCredentials? LoadTelegramCredentials();
    void ClearTelegramCredentials();
}

public sealed class CredentialStoreService : ICredentialStoreService
{
    private readonly string _credentialsFilePath;
    private readonly ICredentialStorage _credentialStorage;

    public CredentialStoreService(string? customPath = null, ICredentialStorage? credentialStorage = null)
    {
        _credentialStorage = credentialStorage ?? new DpapiCredentialStorage();
        if (!string.IsNullOrEmpty(customPath))
        {
            _credentialsFilePath = customPath;
        }
        else
        {
            var appDataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BackupApp");
            Directory.CreateDirectory(appDataDir);
            _credentialsFilePath = Path.Combine(appDataDir, "credentials.dat");
        }
    }

    public void SaveTelegramCredentials(TelegramCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);

        var json = JsonSerializer.Serialize(credentials);
        var plaintextBytes = Encoding.UTF8.GetBytes(json);
        var protectedBytes = _credentialStorage.ProtectData(plaintextBytes);

        var dir = Path.GetDirectoryName(_credentialsFilePath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllBytes(_credentialsFilePath, protectedBytes);
    }

    public TelegramCredentials? LoadTelegramCredentials()
    {
        if (!File.Exists(_credentialsFilePath))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(_credentialsFilePath);
            var plaintextBytes = _credentialStorage.UnprotectData(protectedBytes);
            var json = Encoding.UTF8.GetString(plaintextBytes);
            return JsonSerializer.Deserialize<TelegramCredentials>(json);
        }
        catch (CryptographicException)
        {
            // Decryption failed or different user/machine context
            return null;
        }
    }

    public void ClearTelegramCredentials()
    {
        if (File.Exists(_credentialsFilePath))
        {
            try
            {
                File.Delete(_credentialsFilePath);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }
}
