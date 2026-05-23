namespace VetSystem.Application.Entitlements;

/// <summary>
/// Resolves whether the doctor-entitlement system is effectively enabled for a batch
/// (SCHEMA "Key invariants" #4, PRD §7.5): the per-batch override wins when set, else the
/// environment-wide <c>system_settings.entitlement_enabled_global</c> applies. When disabled,
/// all profit goes to the clinic — the doctor's computed amount is 0.
///
/// <para>Pure (the caller supplies the global default it read from <c>system_settings</c>), keeping
/// the Application layer free of EF Core and the rule trivially unit-testable.</para>
/// </summary>
public interface IEntitlementToggleResolver
{
    bool IsEnabled(bool? perBatchOverride, bool globalDefault);
}

/// <inheritdoc cref="IEntitlementToggleResolver"/>
public sealed class EntitlementToggleResolver : IEntitlementToggleResolver
{
    public bool IsEnabled(bool? perBatchOverride, bool globalDefault) => perBatchOverride ?? globalDefault;
}
