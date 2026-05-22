using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using VetSystem.Application.Common;

namespace VetSystem.Infrastructure.Persistence;

/// <summary>
/// Used by <c>dotnet ef migrations add/update</c>. Reads <c>appsettings*.json</c> from the API
/// project (current dir + parent walks back to the solution) so design-time and runtime share one
/// connection string source. The current-user accessor stays anonymous — migrations never apply
/// query filters.
/// </summary>
public sealed class DesignTimeApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var basePath = FindApiProjectPath();

        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddUserSecrets("vet-system-secrets")
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString(DependencyInjection.PostgresConnectionStringName)
            ?? throw new InvalidOperationException(
                $"ConnectionStrings:{DependencyInjection.PostgresConnectionStringName} not configured for design-time DbContext.");

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName))
            .UseSnakeCaseNamingConvention()
            .Options;

        return new ApplicationDbContext(options, new DesignTimeCurrentUserAccessor());
    }

    private static string FindApiProjectPath()
    {
        var current = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "src", "VetSystem.API");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "appsettings.json")))
            {
                return candidate;
            }

            current = current.Parent;
        }

        return Directory.GetCurrentDirectory();
    }

    private sealed class DesignTimeCurrentUserAccessor : ICurrentUserAccessor
    {
        public bool IsAuthenticated => false;
        public Guid? UserId => null;
        public Guid? EnvironmentId => null;
        public string? Role => null;
        public IReadOnlyCollection<string> Permissions => Array.Empty<string>();
    }
}
