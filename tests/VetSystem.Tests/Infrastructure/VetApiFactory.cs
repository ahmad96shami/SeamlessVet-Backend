using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using VetSystem.API.Identity;

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

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = PgTestScope.ConnectionString,
                // BootstrapAdmin:Password remains a placeholder so the seeder no-ops in tests;
                // we drive ours through PgTestScope + AdminTestSeed instead.
            });
        });

        builder.ConfigureServices(services =>
        {
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
