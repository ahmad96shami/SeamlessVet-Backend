using System.Text.Json;
using System.Text.Json.Nodes;

namespace VetSystem.Application.Settings;

/// <summary>
/// The appointment-reminder tunable that lives inside <c>system_settings.extra</c>: how many minutes
/// <b>before</b> an appointment's <c>scheduled_at</c> the reminder fires. Stored under the
/// <c>"appointmentReminder"</c> key so the rest of the <c>extra</c> bag is preserved across writes.
/// Absent/blank config resolves to <see cref="Default"/> (60-minute lead). Appointments are intraday
/// datetimes, so the lead is in minutes (like <see cref="MedicationReminderSettings"/>).
/// </summary>
public sealed record AppointmentReminderSettings(int LeadMinutes)
{
    public const string ExtraKey = "appointmentReminder";

    public static readonly AppointmentReminderSettings Default = new(60);

    /// <summary>Reads the <c>"appointmentReminder"</c> object out of an <c>extra</c> JSON string.</summary>
    public static AppointmentReminderSettings FromExtra(string? extra)
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

        if (root?[ExtraKey] is not JsonObject ar)
        {
            return Default;
        }

        return new AppointmentReminderSettings(
            LeadMinutes: Int(ar, "leadMinutes", 60));
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
            ["leadMinutes"] = LeadMinutes,
        };

        return root.ToJsonString();
    }

    private static int Int(JsonObject obj, string key, int fallback) =>
        obj[key] is JsonValue v && v.TryGetValue<int>(out var i) ? i : fallback;
}
