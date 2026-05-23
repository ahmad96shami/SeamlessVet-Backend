// VetSystem API — composition root.
//
// Logging:   Serilog (file + Seq); enriched with MachineName/ThreadId/EnvironmentName.
// Auth:      JWT bearer for user tokens (M1) — token shape `{ sub, role, environment_id, perms }`.
//            PowerSync tokens use a separate RSA key exposed at /.well-known/jwks.json.
// Endpoints: Minimal APIs registered through `IEndpointModule` modules — never controllers.
// Filters:   Cross-cutting concerns (auth, FluentValidation, idempotency-key dedupe) attach to
//            route groups via `AddEndpointFilter`, not per-endpoint code.
// Errors:    Domain exceptions → `{ code, message, fieldErrors? }` via ExceptionHandlingMiddleware.

using System.Text;
using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using NSwag;
using NSwag.Generation.Processors.Security;
using Serilog;
using VetSystem.API.Endpoints;
using VetSystem.API.Endpoints.Sync;
using VetSystem.API.Filters;
using VetSystem.API.Identity;
using VetSystem.API.Middleware;
using VetSystem.Application;
using VetSystem.Application.Common;
using VetSystem.Infrastructure;
using VetSystem.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, services, configuration) =>
{
    configuration.ReadFrom.Configuration(context.Configuration).ReadFrom.Services(services);

    var seqUrl = context.Configuration["Seq:ServerUrl"];
    if (!string.IsNullOrWhiteSpace(seqUrl))
    {
        configuration.WriteTo.Seq(seqUrl);
    }
});

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<PowerSyncOptions>(builder.Configuration.GetSection(PowerSyncOptions.SectionName));
builder.Services.Configure<VetSystem.Infrastructure.Storage.R2Options>(
    builder.Configuration.GetSection(VetSystem.Infrastructure.Storage.R2Options.SectionName));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpCurrentUserAccessor>();
builder.Services.AddScoped<IRequestEnvironmentResolver, RequestEnvironmentResolver>();
builder.Services.AddSingleton<IPowerSyncTokenService, PowerSyncTokenService>();

// M1 — identity, RBAC, admin approval
builder.Services.AddSingleton<IPasswordHasher, VetSystem.Infrastructure.Identity.BCryptPasswordHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IRefreshTokenStore, VetSystem.Infrastructure.Identity.EfRefreshTokenStore>();
builder.Services.AddScoped<IPermissionResolver, VetSystem.Infrastructure.Identity.PermissionResolver>();
builder.Services.AddScoped<VetSystem.Application.Identity.INumberPrefixGenerator,
    VetSystem.Infrastructure.Identity.NumberPrefixGenerator>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<UserAdminService>();

// M2 — catalog + system settings
builder.Services.AddScoped<VetSystem.API.Catalog.ProductsAdminService>();
builder.Services.AddScoped<VetSystem.API.Catalog.ServicesAdminService>();
builder.Services.AddScoped<VetSystem.API.Settings.SystemSettingsAdminService>();

// M3 — customers, pets, ledgers
builder.Services.AddScoped<VetSystem.API.Customers.CustomersService>();
builder.Services.AddScoped<VetSystem.API.Pets.PetsService>();
builder.Services.AddScoped<VetSystem.Application.Ledgers.ILedgerService,
    VetSystem.Infrastructure.Ledgers.LedgerService>();

// M4 — inventory (warehouse + field), delta-only append-only movements.
// M11 — the publisher now fans events out to IDomainEventHandler<T>s (notification handlers) in a
// fresh scope per publish; handler registrations live with the SignalR/notification wiring below.
builder.Services.AddSingleton<IDomainEventPublisher,
    VetSystem.Infrastructure.Messaging.DispatchingDomainEventPublisher>();
builder.Services.AddSingleton<VetSystem.Application.Inventory.IMovementTranslator,
    VetSystem.Application.Inventory.MovementTranslator>();
builder.Services.AddScoped<VetSystem.Application.Inventory.IInventoryService,
    VetSystem.Infrastructure.Inventory.InventoryService>();
