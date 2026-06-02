using System.Text.Json;
using System.Text.Json.Nodes;
using VetSystem.Domain.Entities;

namespace VetSystem.Application.Settings;

/// <summary>
/// The night-stay (مبيت, PRD §18.6) tunables that live inside <c>system_settings.extra</c> rather
/// than as first-class columns: a per-night cost per <see cref="CareType"/> and the daily checkout
/// hour used by the hotel-style day-count rule. Stored under the <c>"nightStay"</c> key so the rest
/// of the <c>extra</c> bag is preserved across writes. Absent/blank config resolves to
/// <see cref="Default"/> (zero rates, 12:00 checkout) — safe defaults that bill nothing until an
/// admin sets the rates.
/// </summary>
public sealed record NightStaySettings(
    decimal RateMedical,
    decimal RateIcu,
    decimal RateHotel,
    int CheckoutHour)
{
    public const string ExtraKey = "nightStay";
    public const int DefaultCheckoutHour = 12;

    public static readonly NightStaySettings Default = new(0m, 0m, 0m, DefaultCheckoutHour);

    /// <summary>The per-night cost for a care type; unknown types resolve to 0.</summary>
    public decimal RateFor(string careType) => careType switch
    {
        CareType.Medical => RateMedical,
        CareType.Icu => RateIcu,
        CareType.Hotel => RateHotel,
        _ => 0m,
    };

    /// <summary>Reads the <c>"nightStay"</c> object out of an <c>extra</c> JSON string.</summary>
    public static NightStaySettings FromExtra(string? extra)
    {
        if (string.IsNullOrWhiteSpace(extra))
        {
            return Default;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(extra);
        }
        catch (JsonException)
        {
            return Default;
        }

        if (root?[ExtraKey] is not JsonObject ns)
        {
            return Default;
        }

        return new NightStaySettings(
            RateMedical: Dec(ns, "rateMedical"),
            RateIcu: Dec(ns, "rateIcu"),
            RateHotel: Dec(ns, "rateHotel"),
            CheckoutHour: Int(ns, "checkoutHour", DefaultCheckoutHour));
    }

    /// <summary>
    /// Merges this config back into an existing <c>extra</c> JSON string, preserving every other key,
    /// and returns the serialized result.
    /// </summary>
    public string WriteInto(string? extra)
    {
        JsonObject root;
        if (!string.IsNullOrWhiteSpace(extra) && JsonNode.Parse(extra) is JsonObject existing)
        {
            root = existing;
        }
        else
        {
            root = new JsonObject();
        }

        root[ExtraKey] = new JsonObject
        {
            ["rateMedical"] = RateMedical,
            ["rateIcu"] = RateIcu,
            ["rateHotel"] = RateHotel,
            ["checkoutHour"] = CheckoutHour,
        };

        return root.ToJsonString();
    }

    private static decimal Dec(JsonObject obj, string key) =>
        obj[key] is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : 0m;

    private static int Int(JsonObject obj, string key, int fallback) =>
        obj[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : fallback;
}
