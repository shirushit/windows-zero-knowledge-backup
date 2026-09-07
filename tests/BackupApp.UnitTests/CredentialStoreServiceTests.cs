using System.Security.Cryptography;
using BackupApp.Platform.Windows;
using BackupApp.UI.Services;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class CredentialStoreServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _credentialsPath;

    public CredentialStoreServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"cred_tests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _credentialsPath = Path.Combine(_testDir, "credentials.dat");
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(_testDir))
        {
            try
            {
                Directory.Delete(_testDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    [Fact]
    public void LoadTelegramCredentials_WhenFileDoesNotExist_ShouldReturnNull()
    {
        var service = new CredentialStoreService(_credentialsPath);
        var result = service.LoadTelegramCredentials();
        result.Should().BeNull();
    }

    [Fact]
    public void SaveAndLoadTelegramCredentials_ShouldRoundTripCorrectly()
    {
        var service = new CredentialStoreService(_credentialsPath);
        var credentials = new TelegramCredentials("123456789:ABCdefGHIjklMNOpqrsTUVwxyz", "-1001234567890");

        service.SaveTelegramCredentials(credentials);

        File.Exists(_credentialsPath).Should().BeTrue();

        // The file content should be DPAPI-encrypted, not raw plaintext JSON
        var rawBytes = File.ReadAllBytes(_credentialsPath);
        var rawString = System.Text.Encoding.UTF8.GetString(rawBytes);
        rawString.Should().NotContain("123456789:ABCdefGHIjklMNOpqrsTUVwxyz");

        var loaded = service.LoadTelegramCredentials();
        loaded.Should().NotBeNull();
        loaded!.BotToken.Should().Be("123456789:ABCdefGHIjklMNOpqrsTUVwxyz");
        loaded.ChatId.Should().Be("-1001234567890");
    }

    [Fact]
    public void ClearTelegramCredentials_ShouldDeleteCredentialsFile()
    {
        var service = new CredentialStoreService(_credentialsPath);
        var credentials = new TelegramCredentials("sample_token", "sample_chat");

        service.SaveTelegramCredentials(credentials);
        File.Exists(_credentialsPath).Should().BeTrue();

        service.ClearTelegramCredentials();
        File.Exists(_credentialsPath).Should().BeFalse();
    }

    [Fact]
    public void LoadTelegramCredentials_WhenCorrupted_ShouldCatchAndReturnNull()
    {
        var service = new CredentialStoreService(_credentialsPath);
        File.WriteAllBytes(_credentialsPath, [0x01, 0x02, 0x03, 0x04, 0x05]); // Invalid DPAPI blob

        var loaded = service.LoadTelegramCredentials();
        loaded.Should().BeNull();
    }

    [Fact]
    public void SaveTelegramCredentials_WhenNull_ShouldThrowArgumentNullException()
    {
        var service = new CredentialStoreService(_credentialsPath);
        var act = () => service.SaveTelegramCredentials(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
