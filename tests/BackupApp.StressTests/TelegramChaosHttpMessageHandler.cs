using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BackupApp.StressTests;

public sealed class ChaosOptions
{
    public double FaultProbability { get; init; } = 0.3; // 30% chance of fault
    public int MaxConsecutiveFaultsPerResource { get; init; } = 2; // Allow success on 3rd attempt
    public int LatencyMinMs { get; init; } = 10;
    public int LatencyMaxMs { get; init; } = 50;
    public bool InjectRateLimit429 { get; init; } = true;
    public bool InjectNetworkReset { get; init; } = true;
    public bool InjectServerError500 { get; init; } = true;
}

public sealed class TelegramChaosHttpMessageHandler : HttpMessageHandler
{
    private readonly ChaosOptions _options;
    private readonly ConcurrentDictionary<string, byte[]> _storage = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _fileIdToPath = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _consecutiveFaults = new(StringComparer.Ordinal);
    private readonly Random _random = new(999);
    private long _messageIdCounter = 1000;

    public int Injected429Count { get; private set; }
    public int InjectedNetworkResetCount { get; private set; }
    public int Injected500Count { get; private set; }
    public int TotalRequestsServed { get; private set; }

    public TelegramChaosHttpMessageHandler(ChaosOptions? options = null)
    {
        _options = options ?? new ChaosOptions();
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        TotalRequestsServed++;
        var url = request.RequestUri?.ToString() ?? string.Empty;

        // Simulate network latency
        if (_options.LatencyMaxMs > 0)
        {
            var delay = _random.Next(_options.LatencyMinMs, _options.LatencyMaxMs);
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
        }

        // Check if we should inject chaos on this request (for document upload/download)
        if (url.Contains("sendDocument", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/file/bot", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("getFile", StringComparison.OrdinalIgnoreCase))
        {
            var resourceKey = request.RequestUri?.PathAndQuery ?? "unknown";
            var currentFaults = _consecutiveFaults.GetOrAdd(resourceKey, 0);

            if (currentFaults < _options.MaxConsecutiveFaultsPerResource && _random.NextDouble() < _options.FaultProbability)
            {
                _consecutiveFaults[resourceKey] = currentFaults + 1;

                // Pick fault type
                var faultChoice = _random.Next(0, 3);
                if (faultChoice == 0 && _options.InjectRateLimit429)
                {
                    Injected429Count++;
                    var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests)
                    {
                        Content = new StringContent(
                            "{\"ok\":false,\"error_code\":429,\"description\":\"Too Many Requests: retry after 1\",\"parameters\":{\"retry_after\":1}}",
                            Encoding.UTF8,
                            "application/json")
                    };
                    response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
                    return response;
                }
                else if (faultChoice == 1 && _options.InjectNetworkReset)
                {
                    InjectedNetworkResetCount++;
                    throw new HttpRequestException("The TCP connection was reset by peer (simulated chaos).");
                }
                else if (_options.InjectServerError500)
                {
                    Injected500Count++;
                    return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                    {
                        Content = new StringContent(
                            "{\"ok\":false,\"error_code\":500,\"description\":\"Internal Server Error (simulated chaos)\"}",
                            Encoding.UTF8,
                            "application/json")
                    };
                }
            }
            else
            {
                // Reset faults counter for this resource
                _consecutiveFaults[resourceKey] = 0;
            }
        }

        // Handle standard endpoints
        if (url.Contains("/getMe", StringComparison.OrdinalIgnoreCase))
        {
            return JsonResponse("{\"ok\":true,\"result\":{\"id\":12345,\"is_bot\":true,\"first_name\":\"ChaosBot\"}}");
        }

        if (url.Contains("/getChat", StringComparison.OrdinalIgnoreCase))
        {
            return JsonResponse("{\"ok\":true,\"result\":{\"id\":99999,\"type\":\"supergroup\",\"title\":\"Backup Vault\"}}");
        }

        if (url.Contains("deleteMessage", StringComparison.OrdinalIgnoreCase))
        {
            return JsonResponse("{\"ok\":true,\"result\":true}");
        }

        if (url.Contains("sendDocument", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleSendDocumentAsync(request, cancellationToken).ConfigureAwait(false);
        }

        if (url.Contains("getFile", StringComparison.OrdinalIgnoreCase))
        {
            var query = request.RequestUri?.Query ?? string.Empty;
            var fileId = "unknown";
            foreach (var part in query.TrimStart('?').Split('&'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && kv[0] == "file_id")
                {
                    fileId = Uri.UnescapeDataString(kv[1]);
                    break;
                }
            }

            if (!_fileIdToPath.TryGetValue(fileId, out var filePath))
            {
                filePath = $"documents/{fileId}.dat";
                _fileIdToPath[fileId] = filePath;
            }

            return JsonResponse($"{{\"ok\":true,\"result\":{{\"file_id\":\"{fileId}\",\"file_path\":\"{filePath}\"}}}}");
        }

        if (url.Contains("/file/bot", StringComparison.OrdinalIgnoreCase))
        {
            var segments = request.RequestUri?.Segments;
            var path = segments != null ? string.Join("", segments.Skip(2)) : string.Empty;

            // Find file bytes by matching path
            foreach (var (fileId, storedPath) in _fileIdToPath)
            {
                if (path.EndsWith(storedPath, StringComparison.OrdinalIgnoreCase) ||
                    storedPath.EndsWith(path, StringComparison.OrdinalIgnoreCase))
                {
                    if (_storage.TryGetValue(fileId, out var data))
                    {
                        var resp = new HttpResponseMessage(HttpStatusCode.OK)
                        {
                            Content = new ByteArrayContent(data)
                        };
                        resp.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                        return resp;
                    }
                }
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        return JsonResponse("{\"ok\":true}");
    }

    private async Task<HttpResponseMessage> HandleSendDocumentAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] documentBytes = [];
        var fileName = "unknown";

        if (request.Content is MultipartFormDataContent multipart)
        {
            foreach (var content in multipart)
            {
                if (content.Headers.ContentDisposition?.Name == "document")
                {
                    fileName = content.Headers.ContentDisposition.FileName ?? "chunk.bin";
                    documentBytes = await content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
                }
            }
        }
        else if (request.Content != null)
        {
            documentBytes = await request.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }

        var fileId = $"tg_file_{Guid.NewGuid():N}";
        var filePath = $"documents/{fileId}_{fileName}";
        var msgId = Interlocked.Increment(ref _messageIdCounter);

        _storage[fileId] = documentBytes;
        _fileIdToPath[fileId] = filePath;

        var json = $"{{\"ok\":true,\"result\":{{\"message_id\":{msgId},\"document\":{{\"file_id\":\"{fileId}\",\"file_name\":\"{fileName}\",\"file_size\":{documentBytes.Length}}}}}}}";
        return JsonResponse(json);
    }

    private static HttpResponseMessage JsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }
}
