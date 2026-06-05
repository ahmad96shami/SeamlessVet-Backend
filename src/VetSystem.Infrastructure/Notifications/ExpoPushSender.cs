using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VetSystem.Application.Notifications;

namespace VetSystem.Infrastructure.Notifications;

/// <summary>
/// M21 — <see cref="IPushSender"/> over the Expo Push HTTP API (one endpoint, hand-rolled typed
/// client; the community .NET SDK is unmaintained). Messages go out in batches of ≤100 (Expo's
/// per-request cap); each response ticket is matched to its message by position, and a
/// <c>DeviceNotRegistered</c> ticket marks that token for pruning. Everything else —
/// transport errors, non-2xx, malformed bodies — is logged and swallowed: push is best-effort
/// and must never fail the caller. Receipts polling is deliberately out of scope (BUSINESS_FLOWS
/// §13); ticket-level pruning covers the dominant stale-token case at this scale.
/// </summary>
public sealed class ExpoPushSender : IPushSender
{
    /// <summary>Expo rejects requests with more than 100 messages.</summary>
    internal const int MaxBatchSize = 100;

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _http;
    private readonly ExpoPushOptions _options;
    private readonly ILogger<ExpoPushSender> _logger;

    public ExpoPushSender(HttpClient http, IOptions<ExpoPushOptions> options, ILogger<ExpoPushSender> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyCollection<string>> SendAsync(
        IReadOnlyCollection<PushMessage> messages,
        CancellationToken cancellationToken)
    {
        if (messages.Count == 0)
        {
            return [];
        }

        var deadTokens = new List<string>();
        foreach (var batch in messages.Chunk(MaxBatchSize))
        {
            await SendBatchAsync(batch, deadTokens, cancellationToken);
        }

        return deadTokens;
    }

    private async Task SendBatchAsync(
        IReadOnlyList<PushMessage> batch,
        List<string> deadTokens,
        CancellationToken cancellationToken)
    {
        var body = batch.Select(m => new ExpoMessage(
            To: m.Token,
            Title: m.Title,
            Body: m.Body,
            Data: m.Data,
            // The Mo7 HIGH-importance channel — heads-up presentation on Android.
            ChannelId: "default",
            Priority: "high")).ToArray();

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.BaseUrl)
            {
                Content = JsonContent.Create(body, options: SerializerOptions),
            };
            if (!string.IsNullOrEmpty(_options.AccessToken))
            {
                request.Headers.Authorization = new("Bearer", _options.AccessToken);
            }

            using var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Expo push batch of {Count} rejected with {StatusCode}; dropping (push is best-effort)",
                    batch.Count, (int)response.StatusCode);
                return;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            CollectDeadTokens(doc, batch, deadTokens);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Expo push batch of {Count} failed in transit; dropping (push is best-effort)", batch.Count);
        }
    }

    /// <summary>Tickets come back positionally — <c>data[i]</c> answers <c>batch[i]</c>.</summary>
    private void CollectDeadTokens(JsonDocument doc, IReadOnlyList<PushMessage> batch, List<string> deadTokens)
    {
        if (!doc.RootElement.TryGetProperty("data", out var tickets) || tickets.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        var i = 0;
        foreach (var ticket in tickets.EnumerateArray())
        {
            if (i >= batch.Count)
            {
                break;
            }

            if (ticket.TryGetProperty("status", out var status)
                && status.ValueGetString() == "error")
            {
                var error = ticket.TryGetProperty("details", out var details)
                    && details.ValueKind == JsonValueKind.Object
                    && details.TryGetProperty("error", out var code)
                        ? code.ValueGetString()
                        : null;

                if (error == "DeviceNotRegistered")
                {
                    deadTokens.Add(batch[i].Token);
                }
                else
                {
                    _logger.LogWarning(
                        "Expo push ticket error {Error} for one message (kept token)",
                        error ?? ticket.ToString());
                }
            }

            i++;
        }
    }

    private sealed record ExpoMessage(
        string To,
        string? Title,
        string? Body,
        IReadOnlyDictionary<string, object?> Data,
        string ChannelId,
        string Priority);
}

file static class JsonElementExtensions
{
    /// <summary>GetString() that tolerates non-string kinds (defensive against odd tickets).</summary>
    public static string? ValueGetString(this JsonElement element)
        => element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}
