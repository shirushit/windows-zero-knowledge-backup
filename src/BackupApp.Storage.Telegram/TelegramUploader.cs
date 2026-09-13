using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using BackupApp.Domain;

namespace BackupApp.Storage.Telegram;

public sealed record RemoteUploadResult(
    string FileId,
    long MessageId,
    long FileSize,
    string HashSha256
);

public interface ITelegramUploader
{
    int MaxUploadsPerMinute { get; }
    Action<string>? OnRateLimitDelay { get; set; }

    Task<RemoteUploadResult> UploadDocumentAsync(
        ObjectId id,
        Stream contentStream,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default
    );
}

public sealed class TokenBucketRateLimiter : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly int _capacity;
    private readonly double _refillRatePerSecond;
    private double _currentTokens;
    private DateTimeOffset _lastRefillUtc;

    public int Capacity => _capacity;
    public double CurrentTokens => _currentTokens;

    public TokenBucketRateLimiter(int capacity = 20, int refillTokensPerMinute = 20)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be positive.");
        if (refillTokensPerMinute <= 0) throw new ArgumentOutOfRangeException(nameof(refillTokensPerMinute), "Refill rate must be positive.");

        _capacity = capacity;
        _refillRatePerSecond = (double)refillTokensPerMinute / 60.0;
        _currentTokens = capacity;
        _lastRefillUtc = DateTimeOffset.UtcNow;
    }

    public async Task WaitForTokenAsync(Action<string>? statusCallback = null, CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var now = DateTimeOffset.UtcNow;
                var elapsedSeconds = (now - _lastRefillUtc).TotalSeconds;
                if (elapsedSeconds > 0)
                {
                    _currentTokens = Math.Min(_capacity, _currentTokens + (elapsedSeconds * _refillRatePerSecond));
                    _lastRefillUtc = now;
                }

                if (_currentTokens >= 1.0)
                {
                    _currentTokens -= 1.0;
                    return;
                }

                var needed = 1.0 - _currentTokens;
                var waitSeconds = Math.Max(1.0, needed / _refillRatePerSecond);
                var totalWaitSec = (int)Math.Ceiling(waitSeconds);

                for (int s = totalWaitSec; s > 0; s--)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    statusCallback?.Invoke($"ממתין להפשרת קצב מטלגרם ({s} שניות)...");
                    await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}

public sealed class TelegramUploader : ITelegramUploader, IDisposable
{
    private readonly TelegramStorageConfiguration _config;
    private readonly HttpClient _httpClient;
    private readonly StorageRetryPolicy _retryPolicy;
    private readonly TokenBucketRateLimiter _rateLimiter;
    private readonly int _maxRetries;

    public int MaxUploadsPerMinute { get; }
    public Action<string>? OnRateLimitDelay { get; set; }

