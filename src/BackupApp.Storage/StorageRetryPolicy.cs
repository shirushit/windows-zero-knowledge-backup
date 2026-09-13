using System.Security.Cryptography;

namespace BackupApp.Storage;

public sealed class StorageRetryPolicy
{
    private readonly int _maxRetries;
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;

    public StorageRetryPolicy(int maxRetries = 3, TimeSpan? initialDelay = null, TimeSpan? maxDelay = null)
    {
        _maxRetries = maxRetries;
        _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(500);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(30);
    }

    public async Task<T> ExecuteWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> action,
        Action<Exception, int, TimeSpan>? onRetry = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);

        int attempt = 0;
        while (true)
        {
            attempt++;
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex) && attempt <= _maxRetries)
            {
                var delay = CalculateDelay(attempt, ex);
                onRetry?.Invoke(ex, attempt, delay);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private TimeSpan CalculateDelay(int attempt, Exception ex)
    {
        if (ex is RateLimitException rateEx && rateEx.RetryAfter.HasValue)
        {
            return rateEx.RetryAfter.Value + TimeSpan.FromSeconds(1);
        }

        // Exponential backoff: initial * 2^(attempt - 1)
        var exponentialMs = _initialDelay.TotalMilliseconds * Math.Pow(2, attempt - 1);
        var cappedMs = Math.Min(exponentialMs, _maxDelay.TotalMilliseconds);

        // Add 20% random jitter
        var jitterMultiplier = 0.8 + (RandomNumberGenerator.GetInt32(0, 400) / 1000.0);
        var finalMs = cappedMs * jitterMultiplier;

        return TimeSpan.FromMilliseconds(finalMs);
    }

    public static bool IsTransient(Exception ex)
    {
        return ex switch
        {
            RateLimitException => true,
            HttpRequestException => true,
            IOException => true,
            TimeoutException => true,
            OperationCanceledException => false,
            _ => false
        };
    }
}

public class RateLimitException : HttpRequestException
{
    public TimeSpan? RetryAfter { get; }

    public RateLimitException(string message, TimeSpan? retryAfter = null, Exception? inner = null)
        : base(message, inner)
    {
        RetryAfter = retryAfter;
    }
}

public class ProviderAuthenticationException : Exception
{
    public ProviderAuthenticationException(string message, Exception? inner = null)
        : base(message, inner)
    {
    }
}
