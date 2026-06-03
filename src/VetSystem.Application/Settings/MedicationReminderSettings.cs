using System.Text.Json;
using System.Text.Json.Nodes;

namespace VetSystem.Application.Settings;

/// <summary>
/// M18 — the medication-reminder tunable that lives inside <c>system_settings.extra</c> rather than as
/// a first-class column: the default lead-time (minutes before a dose) applied when a prescription's
/// own <c>lead_minutes</c> is null. Stored under the <c>"medicationReminder"</c> key so the rest of the
/// <c>extra</c> bag is preserved across writes. Absent/blank config resolves to <see cref="Default"/>
/// (0-minute lead — remind exactly at the dose) — a safe default.
/// </summary>
public sealed record MedicationReminderSettings(int DefaultLeadMinutes)
{
    public const string ExtraKey = "medicationReminder";

    public static readonly MedicationReminderSettings Default = new(0);

    /// <summary>Reads the <c>"medicationReminder"</c> object out of an <c>extra</c> JSON string.</summary>
    public static MedicationReminderSettings FromExtra(string? extra)
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

        if (root?[ExtraKey] is not JsonObject mr)
        {
            return Default;
        }

        return new MedicationReminderSettings(
            DefaultLeadMinutes: Int(mr, "defaultLeadMinutes", 0));
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
            ["defaultLeadMinutes"] = DefaultLeadMinutes,
        };

        return root.ToJsonString();
    }

    private static int Int(JsonObject obj, string key, int fallback) =>
        obj[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : fallback;
}
