using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §9 — one persisted in-app notification for a single recipient user. The realtime push
/// (SignalR) and this row are written together by the dispatcher so the feed at <c>GET /notifications</c>
/// always reflects what was pushed. External channels (SMS/WhatsApp/email) are out of scope here —
/// they are dispatched by Hangfire jobs and not stored as rows (PRD §9). <see cref="Payload"/> is a
/// serialized JSON object (<c>jsonb</c>) carrying the structured data the client localizes/links from.
/// </summary>
public sealed class Notification : Entity
{
    public Guid UserId { get; set; }

    public string Type { get; set; } = string.Empty;

    public string? Title { get; set; }

    public string? Body { get; set; }

    /// <summary>Structured data as a JSON document string, stored in a <c>jsonb</c> column.</summary>
    public string? Payload { get; set; }

    public DateTimeOffset? ReadAt { get; set; }
}

/// <summary>SCHEMA <c>notification_type</c> enum (PRD §9). The persisted <c>type</c> column draws from this set.</summary>
public static class NotificationType
{
    public const string AppointmentReminder = "appointment_reminder";
    public const string FollowUpDue = "follow_up_due";
    public const string VaccinationDue = "vaccination_due";
    public const string MedicationDue = "medication_due";
    public const string LowStock = "low_stock";
    public const string ExpiryWarning = "expiry_warning";
    public const string RegistrationRequest = "registration_request";
    public const string NegativeStock = "negative_stock";
    public const string AccountReadyForSettlement = "account_ready_for_settlement";

    /// <summary>M30 — a batch settlement credited a doctor's entitlement to their partner balance.</summary>
    public const string EntitlementCredited = "entitlement_credited";
    public const string ReportDelivery = "report_delivery";

    /// <summary>A visit was created by someone else (e.g. a receptionist) and assigned to this doctor.</summary>
    public const string VisitAssigned = "visit_assigned";

    public static readonly IReadOnlyCollection<string> All =
    [
        AppointmentReminder, FollowUpDue, VaccinationDue, MedicationDue, LowStock, ExpiryWarning,
        RegistrationRequest, NegativeStock, AccountReadyForSettlement, EntitlementCredited, ReportDelivery,
        VisitAssigned,
    ];
}
