using System.Net;
using System.Net.Http;
using System.Text;
using BackupApp.Domain;
using BackupApp.Storage;
using BackupApp.Storage.Telegram;
using FluentAssertions;
using Xunit;

namespace BackupApp.UnitTests;

public class TelegramStorageTests
{
    [Fact]
    public void TelegramStorageConfiguration_ToString_ShouldRedactSecretToken()
    {
        var config = new TelegramStorageConfiguration("123456789:ABCdefGHI_VerySecretTokenValue123", "-1001234567890");
        var str = config.ToString();

        str.Should().NotContain("ABCdefGHI_VerySecretTokenValue123");
        str.Should().Contain("1234...e123");
        str.Should().Contain("-1001234567890");
    }

    [Fact]
    public async Task TelegramStorageAdapter_PutAndGet_WithMockHttp_ShouldSucceed()
    {
        var testData = Encoding.UTF8.GetBytes("Encrypted chunk binary payload");
        var mockHandler = new MockTelegramHttpMessageHandler(testData);
        var httpClient = new HttpClient(mockHandler);

        var config = new TelegramStorageConfiguration("test_bot_token", "test_chat_id", "https://api.telegram.mock");
        var adapter = new TelegramStorageAdapter(config, httpClient);

        var id = ObjectId.FromHex("3333333333333333333333333333333333333333333333333333333333333333");
        using var stream = new MemoryStream(testData);

        var descriptor = await adapter.PutObjectAsync(id, stream);
        descriptor.Id.Should().Be(id);
        descriptor.SizeBytes.Should().Be(testData.Length);
        descriptor.ProviderReference.Should().Contain("tg:42:file_id_mock_123");

        // Download object
        using var downloadedStream = await adapter.GetObjectAsync(id);
        using var memory = new MemoryStream();
        await downloadedStream.CopyToAsync(memory);

        memory.ToArray().Should().BeEquivalentTo(testData);
    }

    [Fact]
    public async Task TelegramStorageAdapter_InvalidToken_ShouldThrowProviderAuthenticationException()
    {
        var mockHandler = new MockTelegramHttpMessageHandler([], simulateAuthError: true);
        var httpClient = new HttpClient(mockHandler);
        var config = new TelegramStorageConfiguration("bad_token", "chat_id", "https://api.telegram.mock");
        var adapter = new TelegramStorageAdapter(config, httpClient);

        var act = () => adapter.ValidateConnectionAsync();
        await act.Should().ThrowAsync<ProviderAuthenticationException>();
    }

    [Fact]
    public async Task TokenBucketRateLimiter_ShouldAllowImmediateBurst_ThenEnforceRateLimit()
    {
        var limiter = new TokenBucketRateLimiter(capacity: 2, refillTokensPerMinute: 60); // 1 token/sec
        limiter.Capacity.Should().Be(2);

        // Immediate bursts
        await limiter.WaitForTokenAsync();
        await limiter.WaitForTokenAsync();

        limiter.CurrentTokens.Should().BeLessThan(1.0);
    }

    [Fact]
    public async Task TelegramUploader_OnHttp429_ShouldWaitForRetryAfterPlusOneSecond_AndInvokeCallback()
    {
        var testData = Encoding.UTF8.GetBytes("Test chunk payload");
        var mockHandler = new MockTelegramHttpMessageHandler(testData, simulate429Count: 1, retryAfterSeconds: 1);
        var httpClient = new HttpClient(mockHandler);

        var config = new TelegramStorageConfiguration("test_bot_token", "test_chat_id", "https://api.telegram.mock");
        var uploader = new TelegramUploader(config, httpClient);

        var reportedMessages = new List<string>();
        uploader.OnRateLimitDelay = msg => reportedMessages.Add(msg);

        var id = ObjectId.FromHex("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        using var stream = new MemoryStream(testData);

        var result = await uploader.UploadDocumentAsync(id, stream);

        result.FileId.Should().Be("file_id_mock_123");
        result.MessageId.Should().Be(42);

        // 1 second retry_after + 1 second buffer = 2 seconds total countdown
        reportedMessages.Should().Contain(m => m.Contains("2 שניות"));
        reportedMessages.Should().Contain(m => m.Contains("1 שניות"));
    }

    [Fact]
    public async Task TelegramUploader_ParseRetryAfter_HeadersAndBody()
    {
        var responseWithHeader = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        responseWithHeader.Headers.Add("Retry-After", "15");
        var headerDelay = await TelegramUploader.ParseRetryAfterAsync(responseWithHeader);
        headerDelay.Should().Be(TimeSpan.FromSeconds(15));

        var responseWithJson = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
        {
            Content = new StringContent("{\"ok\":false,\"error_code\":429,\"parameters\":{\"retry_after\":12}}")
        };
        var jsonDelay = await TelegramUploader.ParseRetryAfterAsync(responseWithJson);
        jsonDelay.Should().Be(TimeSpan.FromSeconds(12));
    }

    private sealed class MockTelegramHttpMessageHandler : HttpMessageHandler
    {
        private readonly byte[] _fileData;
        private readonly bool _simulateAuthError;
        private int _simulate429Count;
        private readonly int _retryAfterSeconds;

        public MockTelegramHttpMessageHandler(
            byte[] fileData,
            bool simulateAuthError = false,
            int simulate429Count = 0,
            int retryAfterSeconds = 1)
        {
            _fileData = fileData;
            _simulateAuthError = simulateAuthError;
            _simulate429Count = simulate429Count;
            _retryAfterSeconds = retryAfterSeconds;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_simulateAuthError)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                {
                    Content = new StringContent("{\"ok\":false,\"error_code\":401,\"description\":\"Unauthorized\"}")
                });
            }

            var path = request.RequestUri?.AbsolutePath ?? string.Empty;

            if (_simulate429Count > 0 && path.EndsWith("/sendDocument", StringComparison.OrdinalIgnoreCase))
            {
                _simulate429Count--;
                var resp = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                {
                    Content = new StringContent($"{{\"ok\":false,\"error_code\":429,\"description\":\"Too Many Requests\",\"parameters\":{{\"retry_after\":{_retryAfterSeconds}}}}}")
                };
                return Task.FromResult(resp);
            }

            if (path.EndsWith("/getMe", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"result\":{\"id\":12345,\"is_bot\":true,\"first_name\":\"BackupBot\"}}")
                });
            }

            if (path.EndsWith("/getChat", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"ok\":true,\"result\":{\"id\":-100123,\"title\":\"BackupChannel\"}}")
                });
            }

            if (path.EndsWith("/sendDocument", StringComparison.OrdinalIgnoreCase))
            {
                var responseJson = "{\"ok\":true,\"result\":{\"message_id\":42,\"document\":{\"file_id\":\"file_id_mock_123\",\"file_size\":" + _fileData.Length + "}}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson)
                });
            }

            if (path.EndsWith("/getFile", StringComparison.OrdinalIgnoreCase))
            {
                var responseJson = "{\"ok\":true,\"result\":{\"file_id\":\"file_id_mock_123\",\"file_path\":\"documents/file_123.bin\"}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson)
                });
            }

            if (path.Contains("/file/bot", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(_fileData)
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
