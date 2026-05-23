using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using VetSystem.Application.Common;
using VetSystem.Domain.Common;
using VetSystem.Domain.Entities;
using DomainEnvironment = VetSystem.Domain.Entities.Environment;

namespace VetSystem.Infrastructure.Persistence;

public sealed class ApplicationDbContext : DbContext
{
    private readonly ICurrentUserAccessor _currentUser;

    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options, ICurrentUserAccessor currentUser)
        : base(options)
    {
        _currentUser = currentUser;
    }

    public DbSet<DomainEnvironment> Environments => Set<DomainEnvironment>();
    public DbSet<IdempotencyKey> IdempotencyKeys => Set<IdempotencyKey>();
    public DbSet<SyncTestRecord> SyncTestRecords => Set<SyncTestRecord>();

    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RegistrationRequest> RegistrationRequests => Set<RegistrationRequest>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Service> Services => Set<Service>();
    public DbSet<SystemSettings> SystemSettings => Set<SystemSettings>();

    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Pet> Pets => Set<Pet>();
    public DbSet<Ledger> Ledgers => Set<Ledger>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();
    public DbSet<FieldInventory> FieldInventories => Set<FieldInventory>();
    public DbSet<StockItem> StockItems => Set<StockItem>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();

    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<Procedure> Procedures => Set<Procedure>();
    public DbSet<Prescription> Prescriptions => Set<Prescription>();
    public DbSet<DailyFollowUp> DailyFollowUps => Set<DailyFollowUp>();
    public DbSet<Vaccination> Vaccinations => Set<Vaccination>();
    public DbSet<Attachment> Attachments => Set<Attachment>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<ReceiptVoucher> ReceiptVouchers => Set<ReceiptVoucher>();

    public DbSet<Contract> Contracts => Set<Contract>();
    public DbSet<ContractMedicationPrice> ContractMedicationPrices => Set<ContractMedicationPrice>();
    public DbSet<Batch> Batches => Set<Batch>();

    public DbSet<DoctorEntitlement> DoctorEntitlements => Set<DoctorEntitlement>();

    /// <summary>
    /// Read by the global env-scoped query filter on every materialization. Returns
    /// <see cref="Guid.Empty"/> when unauthenticated, which deliberately matches no row.
    /// Use <c>IgnoreQueryFilters()</c> for admin or boot-strap queries that must cross environments.
    /// </summary>
    public Guid CurrentEnvironmentId => _currentUser.EnvironmentId ?? Guid.Empty;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        ApplyEntityConventions(modelBuilder);
    }

    private void ApplyEntityConventions(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            var clrType = entityType.ClrType;
            if (!clrType.IsAssignableTo(typeof(Entity)))
            {
                continue;
            }

            ConfigureEntityColumns(modelBuilder, clrType);
            ConfigureEntityIndexes(modelBuilder, clrType);
            ApplyEnvironmentScopedSoftDeleteFilter(modelBuilder, clrType);
        }
    }

    private static void ConfigureEntityColumns(ModelBuilder modelBuilder, Type clrType)
    {
        modelBuilder.Entity(clrType, builder =>
        {
            builder.Property(nameof(Entity.Id)).HasColumnName("id");
            builder.Property(nameof(Entity.EnvironmentId)).HasColumnName("environment_id");
            builder.Property(nameof(Entity.CreatedAt)).HasColumnName("created_at");
            builder.Property(nameof(Entity.UpdatedAt)).HasColumnName("updated_at");
            builder.Property(nameof(Entity.DeletedAt)).HasColumnName("deleted_at");

            builder.Property(nameof(Entity.Xmin))
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();
        });
    }

    private static void ConfigureEntityIndexes(ModelBuilder modelBuilder, Type clrType)
    {
        modelBuilder.Entity(clrType, builder =>
        {
            // baseline indexes per SCHEMA "Indexing baseline"
            builder.HasIndex(nameof(Entity.EnvironmentId), nameof(Entity.UpdatedAt));
            builder.HasIndex(nameof(Entity.EnvironmentId), nameof(Entity.DeletedAt));
        });
    }

    private void ApplyEnvironmentScopedSoftDeleteFilter(ModelBuilder modelBuilder, Type clrType)
    {
        // (e) => e.DeletedAt == null && e.EnvironmentId == this.CurrentEnvironmentId
        var parameter = Expression.Parameter(clrType, "e");
        var deletedAtProp = Expression.Property(parameter, nameof(Entity.DeletedAt));
        var deletedAtNull = Expression.Equal(deletedAtProp, Expression.Constant(null, typeof(DateTimeOffset?)));

        var envProp = Expression.Property(parameter, nameof(Entity.EnvironmentId));
        var contextEnv = Expression.Property(
            Expression.Constant(this),
            typeof(ApplicationDbContext).GetProperty(nameof(CurrentEnvironmentId))!);
        var envMatch = Expression.Equal(envProp, contextEnv);

        var body = Expression.AndAlso(deletedAtNull, envMatch);
        var lambda = Expression.Lambda(body, parameter);

        modelBuilder.Entity(clrType).HasQueryFilter(lambda);
    }
}
