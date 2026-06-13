namespace VetSystem.Application.Provisioning;

/// <summary>Inputs for standing up a new tenant center + its first administrator.</summary>
public sealed record ProvisionEnvironmentRequest(
    string CenterName,
    string Code,
    string Mode,
    string AdminFullName,
    string AdminPhone,
    string AdminPassword,
    string? AdminEmail);

public sealed record ProvisionEnvironmentResult(Guid EnvironmentId, Guid AdminUserId, string Code);

/// <summary>
/// The one transactional code path that stands up a tenant <c>environment</c> + all its per-env
/// identity/settings rows + a first active admin. Shared by <c>DataSeeder</c> (bootstrap) and the
/// platform console (M35). <see cref="SeedStructureAsync"/> is the idempotent per-env seed
/// (roles/permissions/defaults/settings/warehouse/system-services), reused for redeploy top-ups.
/// </summary>
public interface IEnvironmentProvisioningService
{
    /// <summary>
    /// Atomically creates the environment, seeds its structure, and creates the first admin user.
    /// <paramref name="environmentId"/> is <c>null</c> for a console-provisioned center (a Guid v7
    /// is minted) or a fixed id for the bootstrap env.
    /// </summary>
    Task<ProvisionEnvironmentResult> ProvisionAsync(
        ProvisionEnvironmentRequest request,
        Guid? environmentId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Idempotently seeds the per-env structure (roles, permissions, role-permission defaults,
    /// system settings, central warehouse, system services). Safe to re-run — only missing rows are
    /// added — so a redeploy that adds new permission keys backfills existing centers.
    /// </summary>
    Task SeedStructureAsync(Guid environmentId, CancellationToken cancellationToken);
}
