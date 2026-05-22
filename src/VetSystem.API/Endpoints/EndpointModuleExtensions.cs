using System.Reflection;

namespace VetSystem.API.Endpoints;

public static class EndpointModuleExtensions
{
    public static IServiceCollection AddEndpointModules(this IServiceCollection services, params Assembly[] assemblies)
    {
        var scanned = assemblies.Length == 0 ? [Assembly.GetExecutingAssembly()] : assemblies;

        foreach (var moduleType in scanned.SelectMany(a => a.GetTypes())
                     .Where(t => typeof(IEndpointModule).IsAssignableFrom(t)
                                 && t is { IsClass: true, IsAbstract: false }))
        {
            services.AddSingleton(typeof(IEndpointModule), moduleType);
        }

        return services;
    }

    public static IEndpointRouteBuilder MapEndpointModules(this IEndpointRouteBuilder endpoints)
    {
        var modules = endpoints.ServiceProvider.GetServices<IEndpointModule>();
        foreach (var module in modules)
        {
            module.MapEndpoints(endpoints);
        }

        return endpoints;
    }
}
