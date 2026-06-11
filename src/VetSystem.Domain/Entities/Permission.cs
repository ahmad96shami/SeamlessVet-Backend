using VetSystem.Domain.Common;

namespace VetSystem.Domain.Entities;

/// <summary>SCHEMA §1 — fine-grained permission key (dot-notation). Globally unique on <c>key</c>.</summary>
public sealed class Permission : Entity
{
    public string Key { get; set; } = string.Empty;

    public string? Description { get; set; }
}

/// <summary>
/// Canonical permission catalog. Each milestone appends the keys it owns; M1 ships the structural
/// set the admin-approval flow needs plus the cross-milestone gates referenced by later tasks.
/// </summary>
public static class PermissionKey
{
    // Identity & user management (M1)
    public const string UsersApprove = "users.approve";
    public const string UsersManage = "users.manage";
    public const string UsersPermissionsOverride = "users.permissions.override";

    // Settings & catalog (M2)
    public const string SettingsWrite = "settings.write";
    public const string CatalogWrite = "catalog.write";

    // Customers, pets, ledgers (M3)
    public const string CustomersWrite = "customers.write";

    // Visits & medical records (M5)
    public const string MedicalWrite = "medical.write";

    // Appointments (M6)
    public const string AppointmentsWrite = "appointments.write";

    // Contracts & batches (M8)
    public const string ContractsWrite = "contracts.write";
    public const string ContractsActivate = "contracts.activate";

    // Financial (M7)
    public const string InvoicesWrite = "invoices.write";
    public const string InvoicesRefund = "invoices.refund";
    public const string InvoicesVoid = "invoices.void";

    // Inventory (M4)
    public const string InventoryAdjust = "inventory.adjust";

    // Multi-environment & partnership (M10)
    public const string PartnershipManage = "partnership.manage";

    // Reporting (M12)
    public const string ReportsRead = "reports.read";

    // Suppliers, purchase invoices & supplier payments (M19)
    public const string SuppliersWrite = "suppliers.write";

    // Doctor-partners & their entitlement-payable ledger (M30)
    public const string DoctorPartnersManage = "doctor_partners.manage";
    public const string DoctorPartnersPay = "doctor_partners.pay";

    public static readonly IReadOnlyCollection<string> All =
    [
        UsersApprove, UsersManage, UsersPermissionsOverride,
        SettingsWrite, CatalogWrite,
        CustomersWrite,
        MedicalWrite,
        AppointmentsWrite,
        ContractsWrite, ContractsActivate,
        InvoicesWrite, InvoicesRefund, InvoicesVoid,
        InventoryAdjust,
        PartnershipManage,
        ReportsRead,
        SuppliersWrite,
        DoctorPartnersManage, DoctorPartnersPay,
    ];
}
