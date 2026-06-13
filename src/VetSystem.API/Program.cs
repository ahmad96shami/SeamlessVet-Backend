// VetSystem API — composition root.
//
// Logging:   Serilog (file + Seq); enriched with MachineName/ThreadId/EnvironmentName.
// Auth:      JWT bearer for user tokens (M1) — token shape `{ sub, role, environment_id, perms }`.
//            PowerSync tokens use a separate RSA key exposed at /.well-known/jwks.json.
// Endpoints: Minimal APIs registered through `IEndpointModule` modules — never controllers.
// Filters:   Cross-cutting concerns (auth, FluentValidation, idempotency-key dedupe) attach to
//            route groups via `AddEndpointFilter`, not per-endpoint code.
// Errors:    Domain exceptions → `{ code, message, fieldErrors? }` via ExceptionHandlingMiddleware.

using System.Globalization;
using System.Text;
using System.Threading.RateLimiting;
using FluentValidation;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using NSwag;
using NSwag.Generation.Processors.Security;
using Serilog;
using Serilog.Events;
using Serilog.Filters;
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

    // Sentry (M13 task 8): forward logged errors to Sentry as events and INFO+ as breadcrumbs. The SDK
    // itself is initialized by UseSentry (below) — InitializeSdk=false here so this sink only feeds the
    // already-running hub. No DSN ⇒ the hub is disabled and this is a no-op. Routing capture through
    // Serilog (rather than Sentry's ASP.NET middleware) is deliberate: ExceptionHandlingMiddleware
    // handles every request exception so it never propagates to outer middleware, but it logs 500s via
    // LogError — and this also captures Hangfire job failures, which aren't HTTP at all.
    if (!string.IsNullOrWhiteSpace(context.Configuration["Sentry:Dsn"]))
    {
        // Scope the Sentry sink through a sub-logger so one source can be dropped from it without
        // touching file/Seq: UseSerilogRequestLogging logs every 5xx at Error, which would otherwise
        // reach Sentry as a second, stack-less issue for the same request, double-counting each 500.
        // Exclude only that source's Error events — the ExceptionHandlingMiddleware event already
        // carries the exception + HTTP request context (via UseSentry), and the source's Information
        // request logs still feed Sentry breadcrumbs.
        var fromRequestLogging = Matching.FromSource("Serilog.AspNetCore.RequestLoggingMiddleware");
        configuration.WriteTo.Logger(sentry => sentry
            .Filter.ByExcluding(e => e.Level == LogEventLevel.Error && fromRequestLogging(e))
            .WriteTo.Sentry(o =>
            {
                o.InitializeSdk = false;
                o.MinimumEventLevel = LogEventLevel.Error;
                o.MinimumBreadcrumbLevel = LogEventLevel.Information;
            }));
    }
});

// Sentry crash/error reporting (M13 task 8). Config-driven and OFF until a DSN is supplied (user-secrets
// in dev, env var `Sentry__Dsn` in prod — never commit it). Environment + release are tagged so issues
// group per deploy; release falls back to the assembly's informational version when unset. PII is NOT
// sent (medical/financial data) and request bodies aren't captured. UseSentry owns SDK init + HTTP
// request enrichment + tracing; the actual error capture flows through the Serilog sink wired above.
// See vet-backend/CLAUDE.md "Operations".
var sentryDsn = builder.Configuration["Sentry:Dsn"];
if (!string.IsNullOrWhiteSpace(sentryDsn))
{
    builder.WebHost.UseSentry(o =>
    {
        o.Dsn = sentryDsn;
        var sentryEnv = builder.Configuration["Sentry:Environment"];
        o.Environment = string.IsNullOrWhiteSpace(sentryEnv) ? builder.Environment.EnvironmentName : sentryEnv;
        var release = builder.Configuration["Sentry:Release"];
        o.Release = string.IsNullOrWhiteSpace(release) ? null : release; // null ⇒ auto-detect from assembly
        o.SendDefaultPii = false;
        o.AttachStacktrace = true;
        o.TracesSampleRate = builder.Configuration.GetValue<double?>("Sentry:TracesSampleRate") ?? 0.0;
    });
}

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.Configure<PowerSyncOptions>(builder.Configuration.GetSection(PowerSyncOptions.SectionName));
builder.Services.Configure<VetSystem.Infrastructure.Storage.R2Options>(
    builder.Configuration.GetSection(VetSystem.Infrastructure.Storage.R2Options.SectionName));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUserAccessor, HttpCurrentUserAccessor>();