builder.Services.AddScoped<VetSystem.Application.Inventory.IInventoryScanService,
    VetSystem.Infrastructure.Inventory.InventoryScanService>();
builder.Services.AddScoped<VetSystem.API.Inventory.InventoryAdminService>();

// M5 — visits & medical records
builder.Services.AddScoped<VetSystem.Application.Visits.IVisitNumberValidator,
    VetSystem.Infrastructure.Visits.VisitNumberValidator>();
builder.Services.AddScoped<VetSystem.API.Visits.VisitsService>();
builder.Services.AddScoped<VetSystem.API.Procedures.ProceduresService>();
builder.Services.AddScoped<VetSystem.API.Prescriptions.PrescriptionsService>();
builder.Services.AddScoped<VetSystem.API.DailyFollowUps.DailyFollowUpsService>();
builder.Services.AddScoped<VetSystem.API.Vaccinations.VaccinationsService>();
builder.Services.AddSingleton<VetSystem.Application.Storage.ISignedUrlService,
    VetSystem.Infrastructure.Storage.R2SignedUrlService>();
builder.Services.AddScoped<VetSystem.API.Attachments.AttachmentsService>();
builder.Services.AddScoped<VetSystem.API.Pets.PetTimelineService>();

// M6 — appointments (scheduling + conflict detection)
builder.Services.AddScoped<VetSystem.Application.Appointments.IAppointmentService,
    VetSystem.Infrastructure.Appointments.AppointmentService>();
builder.Services.AddScoped<VetSystem.API.Appointments.AppointmentsService>();

// M7 — financial (POS, invoices, receipt vouchers)
builder.Services.AddScoped<VetSystem.Application.Financial.IInvoiceNumberValidator,
    VetSystem.Infrastructure.Financial.InvoiceNumberValidator>();
builder.Services.AddScoped<VetSystem.API.Financial.InvoicesService>();
builder.Services.AddScoped<VetSystem.API.Financial.ReceiptVouchersService>();

// M8 — contracts & batches (lifecycle, per-medication pricing, supervision cycles)
builder.Services.AddScoped<VetSystem.Application.Contracts.IContractLifecycleService,
    VetSystem.Infrastructure.Contracts.ContractLifecycleService>();
builder.Services.AddScoped<VetSystem.Application.Contracts.IPricingService,
    VetSystem.Infrastructure.Contracts.PricingService>();
builder.Services.AddScoped<VetSystem.API.Contracts.ContractsService>();
builder.Services.AddScoped<VetSystem.API.Contracts.ContractMedicationPricesService>();
builder.Services.AddScoped<VetSystem.API.Contracts.BatchesService>();

// M9 — doctor entitlements & settlement lock. The fee-model + System A/B calculators and the toggle
// resolver are pure (no state), registered as singletons; the four IExamFeeCalculator impls are
// enumerated by the factory to dispatch on supervision_fee_model.
builder.Services.AddSingleton<VetSystem.Application.Entitlements.IExamFeeCalculator,
    VetSystem.Application.Entitlements.FixedAmountExamFeeCalculator>();
builder.Services.AddSingleton<VetSystem.Application.Entitlements.IExamFeeCalculator,
    VetSystem.Application.Entitlements.PercentOfInvoiceExamFeeCalculator>();
builder.Services.AddSingleton<VetSystem.Application.Entitlements.IExamFeeCalculator,
    VetSystem.Application.Entitlements.PerBirdExamFeeCalculator>();
builder.Services.AddSingleton<VetSystem.Application.Entitlements.IExamFeeCalculator,
    VetSystem.Application.Entitlements.PerBatchFixedExamFeeCalculator>();
builder.Services.AddSingleton<VetSystem.Application.Entitlements.IExamFeeCalculatorFactory,
    VetSystem.Application.Entitlements.ExamFeeCalculatorFactory>();
builder.Services.AddSingleton<VetSystem.Application.Entitlements.IEntitlementToggleResolver,
    VetSystem.Application.Entitlements.EntitlementToggleResolver>();
builder.Services.AddSingleton<VetSystem.Application.Entitlements.ISystemADrugProfitCalculator,
    VetSystem.Application.Entitlements.SystemADrugProfitCalculator>();
