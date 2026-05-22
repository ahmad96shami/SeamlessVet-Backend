namespace VetSystem.Domain.Entities;

/// <summary>
/// SCHEMA §9 — server-side dedupe for every mutating write across <c>/sync/*</c> and admin
/// surfaces. A replayed offline mutation matches on <c>(environment_id, key)</c> and returns
/// the original <c>result_ref</c> without applying again.
/// </summary>
public sealed class IdempotencyKey
{
    public string Key { get; set; } = string.Empty;

    public Guid EnvironmentId { get; set; }

    public string EntityType { get; set; } = string.Empty;

    public Guid? ResultRef { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
