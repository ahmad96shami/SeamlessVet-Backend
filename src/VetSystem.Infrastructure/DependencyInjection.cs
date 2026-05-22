using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VetSystem.Domain.Common;
using VetSystem.Infrastructure.Persistence;

namespace VetSystem.Infrastructure;

public static class DependencyInjection
{
    public const string PostgresConnectionStringName = "Postgres";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IGuidV7Generator, GuidV7Generator>();
        services.AddScoped<AuditingSaveChangesInterceptor>();

        services.AddDbContext<ApplicationDbContext>((sp, options) =>
        {
            var connectionString = configuration.GetConnectionString(PostgresConnectionStringName)
                ?? throw new InvalidOperationException(
                    $"ConnectionStrings:{PostgresConnectionStringName} is not configured.");

            options
                .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
                .UseSnakeCaseNamingConvention()
                .AddInterceptors(sp.GetRequiredService<AuditingSaveChangesInterceptor>());
        });

        return services;
    }
}