builder.Services.AddSingleton<VetSystem.Application.Entitlements.ISystemBDirectFeeCalculator,
    VetSystem.Application.Entitlements.SystemBDirectFeeCalculator>();
builder.Services.AddSingleton<VetSystem.Application.Entitlements.ISettlementLockGuard,
    VetSystem.Application.Entitlements.SettlementLockGuard>();
builder.Services.AddScoped<VetSystem.Application.Entitlements.IEntitlementService,
    VetSystem.Infrastructure.Entitlements.EntitlementService>();
builder.Services.AddScoped<VetSystem.API.Entitlements.EntitlementSettlementService>();

// M10 — multi-environment & partnership. The share-limit validator is pure (no state), a singleton
// like the entitlement calculators; the profit-distribution service reads the DB, so it is scoped.
builder.Services.AddSingleton<VetSystem.Application.Partnership.IPartnershipValidator,
    VetSystem.Application.Partnership.PartnershipShareLimitValidator>();
builder.Services.AddScoped<VetSystem.Application.Partnership.IProfitDistributionService,
    VetSystem.Infrastructure.Partnership.ProfitDistributionService>();
builder.Services.AddScoped<VetSystem.API.Partnership.PartnersService>();
builder.Services.AddScoped<VetSystem.API.Partnership.PartnershipSharesService>();

// M11 — SignalR realtime + notification dispatch. The dispatcher persists a notifications row per
// recipient and pushes over the hub; recipient resolution + the in-app feed are scoped DB reads.
// Domain-event handlers (which turn negative-stock/account-ready/entitlement-approved into
// notifications) and the Hangfire jobs are registered further below.
builder.Services.AddSignalR();
builder.Services.AddScoped<VetSystem.Application.Notifications.INotificationDispatcher,
    VetSystem.API.Notifications.NotificationDispatcher>();
builder.Services.AddScoped<VetSystem.API.Notifications.NotificationRecipientResolver>();
builder.Services.AddScoped<VetSystem.API.Notifications.NotificationsService>();

// Domain-event → notification handlers (dispatched in a fresh scope by DispatchingDomainEventPublisher).
builder.Services.AddScoped<IDomainEventHandler<VetSystem.Domain.Events.NegativeStockAttemptedEvent>,
    VetSystem.API.Notifications.Handlers.NegativeStockAttemptedHandler>();
builder.Services.AddScoped<IDomainEventHandler<VetSystem.Domain.Events.AccountReadyForSettlementEvent>,
    VetSystem.API.Notifications.Handlers.AccountReadyForSettlementHandler>();
builder.Services.AddScoped<IDomainEventHandler<VetSystem.Domain.Events.EntitlementApprovedEvent>,
    VetSystem.API.Notifications.Handlers.EntitlementApprovedHandler>();

// Hangfire job classes — registered as services always so the recurring registration (real runs) and
// the integration tests (forced clock, invoked directly) both resolve them from DI.
builder.Services.AddScoped<VetSystem.API.Jobs.VaccinationRemindersJob>();
builder.Services.AddScoped<VetSystem.API.Jobs.LowStockAlertsJob>();
builder.Services.AddScoped<VetSystem.API.Jobs.ExpirationWarningsJob>();
builder.Services.AddScoped<VetSystem.API.Jobs.ScheduledReportDeliveryJob>();

// M12 — reports (PRD §7.9). Each report is a read-only, environment-scoped, offset-paged service over
// ApplicationDbContext; the /reports endpoint group is gated on PermissionKey.ReportsRead.
builder.Services.AddScoped<VetSystem.API.Reports.DoctorIncomeReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.ClinicProfitsReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.ProfitPerBatchReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.SalesReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.ProfitAndLossReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.InventoryMovementReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.FieldDoctorVisitsReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.UpcomingVaccinationsReportService>();