builder.Services.AddSingleton<IPowerSyncTokenService, PowerSyncTokenService>();

// M1 — identity, RBAC, admin approval
builder.Services.AddSingleton<IPasswordHasher, VetSystem.Infrastructure.Identity.BCryptPasswordHasher>();
builder.Services.AddSingleton<IRefreshTokenHasher, VetSystem.Infrastructure.Identity.Sha256RefreshTokenHasher>();
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();
builder.Services.AddScoped<IRefreshTokenStore, VetSystem.Infrastructure.Identity.EfRefreshTokenStore>();
builder.Services.AddScoped<IPermissionResolver, VetSystem.Infrastructure.Identity.PermissionResolver>();
// M32 — tenant lifecycle: cached env-status lookup behind the live-suspension middleware.
builder.Services.AddScoped<VetSystem.Application.Identity.IEnvironmentStatusProvider,
    VetSystem.Infrastructure.Identity.EnvironmentStatusProvider>();
// M33 — reusable environment provisioning (shared by DataSeeder bootstrap + the platform console).
builder.Services.AddScoped<VetSystem.Application.Provisioning.IEnvironmentProvisioningService,
    VetSystem.Infrastructure.Provisioning.EnvironmentProvisioningService>();
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
// M16/M17 — shared per-farm charge routing (invoices, night-stay + checkup-fee charges).
builder.Services.AddScoped<VetSystem.Application.Ledgers.IOwnerLedgerResolver,
    VetSystem.Infrastructure.Ledgers.OwnerLedgerResolver>();

// M15 — farms (attached to customers like pets; inherit the customer's doctor scope).
builder.Services.AddScoped<VetSystem.API.Farms.FarmsService>();

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
builder.Services.AddScoped<VetSystem.API.Inventory.InventoryReadService>();
builder.Services.AddScoped<VetSystem.API.Doctors.DoctorsReadService>();

// M5 — visits & medical records
builder.Services.AddScoped<VetSystem.Application.Visits.IVisitNumberValidator,
    VetSystem.Infrastructure.Visits.VisitNumberValidator>();
builder.Services.AddScoped<VetSystem.Application.Visits.IVisitNumberGenerator,
    VetSystem.Infrastructure.Visits.VisitNumberGenerator>();
builder.Services.AddScoped<VetSystem.API.Visits.VisitsService>();
builder.Services.AddScoped<VetSystem.API.Procedures.ProceduresService>();
builder.Services.AddScoped<VetSystem.API.Prescriptions.PrescriptionsService>();
builder.Services.AddScoped<VetSystem.API.DailyFollowUps.DailyFollowUpsService>();
builder.Services.AddScoped<VetSystem.API.NightStays.NightStaysService>(); // M17 — night stays (مبيت)
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

// M19 — suppliers, purchase invoices & supplier payments (accounts payable).
builder.Services.AddScoped<VetSystem.Application.SupplierLedgers.ISupplierLedgerService,
    VetSystem.Infrastructure.SupplierLedgers.SupplierLedgerService>();
builder.Services.AddScoped<VetSystem.API.Suppliers.SuppliersService>();
builder.Services.AddScoped<VetSystem.API.Suppliers.PurchaseInvoicesService>();
builder.Services.AddScoped<VetSystem.API.Suppliers.SupplierPaymentsService>();

// M30 — doctor-partners (entitlement-earning field doctors) + their AP ledger.
builder.Services.AddScoped<VetSystem.Application.DoctorPartnerLedgers.IDoctorPartnerLedgerService,
    VetSystem.Infrastructure.DoctorPartnerLedgers.DoctorPartnerLedgerService>();
builder.Services.AddScoped<VetSystem.API.DoctorPartners.DoctorPartnersService>();
builder.Services.AddScoped<VetSystem.API.DoctorPartners.DoctorPartnerPaymentsService>();

// M31 — employees + their HR salary/loan ledger (the AP-side mirror of the supplier ledger triad).
builder.Services.AddScoped<VetSystem.Application.EmployeeLedgers.IEmployeeLedgerService,
    VetSystem.Infrastructure.EmployeeLedgers.EmployeeLedgerService>();
