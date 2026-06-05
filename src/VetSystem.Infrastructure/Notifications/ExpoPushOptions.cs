namespace VetSystem.Infrastructure.Notifications;

/// <summary>
/// M21 — Expo Push Service settings. Like <see cref="Storage.R2Options"/> these bind from the
/// <c>ExpoPush</c> section (user-secrets in dev, <c>ExpoPush__*</c> env vars in prod).
/// </summary>
public sealed class ExpoPushOptions
{
    public const string SectionName = "ExpoPush";

    /// <summary>Master switch — when false the push worker drops jobs without calling out.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Optional bearer for Expo's "Enhanced Push Security"; the public push API works without one,
    /// so an empty value simply sends unauthenticated.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <summary>Overridable for tests (fake handler asserts against relative batching only).</summary>
    public string BaseUrl { get; set; } = "https://exp.host/--/api/v2/push/send";
}
