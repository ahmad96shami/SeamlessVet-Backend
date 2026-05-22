using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VetSystem.Domain.Common;

namespace VetSystem.API.Middleware;

/// <summary>
/// Maps domain exceptions and infrastructure-level failures into the canonical
/// <c>{ code, message, fieldErrors? }</c> response shape (TECH_STACK Error Handling).
/// </summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _env;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment env)
    {
        _next = next;
        _logger = logger;
        _env = env;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (DomainException ex)
        {
            await WriteErrorAsync(context, MapDomainException(ex), ex.Code, ex.Message, ex.FieldErrors);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            _logger.LogWarning(ex, "Concurrency conflict on {Path}", context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status409Conflict, "concurrency_conflict",
                "The record was modified by another writer.");
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogWarning(ex, "Unique violation on {Path}", context.Request.Path);
            await WriteErrorAsync(context, StatusCodes.Status409Conflict, "unique_violation",
                "A record with the same key already exists.");
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected — no point writing a body.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception at {Path}", context.Request.Path);
            await WriteErrorAsync(
                context,
                StatusCodes.Status500InternalServerError,
                "internal_error",
                _env.IsDevelopment() ? ex.Message : "An unexpected error occurred.");
        }
    }

    private static int MapDomainException(DomainException ex) => ex switch
    {
        NotFoundException => StatusCodes.Status404NotFound,
        ForbiddenException => StatusCodes.Status403Forbidden,
        ConflictException => StatusCodes.Status409Conflict,
        ValidationException => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status400BadRequest,
    };

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is Npgsql.PostgresException pg && pg.SqlState == "23505";

    private static async Task WriteErrorAsync(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.Clear();
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";

        var payload = new ErrorResponse(code, message, fieldErrors);
        await JsonSerializer.SerializeAsync(context.Response.Body, payload, JsonOptions);
    }

    private sealed record ErrorResponse(
        string Code,
        string Message,
        IReadOnlyDictionary<string, string[]>? FieldErrors);
}
