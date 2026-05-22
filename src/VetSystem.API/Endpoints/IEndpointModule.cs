namespace VetSystem.API.Endpoints;

/// <summary>
/// Modular endpoint registration. Each feature owns one module that registers a
/// <see cref="RouteGroupBuilder"/> for its resource and attaches shared filters.
/// Loaded by assembly scan from <c>Program.cs</c> so adding a domain is one new file,
/// not a new <c>MapEndpoints</c> call.
/// </summary>
public interface IEndpointModule
{
    void MapEndpoints(IEndpointRouteBuilder endpoints);
}
