using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>
/// Throw-away M0 entity exercised by <c>/sync/test</c> — verifies the auth, validation,
/// idempotency, and environment-scoping pipeline end-to-end before any real domain lands.
/// Safe to delete once a real domain table goes through the same plumbing.
/// </summary>
public sealed class SyncTestRecord : Entity
{
    public string Label { get; set; } = string.Empty;
}