builder.Services.AddScoped<VetSystem.API.Employees.EmployeesService>();
builder.Services.AddScoped<VetSystem.API.Employees.EmployeePaymentsService>();

// M8 — contracts & batches (lifecycle, per-medication pricing, supervision cycles)
builder.Services.AddScoped<VetSystem.Application.Contracts.IContractLifecycleService,
    VetSystem.Infrastructure.Contracts.ContractLifecycleService>();
builder.Services.AddScoped<VetSystem.Application.Contracts.IPricingService,
    VetSystem.Infrastructure.Contracts.PricingService>();
builder.Services.AddScoped<VetSystem.API.Contracts.ContractsService>();
builder.Services.AddScoped<VetSystem.API.Contracts.ContractFarmsService>();
builder.Services.AddScoped<VetSystem.API.Contracts.BatchesService>();
builder.Services.AddScoped<VetSystem.API.Contracts.BatchSettlementService>(); // M24 — تصفية الدورة

// M9 — doctor entitlements & settlement lock. The fee-model calculators and the toggle resolver are
// pure (no state), registered as singletons; the four IExamFeeCalculator impls are enumerated by the
// factory to dispatch on supervision_fee_model. (M28 — the supervision fee IS the entitlement in both
// systems; the System-A percent/ceiling calculator is retired, the split math is pure in
// EntitlementSplitCalculator, and the System-B calculator now serves only the visit-sourced path.)
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
builder.Services.AddSingleton<VetSystem.Application.Entitlements.ISystemBDirectFeeCalculator,
    VetSystem.Application.Entitlements.SystemBDirectFeeCalculator>();
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

// M21 — out-of-process push (Expo). The typed client is the repo's first IHttpClientFactory use;
// a short timeout keeps the best-effort send from ever wedging the push worker.
builder.Services.Configure<VetSystem.Infrastructure.Notifications.ExpoPushOptions>(
    builder.Configuration.GetSection(VetSystem.Infrastructure.Notifications.ExpoPushOptions.SectionName));
builder.Services.AddHttpClient<VetSystem.Application.Notifications.IPushSender,
    VetSystem.Infrastructure.Notifications.ExpoPushSender>(client => client.Timeout = TimeSpan.FromSeconds(10));
builder.Services.AddSingleton<VetSystem.API.Notifications.PushQueue>();
builder.Services.AddHostedService<VetSystem.API.Notifications.PushDispatchHostedService>();
builder.Services.AddScoped<VetSystem.API.Devices.DeviceTokensService>();

// Domain-event → notification handlers (dispatched in a fresh scope by DispatchingDomainEventPublisher).
builder.Services.AddScoped<IDomainEventHandler<VetSystem.Domain.Events.NegativeStockAttemptedEvent>,
    VetSystem.API.Notifications.Handlers.NegativeStockAttemptedHandler>();
builder.Services.AddScoped<IDomainEventHandler<VetSystem.Domain.Events.AccountReadyForSettlementEvent>,
    VetSystem.API.Notifications.Handlers.AccountReadyForSettlementHandler>();
builder.Services.AddScoped<IDomainEventHandler<VetSystem.Domain.Events.EntitlementCreditedEvent>,
    VetSystem.API.Notifications.Handlers.EntitlementCreditedHandler>();

// Hangfire job classes — registered as services always so the recurring registration (real runs) and
// the integration tests (forced clock, invoked directly) both resolve them from DI.
builder.Services.AddScoped<VetSystem.API.Jobs.VaccinationRemindersJob>();
builder.Services.AddScoped<VetSystem.API.Jobs.MedicationDueJob>();
builder.Services.AddScoped<VetSystem.API.Jobs.LowStockAlertsJob>();
builder.Services.AddScoped<VetSystem.API.Jobs.ExpirationWarningsJob>();
builder.Services.AddScoped<VetSystem.API.Jobs.ScheduledReportDeliveryJob>();
builder.Services.AddScoped<VetSystem.API.Jobs.MonthlySalaryAccrualJob>(); // M31 — monthly salary accrual

// M12 — reports (PRD §7.9). Each report is a read-only, environment-scoped, offset-paged service over
// ApplicationDbContext; the /reports endpoint group is gated on PermissionKey.ReportsRead.
builder.Services.AddScoped<VetSystem.API.Reports.DoctorIncomeReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.ClinicProfitsReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.ProfitPerBatchReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.SalesReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.ProfitAndLossReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.InventoryMovementReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.ConsumablesReportService>(); // M27
builder.Services.AddScoped<VetSystem.API.Reports.FieldDoctorVisitsReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.KpiSummaryReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.UpcomingVaccinationsReportService>();

