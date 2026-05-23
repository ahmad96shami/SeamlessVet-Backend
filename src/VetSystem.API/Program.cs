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

// M4 — inventory (warehouse + field), delta-only append-only movements
builder.Services.AddSingleton<IDomainEventPublisher,
    VetSystem.Infrastructure.Messaging.InMemoryDomainEventPublisher>();
builder.Services.AddSingleton<VetSystem.Application.Inventory.IMovementTranslator,
    VetSystem.Application.Inventory.MovementTranslator>();
builder.Services.AddScoped<VetSystem.Application.Inventory.IInventoryService,
    VetSystem.Infrastructure.Inventory.InventoryService>();
builder.Services.AddScoped<VetSystem.Application.Inventory.IInventoryScanService,
    VetSystem.Infrastructure.Inventory.InventoryScanService>();
builder.Services.AddScoped<VetSystem.API.Inventory.InventoryAdminService>();

builder.Services.AddScoped<ISyncDispatcher, SyncDispatcher>();
builder.Services.AddScoped<ISyncTableHandler, SyncTestHandler>();
builder.Services.AddScoped<ISyncTableHandler, CustomersSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, PetsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, LedgersSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, LedgerEntriesSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, InventoryMovementsSyncHandler>();
builder.Services.AddScoped<ISyncTableHandler, StockItemsSyncHandler>();
builder.Services.AddScoped<IdempotencyKeyFilter>();

builder.Services.AddMemoryCache();

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
