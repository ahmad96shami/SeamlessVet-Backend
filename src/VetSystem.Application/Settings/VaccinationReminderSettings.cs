using System.Text.Json;
using System.Text.Json.Nodes;

namespace VetSystem.Application.Settings;

/// <summary>
/// The vaccination-reminder tunable that lives inside <c>system_settings.extra</c>: how many days
/// <b>before</b> a vaccination's <c>next_due_date</c> the reminder fires. Stored under the
/// <c>"vaccinationReminder"</c> key so the rest of the <c>extra</c> bag is preserved across writes.
/// Absent/blank config resolves to <see cref="Default"/> (0-day lead — remind on the due date itself).
/// The vaccination due date is date-only and the scan runs daily, so the lead is in days (mirrors the
/// minute-granular <see cref="MedicationReminderSettings"/>).
/// </summary>
public sealed record VaccinationReminderSettings(int LeadDays)
{
    public const string ExtraKey = "vaccinationReminder";

    public static readonly VaccinationReminderSettings Default = new(0);

    /// <summary>Reads the <c>"vaccinationReminder"</c> object out of an <c>extra</c> JSON string.</summary>
    public static VaccinationReminderSettings FromExtra(string? extra)
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

        if (root?[ExtraKey] is not JsonObject vr)
        {
            return Default;
        }

        return new VaccinationReminderSettings(
            LeadDays: Int(vr, "leadDays", 0));
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
            ["leadDays"] = LeadDays,
        };

        return root.ToJsonString();
    }

    private static int Int(JsonObject obj, string key, int fallback) =>
        obj[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : fallback;
}