builder.Services.AddScoped<ISyncDispatcher, SyncDispatcher>();
builder.Services.AddScoped<ISyncTableHandler, SyncTestHandler>();
builder.Services.AddScoped<ISyncTableHandler, CustomersSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, PetsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, LedgersSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, LedgerEntriesSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, InventoryMovementsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, StockItemsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, VisitsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, ProceduresSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, PrescriptionsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, DailyFollowUpsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, VaccinationsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, AttachmentsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, AppointmentsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, InvoicesSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, InvoiceItemsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, PaymentsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, ReceiptVouchersSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, ContractsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, ContractMedicationPricesSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, BatchesSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, DoctorEntitlementsSyncHandler>();
builder.Services.AddScoped<IdempotencyKeyFilter>();

builder.Services.AddMemoryCache();

// M11 — Hangfire (PostgreSQL storage, single in-process worker per TECH_STACK). Disabled under the
// "Test" environment so integration tests never touch Hangfire's global JobStorage/schema; the job
// classes are still registered as plain services (below) so tests can drive them directly with a
// forced clock. The dashboard is mounted after build, gated to admins.
var hangfireEnabled = !builder.Environment.IsEnvironment("Test");
if (hangfireEnabled)
{
    var hangfireConnection = builder.Configuration.GetConnectionString(
        VetSystem.Infrastructure.DependencyInjection.PostgresConnectionStringName);

    builder.Services.AddHangfire(cfg => cfg
        .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UsePostgreSqlStorage(o => o.UseNpgsqlConnection(hangfireConnection)));

    builder.Services.AddHangfireServer(o => o.WorkerCount = 1);
}

builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddMappingAndValidators(typeof(Program).Assembly);
builder.Services.AddScoped<DataSeeder>();
builder.Services.AddEndpointModules(typeof(Program).Assembly);

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),
        };

        // SignalR clients send the access token on the query string — the WebSocket/SSE handshake
        // can't carry an Authorization header. Honor it for hub requests only (M11 task 5).
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs/notifications"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddProblemDetails();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddOpenApiDocument(settings =>
{
    settings.Title = "VetSystem API";
    settings.DocumentName = "v1";
    settings.Version = "1.0.0";
    settings.AddSecurity("Bearer", [],
        new OpenApiSecurityScheme
        {
            Type = OpenApiSecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT",
            Description = "JWT Bearer token (Authorization: Bearer …).",
        });
    settings.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor("Bearer"));
});

builder.Services.AddRouting(options => options.LowercaseUrls = true);

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseOpenApi(settings => settings.Path = "/swagger/{documentName}/swagger.json");
app.UseSwaggerUi(settings =>
{
    settings.Path = "/swagger";
    settings.DocumentPath = "/swagger/v1/swagger.json";
});

app.MapEndpointModules();
app.MapHub<VetSystem.API.Notifications.NotificationsHub>("/hubs/notifications");

if (hangfireEnabled)
{
    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        Authorization = [new VetSystem.API.Jobs.AdminOnlyDashboardAuthorizationFilter()],
    });

    // Recurring scans (UTC cron). The alert jobs run each morning; report delivery is weekly. Jobs
    // iterate every environment internally, so one schedule covers the whole instance.
    RecurringJob.AddOrUpdate<VetSystem.API.Jobs.VaccinationRemindersJob>(
        "vaccination-reminders", j => j.RunAsync(CancellationToken.None), "0 7 * * *");
    RecurringJob.AddOrUpdate<VetSystem.API.Jobs.LowStockAlertsJob>(
        "low-stock-alerts", j => j.RunAsync(CancellationToken.None), "0 7 * * *");
    RecurringJob.AddOrUpdate<VetSystem.API.Jobs.ExpirationWarningsJob>(
        "expiration-warnings", j => j.RunAsync(CancellationToken.None), "0 7 * * *");
    RecurringJob.AddOrUpdate<VetSystem.API.Jobs.ScheduledReportDeliveryJob>(
        "scheduled-report-delivery", j => j.RunAsync(CancellationToken.None), "0 7 * * 1");
}

if (args.Contains("--seed") || args.Contains("--force-seed"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync(force: args.Contains("--force-seed"));
    return;
}

await app.RunAsync();

/// <summary>Exposed as a partial class so WebApplicationFactory can target it for integration tests.</summary>
public partial class Program { }
