using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace Notification.Shared.Telegram;

public sealed class TelegramOptions
{
    public const string SectionName = "Telegram";

    /// <summary>Token của bot, lấy từ @BotFather. Để trống thì kênh Telegram không gửi thật.</summary>
    public string BotToken { get; set; } = "";

    public bool Enabled => !string.IsNullOrWhiteSpace(BotToken);
}

public sealed record TelegramResponse<T>(
    bool Ok,
    T? Result,
    int? ErrorCode,
    string? Description,
    TelegramResponseParameters? Parameters);

public sealed record TelegramResponseParameters(int? RetryAfter);

public sealed record TelegramUser(long Id, string? FirstName, string? LastName, string? Username);

public sealed record TelegramChat(long Id, string Type, string? FirstName, string? LastName, string? Username);

public sealed record TelegramMessage(long MessageId, TelegramChat Chat, TelegramUser? From, string? Text);

public sealed record TelegramUpdate(long UpdateId, TelegramMessage? Message);

/// <summary>
/// Client tối giản cho Telegram Bot API (https://core.telegram.org/bots/api). URL chứa token của bot,
/// nên HttpClient dùng cho lớp này phải tắt log request để token không lọt vào log.
/// </summary>
public sealed class TelegramApi(HttpClient http, IOptions<TelegramOptions> options)
{
    public const string HttpClientName = "Telegram";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public Task<TelegramResponse<TelegramUser>> GetMeAsync(CancellationToken ct) =>
        CallAsync<TelegramUser>("getMe", new { }, ct);

    /// <summary>Long polling: chờ tối đa timeoutSeconds để nhận tin nhắn mới gửi tới bot.</summary>
    public Task<TelegramResponse<List<TelegramUpdate>>> GetUpdatesAsync(long offset, int timeoutSeconds,
        CancellationToken ct) =>
        CallAsync<List<TelegramUpdate>>("getUpdates",
            new { offset, timeout = timeoutSeconds, allowed_updates = new[] { "message" } }, ct);

    public Task<TelegramResponse<TelegramMessage>> SendMessageAsync(string chatId, string html, CancellationToken ct) =>
        CallAsync<TelegramMessage>("sendMessage", new { chat_id = chatId, text = html, parse_mode = "HTML" }, ct);

    private async Task<TelegramResponse<T>> CallAsync<T>(string method, object body, CancellationToken ct)
    {
        var url = $"https://api.telegram.org/bot{options.Value.BotToken}/{method}";
        using var response = await http.PostAsJsonAsync(url, body, Json, ct);
        return await response.Content.ReadFromJsonAsync<TelegramResponse<T>>(Json, ct)
               ?? new TelegramResponse<T>(false, default, (int)response.StatusCode, "Telegram trả phản hồi rỗng", null);
    }
}