// M20 — profit reports (PRD §7.9): pharmacy gross margin + in-clinic / field visit profit.
builder.Services.AddScoped<VetSystem.API.Reports.PharmacyProfitReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.InClinicVisitProfitReportService>();
builder.Services.AddScoped<VetSystem.API.Reports.FieldVisitProfitReportService>();

// M12 tasks 12–13 — report export (?format=xlsx|pdf). Renderers are stateless and thread-safe, so they
// are singletons; the exporter picks JSON vs. a generated file. ClosedXML and QuestPDF each live in
// exactly one renderer (VetSystem.API.Reports.Export).
builder.Services.AddSingleton<VetSystem.API.Reports.Export.ReportExcelRenderer>();
builder.Services.AddSingleton<VetSystem.API.Reports.Export.ReportPdfRenderer>();
builder.Services.AddSingleton<VetSystem.API.Reports.Export.ReportExporter>();

builder.Services.AddScoped<ISyncDispatcher, SyncDispatcher>();
builder.Services.AddScoped<ISyncTableHandler, SyncTestHandler>();
builder.Services.AddScoped<ISyncTableHandler, CustomersSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, PetsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, FarmsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, LedgersSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, LedgerEntriesSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, InventoryMovementsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, StockItemsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, VisitsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, ProceduresSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, PrescriptionsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, DailyFollowUpsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, NightStaysSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, VaccinationsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, AttachmentsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, AppointmentsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, InvoicesSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, InvoiceItemsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, PaymentsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, ReceiptVouchersSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, ContractsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, ContractFarmsSyncHandler>();
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

    // Single in-process worker per TECH_STACK ("Backend API + Hangfire" is one component, no Redis
    // backplane). Tunable via Hangfire:WorkerCount so ops can scale the worker without a code change
    // (M14 RUNBOOK "scale Hangfire worker"); defaults to 1. Keep it modest on a single VPS.
    var hangfireWorkerCount = builder.Configuration.GetValue<int?>("Hangfire:WorkerCount") ?? 1;
    builder.Services.AddHangfireServer(o => o.WorkerCount = Math.Max(1, hangfireWorkerCount));
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

// CORS for the browser web client (the native mobile app doesn't need it). Origins are config-driven
// (Cors:AllowedOrigins) so each environment lists its own web URL(s); the dev default is the Vite
// dev/preview ports. Bearer tokens travel in the Authorization header (no cookies) so credentials are
// not enabled; AllowAnyHeader covers Authorization + Idempotency-Key.
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://localhost:4173", "https://strained-discount-washer.ngrok-free.dev"];
builder.Services.AddCors(options =>
    options.AddPolicy("web", policy => policy
        .WithOrigins(corsOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

// M13 task 10 — rate limit /sync/* with a per-user token bucket to absorb field-doctor reconnect/sync
// storms (PRD §14 risk mitigation). The limiter is always wired; the "sync" policy decides per request
// whether to enforce, reading config lazily so a test can toggle it (eager host-build reads can't be
// overridden by WebApplicationFactory — see VetApiFactory). Default: off in the "Test" environment (so
// the broad integration suite is never throttled), on everywhere else; explicit "RateLimiting:Enabled"
// wins. Tuning knobs live in "RateLimiting:Sync" — see vet-backend/CLAUDE.md operations.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("sync", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var env = httpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        var enabled = config.GetValue<bool?>("RateLimiting:Enabled") ?? !env.IsEnvironment("Test");
        if (!enabled)
        {
            return RateLimitPartition.GetNoLimiter("disabled");
        }

        // Partition per authenticated user (same identity the rest of the app uses); fall back to the
        // connection IP for the rare unauthenticated case so a key always exists.
        var accessor = httpContext.RequestServices.GetRequiredService<ICurrentUserAccessor>();
        var partitionKey = accessor.UserId?.ToString()
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        var sync = config.GetSection("RateLimiting:Sync");
        return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = sync.GetValue<int?>("TokenLimit") ?? 200,
            TokensPerPeriod = sync.GetValue<int?>("TokensPerPeriod") ?? 100,
            ReplenishmentPeriod = TimeSpan.FromSeconds(sync.GetValue<int?>("ReplenishmentPeriodSeconds") ?? 10),
            QueueLimit = sync.GetValue<int?>("QueueLimit") ?? 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    });

    // M34 — the anonymous /auth/* lookups + login are an unauthenticated phone/center oracle, so
    // partition by client IP (no user yet). Same enabled-flag + lazy-config convention as "sync".
    options.AddPolicy("auth", httpContext =>
    {
        var config = httpContext.RequestServices.GetRequiredService<IConfiguration>();
        var env = httpContext.RequestServices.GetRequiredService<IHostEnvironment>();
        var enabled = config.GetValue<bool?>("RateLimiting:Enabled") ?? !env.IsEnvironment("Test");
        if (!enabled)
        {
            return RateLimitPartition.GetNoLimiter("disabled");
        }

        var partitionKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        var auth = config.GetSection("RateLimiting:Auth");
        return RateLimitPartition.GetTokenBucketLimiter(partitionKey, _ => new TokenBucketRateLimiterOptions
        {
            TokenLimit = auth.GetValue<int?>("TokenLimit") ?? 10,
            TokensPerPeriod = auth.GetValue<int?>("TokensPerPeriod") ?? 5,
            ReplenishmentPeriod = TimeSpan.FromSeconds(auth.GetValue<int?>("ReplenishmentPeriodSeconds") ?? 60),
            QueueLimit = auth.GetValue<int?>("QueueLimit") ?? 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            AutoReplenishment = true,
        });
    });

    // Match the canonical { code, message } error shape and surface Retry-After to clients.
    options.OnRejected = async (context, cancellationToken) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString(NumberFormatInfo.InvariantInfo);
        }

        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(
            new { code = "rate_limited", message = "Too many requests; slow down and retry." },
            cancellationToken);
    };
});

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();

