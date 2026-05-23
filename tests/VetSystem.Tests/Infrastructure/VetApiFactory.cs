using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using VetSystem.API.Identity;
using VetSystem.Application.Common;

namespace VetSystem.Tests.Infrastructure;

/// <summary>
/// In-process API host for integration tests.
///
/// <para>Two tiers of override are needed because <c>Program.cs</c> reads
/// <c>builder.Configuration["Jwt:SecretKey"]</c> eagerly during host setup — before
/// <see cref="IWebHostBuilder.ConfigureAppConfiguration"/> callbacks fire — and bakes that value
/// into the JwtBearer <see cref="TokenValidationParameters"/>. So:</para>
/// <list type="number">
/// <item><see cref="IWebHostBuilder.ConfigureAppConfiguration"/> handles the values
///       read lazily (the Postgres connection string).</item>
/// <item><see cref="OptionsServiceCollectionExtensions.PostConfigure"/> on <c>JwtOptions</c> +
///       <c>JwtBearerOptions</c> overrides the eagerly-read JWT secret so the same key signs
///       and validates tokens.</item>
/// </list>
/// </summary>
public sealed class VetApiFactory : WebApplicationFactory<Program>
{
    public const string TestJwtSecret = "vet-tests-jwt-secret-32-bytes-minimum-1234567890";
    public const string TestJwtIssuer = "vet-system-api";
    public const string TestJwtAudience = "vet-system-clients";

    /// <summary>
    /// Optional R2/S3 overrides. When <see cref="R2ServiceUrl"/> is set (e.g. a MinIO test
    /// container), the host's <c>R2</c> config points at it so attachment uploads/downloads round-trip
    /// for real. Left null by default, so the placeholder config applies and non-R2 tests are
    /// unaffected. Set via object initializer before the first <c>CreateClient</c>/<c>Services</c> use.
    /// </summary>
    public string? R2ServiceUrl { get; init; }
    public string? R2AccessKey { get; init; }
    public string? R2SecretKey { get; init; }
    public string? R2Bucket { get; init; }

    /// <summary>
    /// Optional <see cref="IClock"/> override. Set to a <c>FakeClock</c> to drive the M11 Hangfire
    /// jobs (vaccination reminders etc.) at a forced "today" without touching the wall clock. Replaces
    /// the registered <c>SystemClock</c> for the whole host when set.
    /// </summary>
    public IClock? Clock { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            var overrides = new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = PgTestScope.ConnectionString,
                // BootstrapAdmin:Password remains a placeholder so the seeder no-ops in tests;
                // we drive ours through PgTestScope + AdminTestSeed instead.
            };

            if (R2ServiceUrl is not null)
            {
                overrides["R2:ServiceUrl"] = R2ServiceUrl;
                overrides["R2:AccessKey"] = R2AccessKey;
                overrides["R2:SecretKey"] = R2SecretKey;
                overrides["R2:Bucket"] = R2Bucket;
                overrides["R2:Region"] = "us-east-1";
            }

            cfg.AddInMemoryCollection(overrides);
        });

        builder.ConfigureServices(services =>
        {
            if (Clock is not null)
            {
                services.RemoveAll<IClock>();
                services.AddSingleton(Clock);
            }

            services.PostConfigure<JwtOptions>(opts =>
            {
                opts.Issuer = TestJwtIssuer;
                opts.Audience = TestJwtAudience;
                opts.SecretKey = TestJwtSecret;
                opts.AccessTokenMinutes = 15;
                opts.RefreshTokenDays = 7;
            });

            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, opts =>
            {
                opts.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestJwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestJwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.FromSeconds(30),
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestJwtSecret)),
                };
            });
        });
    }
}
