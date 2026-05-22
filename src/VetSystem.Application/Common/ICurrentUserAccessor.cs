namespace VetSystem.Application.Common;

/// <summary>
/// Surfaces the authenticated principal to layers below the API: drives the global
/// environment-scoped EF query filter, idempotency-key lookup, and permission checks.
/// HTTP-aware implementation lives in <c>VetSystem.API</c>; the contract stays here so
/// Application and Infrastructure never reference ASP.NET Core directly.
/// </summary>
public interface ICurrentUserAccessor
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    /// <summary>
    /// Tenant key from the JWT's <c>environment_id</c> claim. The global EF query filter
    /// reads this on every materialization; <c>IgnoreQueryFilters()</c> is required for
    /// cross-environment admin queries.
    /// </summary>
    Guid? EnvironmentId { get; }

    string? Role { get; }

    IReadOnlyCollection<string> Permissions { get; }
}