    public TelegramUploader(
        TelegramStorageConfiguration config,
        HttpClient? httpClient = null,
        StorageRetryPolicy? retryPolicy = null,
        int maxUploadsPerMinute = 25,
        int maxRetries = 3)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? new HttpClient { Timeout = config.RequestTimeout };
        _retryPolicy = retryPolicy ?? new StorageRetryPolicy(maxRetries: maxRetries);
        MaxUploadsPerMinute = Math.Clamp(maxUploadsPerMinute, 1, 30);
        _rateLimiter = new TokenBucketRateLimiter(capacity: MaxUploadsPerMinute, refillTokensPerMinute: MaxUploadsPerMinute);
        _maxRetries = maxRetries;
    }

    public void Dispose()
    {
        _rateLimiter.Dispose();
    }

    public async Task<RemoteUploadResult> UploadDocumentAsync(
        ObjectId id,
        Stream contentStream,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentStream);

        // 1. Proactive rate limiting: consume token from TokenBucket / SemaphoreSlim
        await _rateLimiter.WaitForTokenAsync(OnRateLimitDelay, cancellationToken).ConfigureAwait(false);

        int attempt = 0;
        while (true)
        {
            attempt++;
            cancellationToken.ThrowIfCancellationRequested();

            if (contentStream.CanSeek)
            {
                contentStream.Position = 0;
            }

            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var trackedStream = new ProgressAndHashingStream(contentStream, sha, progress);

            var sendDocUrl = $"{_config.ApiBaseUrl}/bot{_config.BotToken}/sendDocument";
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(_config.TargetChatId), "chat_id");
            form.Add(new StringContent($"BA_CHUNK_{id.Value}"), "caption");

            var streamContent = new StreamContent(trackedStream);
            form.Add(streamContent, "document", $"{id.Value}.bin");

            HttpResponseMessage? response = null;
            try
            {
                response = await _httpClient.PostAsync(new Uri(sendDocUrl), form, cancellationToken).ConfigureAwait(false);

                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                {
                    throw new ProviderAuthenticationException("Telegram bot token is invalid or revoked.");
                }

                if ((int)response.StatusCode == 429)
                {
                    // Requirement 2: Extract retry_after_seconds, wait retry_after + 1 second
                    var retryAfter = await ParseRetryAfterAsync(response).ConfigureAwait(false);
                    var retrySeconds = retryAfter.HasValue && retryAfter.Value.TotalSeconds > 0
                        ? (int)Math.Ceiling(retryAfter.Value.TotalSeconds)
                        : 5;

                    var totalWaitSeconds = retrySeconds + 1; // + 1 second buffer

                    // Requirement 3: Second-by-second countdown notification
                    for (int s = totalWaitSeconds; s > 0; s--)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        OnRateLimitDelay?.Invoke($"ממתין להפשרת קצב מטלגרם ({s} שניות)...");
                        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
                    }

                    if (attempt <= _maxRetries)
                    {
                        continue;
                    }

                    throw new RateLimitException("Telegram API rate limit exceeded.", TimeSpan.FromSeconds(totalWaitSeconds));
                }

                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var (fileId, messageId, fileSize) = ParseSendDocumentResponse(responseJson);

                var hashHex = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();

                // Clear rate limit status on successful upload
                OnRateLimitDelay?.Invoke(string.Empty);

                return new RemoteUploadResult(fileId, messageId, fileSize, hashHex);
            }
            catch (Exception ex) when (StorageRetryPolicy.IsTransient(ex) && attempt <= _maxRetries && ex is not RateLimitException)
            {
                var delay = TimeSpan.FromMilliseconds(500 * Math.Pow(2, attempt - 1));
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                response?.Dispose();
            }
        }
    }

    public static async Task<TimeSpan?> ParseRetryAfterAsync(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var first = values.FirstOrDefault();
            if (int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }

        try
        {
            var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var node = JsonNode.Parse(body);
            var retryAfterSec = node?["parameters"]?["retry_after"]?.GetValue<int>();
            if (retryAfterSec.HasValue && retryAfterSec.Value > 0)
            {
                return TimeSpan.FromSeconds(retryAfterSec.Value);
            }
        }
        catch
        {
            // Ignore parse errors on body
        }

        return null;
    }

    private static (string FileId, long MessageId, long FileSize) ParseSendDocumentResponse(string json)
    {
        var node = JsonNode.Parse(json);
        var ok = node?["ok"]?.GetValue<bool>() ?? false;
        if (!ok)
        {
            var desc = node?["description"]?.GetValue<string>() ?? "Unknown Telegram error";
            throw new HttpRequestException($"Telegram API error: {desc}");
        }

        var result = node?["result"];
        var messageId = result?["message_id"]?.GetValue<long>() ?? 0;
        var document = result?["document"];
        var fileId = document?["file_id"]?.GetValue<string>()
            ?? throw new HttpRequestException("Telegram response missing file_id.");
        var fileSize = document?["file_size"]?.GetValue<long>() ?? 0;

        return (fileId, messageId, fileSize);
    }

    private sealed class ProgressAndHashingStream : Stream
    {
        private readonly Stream _inner;
        private readonly IncrementalHash _hash;
        private readonly IProgress<long>? _progress;
        private long _totalRead;

        public ProgressAndHashingStream(Stream inner, IncrementalHash hash, IProgress<long>? progress)
        {
            _inner = inner;
            _hash = hash;
            _progress = progress;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = _inner.Read(buffer, offset, count);
            if (read > 0)
            {
                _hash.AppendData(buffer, offset, read);
                _totalRead += read;
                _progress?.Report(_totalRead);
            }
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read > 0)
            {
                _hash.AppendData(buffer.Span[..read]);
                _totalRead += read;
                _progress?.Report(_totalRead);
            }
            return read;
        }

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
