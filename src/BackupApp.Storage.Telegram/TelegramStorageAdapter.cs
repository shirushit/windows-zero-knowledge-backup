using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using BackupApp.Domain;

namespace BackupApp.Storage.Telegram;

public sealed class TelegramStorageAdapter : IStorageProvider
{
    private readonly HttpClient _httpClient;
    private readonly TelegramStorageConfiguration _config;
    private readonly StorageRetryPolicy _retryPolicy;
    private readonly ConcurrentDictionary<string, (string FileId, long MessageId, long SizeBytes, string HashSha256, DateTimeOffset UploadedAt)> _objectIndex = new(StringComparer.Ordinal);
    private readonly List<ObjectId> _manifestAnchors = [];
    private readonly object _lock = new();

    public string ProviderId => "telegram";

    public TelegramStorageAdapter(
        TelegramStorageConfiguration config,
        HttpClient? httpClient = null,
        StorageRetryPolicy? retryPolicy = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? new HttpClient { Timeout = config.RequestTimeout };
        _retryPolicy = retryPolicy ?? new StorageRetryPolicy(maxRetries: 3);
    }

    public Task<StorageCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(new StorageCapabilities(
            MaxObjectSizeBytes: _config.MaxChunkSizeBytes,
            SupportsDeletion: true,
            SupportsRangeRequests: false,
            RecommendedChunkSizeBytes: Math.Min(_config.MaxChunkSizeBytes, 8 * 1024 * 1024)
        ));
    }

    public async Task ValidateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var getMeUrl = $"{_config.ApiBaseUrl}/bot{_config.BotToken}/getMe";
        using var getMeResponse = await _httpClient.GetAsync(new Uri(getMeUrl), cancellationToken).ConfigureAwait(false);

        if (getMeResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new ProviderAuthenticationException("Telegram bot token is invalid or revoked. Check bot credentials.");
        }

        getMeResponse.EnsureSuccessStatusCode();

        var getChatUrl = $"{_config.ApiBaseUrl}/bot{_config.BotToken}/getChat?chat_id={Uri.EscapeDataString(_config.TargetChatId)}";
        using var getChatResponse = await _httpClient.GetAsync(new Uri(getChatUrl), cancellationToken).ConfigureAwait(false);

        if (getChatResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.BadRequest)
        {
            throw new ProviderAuthenticationException($"Telegram bot cannot access target chat ID '{_config.TargetChatId}'. Verify chat ID and bot permissions.");
        }

        getChatResponse.EnsureSuccessStatusCode();
    }

    public async Task<RemoteObjectDescriptor> PutObjectAsync(
        ObjectId id,
        Stream contentStream,
        IProgress<long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(contentStream);

        return await _retryPolicy.ExecuteWithRetryAsync(async ct =>
        {
            using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            using var trackedStream = new ProgressAndHashingStream(contentStream, sha, progress);

            var sendDocUrl = $"{_config.ApiBaseUrl}/bot{_config.BotToken}/sendDocument";
            using var form = new MultipartFormDataContent();
            form.Add(new StringContent(_config.TargetChatId), "chat_id");
            form.Add(new StringContent($"BA_CHUNK_{id.Value}"), "caption");

            var streamContent = new StreamContent(trackedStream);
            form.Add(streamContent, "document", $"{id.Value}.bin");

            using var response = await _httpClient.PostAsync(new Uri(sendDocUrl), form, ct).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ProviderAuthenticationException("Telegram bot token is invalid or revoked.");
            }

            if ((int)response.StatusCode == 429)
            {
                var retryAfter = ParseRetryAfter(response);
                throw new RateLimitException("Telegram API rate limit exceeded.", retryAfter);
            }

            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var (fileId, messageId, fileSize) = ParseSendDocumentResponse(responseJson);

            var hashHex = Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
            var now = DateTimeOffset.UtcNow;

            _objectIndex[id.Value] = (fileId, messageId, fileSize, hashHex, now);

            lock (_lock)
            {
                if (!_manifestAnchors.Contains(id) && id.Value.Contains("manifest", StringComparison.OrdinalIgnoreCase))
                {
                    _manifestAnchors.Add(id);
                }
            }

            return new RemoteObjectDescriptor(
                id,
                fileSize,
                hashHex,
                now,
                $"tg:{messageId}:{fileId}"
            );
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public async Task<Stream> GetObjectAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        if (!_objectIndex.TryGetValue(id.Value, out var info))
        {
            throw new KeyNotFoundException($"Remote object '{id.Value}' has not been registered in Telegram index.");
        }

        return await _retryPolicy.ExecuteWithRetryAsync(async ct =>
        {
            // 1. Get file path from file_id
            var getFileUrl = $"{_config.ApiBaseUrl}/bot{_config.BotToken}/getFile?file_id={Uri.EscapeDataString(info.FileId)}";
            using var getFileResponse = await _httpClient.GetAsync(new Uri(getFileUrl), ct).ConfigureAwait(false);

            if (getFileResponse.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                throw new ProviderAuthenticationException("Telegram bot token is invalid or revoked.");
            }

            if ((int)getFileResponse.StatusCode == 429)
            {
                var retryAfter = ParseRetryAfter(getFileResponse);
                throw new RateLimitException("Telegram API rate limit exceeded.", retryAfter);
            }

            getFileResponse.EnsureSuccessStatusCode();

            var getFileJson = await getFileResponse.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var filePath = ParseFilePath(getFileJson);

            // 2. Download file content stream
            var downloadUrl = $"{_config.ApiBaseUrl}/file/bot{_config.BotToken}/{filePath}";
            var downloadResponse = await _httpClient.GetAsync(new Uri(downloadUrl), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            downloadResponse.EnsureSuccessStatusCode();

            return await downloadResponse.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public Task<bool> ExistsAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_objectIndex.ContainsKey(id.Value));
    }

    public async Task<bool> DeleteAsync(ObjectId id, CancellationToken cancellationToken = default)
    {
        if (!_objectIndex.TryRemove(id.Value, out var info))
        {
            return false;
        }

        lock (_lock)
        {
            _manifestAnchors.Remove(id);
        }

        return await _retryPolicy.ExecuteWithRetryAsync(async ct =>
        {
            var deleteUrl = $"{_config.ApiBaseUrl}/bot{_config.BotToken}/deleteMessage?chat_id={Uri.EscapeDataString(_config.TargetChatId)}&message_id={info.MessageId}";
            using var response = await _httpClient.GetAsync(new Uri(deleteUrl), ct).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }, cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<ObjectId>> EnumerateCatalogAnchorsAsync(CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            return Task.FromResult<IReadOnlyList<ObjectId>>(_manifestAnchors.ToList());
        }
    }

    public void RegisterKnownObject(ObjectId id, string fileId, long messageId, long sizeBytes, string hashHex)
    {
        _objectIndex[id.Value] = (fileId, messageId, sizeBytes, hashHex, DateTimeOffset.UtcNow);
        lock (_lock)
        {
            if (!_manifestAnchors.Contains(id) && id.Value.Contains("manifest", StringComparison.OrdinalIgnoreCase))
            {
                _manifestAnchors.Add(id);
            }
        }
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

    private static string ParseFilePath(string json)
    {
        var node = JsonNode.Parse(json);
        var ok = node?["ok"]?.GetValue<bool>() ?? false;
        if (!ok)
        {
            var desc = node?["description"]?.GetValue<string>() ?? "Unknown Telegram error";
            throw new HttpRequestException($"Telegram API error: {desc}");
        }

        return node?["result"]?["file_path"]?.GetValue<string>()
            ?? throw new HttpRequestException("Telegram response missing file_path.");
    }

    private static TimeSpan? ParseRetryAfter(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values))
        {
            var first = values.FirstOrDefault();
            if (int.TryParse(first, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
            {
                return TimeSpan.FromSeconds(seconds);
            }
        }
        return null;
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