// CORS before auth so the browser's anonymous preflight (OPTIONS) is answered (web client only).
app.UseCors("web");

app.UseAuthentication();
app.UseAuthorization();

// M32 — live tenant suspension. After auth so the principal (and environment_id claim) is resolved;
// rejects tenant requests whose environment is suspended/deleted, bypassing platform + infra paths.
app.UseMiddleware<EnvironmentStatusMiddleware>();

// After auth so the rate-limiter partition can read the authenticated user (M13 task 10). The "sync"
// policy is a no-op limiter unless enabled, so this is safe to run in every environment.
app.UseRateLimiter();

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
    // Doses are intraday, so this scan runs every 5 minutes (the lead-time absorbs the cadence latency)
    // rather than daily like the vaccination reminder it otherwise mirrors.
    RecurringJob.AddOrUpdate<VetSystem.API.Jobs.MedicationDueJob>(
        "medication-due", j => j.RunAsync(CancellationToken.None), "*/5 * * * *");
    RecurringJob.AddOrUpdate<VetSystem.API.Jobs.LowStockAlertsJob>(
        "low-stock-alerts", j => j.RunAsync(CancellationToken.None), "0 7 * * *");
    RecurringJob.AddOrUpdate<VetSystem.API.Jobs.ExpirationWarningsJob>(
        "expiration-warnings", j => j.RunAsync(CancellationToken.None), "0 7 * * *");
    RecurringJob.AddOrUpdate<VetSystem.API.Jobs.ScheduledReportDeliveryJob>(
        "scheduled-report-delivery", j => j.RunAsync(CancellationToken.None), "0 7 * * 1");
    // M31 — salary accrual fires once a month (1st at 06:00 UTC). The period idempotency key
    // (salary-accrual-{employeeId}-{yyyyMM}) makes a same-month re-run a no-op.
    RecurringJob.AddOrUpdate<VetSystem.API.Jobs.MonthlySalaryAccrualJob>(
        "monthly-salary-accrual", j => j.RunAsync(CancellationToken.None), "0 6 1 * *");
}

if (args.Contains("--seed") || args.Contains("--force-seed"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var seeder = scope.ServiceProvider.GetRequiredService<DataSeeder>();
    await seeder.SeedAsync(force: args.Contains("--force-seed"), demo: args.Contains("--demo"));
    return;
}

await app.RunAsync();

/// <summary>Exposed as a partial class so WebApplicationFactory can target it for integration tests.</summary>
public partial class Program { }
