namespace BackupApp.Storage.Telegram;

public sealed record TelegramStorageConfiguration
{
    public const string DefaultApiBaseUrl = "https://api.telegram.org";
    public const int DefaultMaxChunkSizeBytes = 32 * 1024 * 1024; // 32MB safe chunk size for bot API (limit is 50MB)

    public string BotToken { get; init; }
    public string TargetChatId { get; init; }
    public string ApiBaseUrl { get; init; } = DefaultApiBaseUrl;
    public int MaxChunkSizeBytes { get; init; } = DefaultMaxChunkSizeBytes;
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(180);

    public TelegramStorageConfiguration(string botToken, string targetChatId, string? apiBaseUrl = null)
    {
        ArgumentNullException.ThrowIfNull(botToken);
        ArgumentNullException.ThrowIfNull(targetChatId);

        if (string.IsNullOrWhiteSpace(botToken))
        {
            throw new ArgumentException("Bot token cannot be empty.", nameof(botToken));
        }

        if (string.IsNullOrWhiteSpace(targetChatId))
        {
            throw new ArgumentException("Target chat ID cannot be empty.", nameof(targetChatId));
        }

        BotToken = botToken;
        TargetChatId = targetChatId;
        if (!string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            ApiBaseUrl = apiBaseUrl;
        }
    }

    public override string ToString()
    {
        var redactedToken = BotToken.Length > 8
            ? BotToken[..4] + "..." + BotToken[^4..]
            : "***";
        return $"TelegramStorageConfiguration [ChatId: {TargetChatId}, Token: {redactedToken}, Url: {ApiBaseUrl}]";
    }
}
