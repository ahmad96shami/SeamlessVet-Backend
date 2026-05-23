namespace VetSystem.Domain.Common;

/// <summary>
/// Base for invariant violations raised in the Application/Domain layer.
/// The global <c>ExceptionHandlingMiddleware</c> maps these to <c>{ code, message, fieldErrors? }</c>.
/// </summary>
public abstract class DomainException : Exception
{
    public string Code { get; }

    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; }

    protected DomainException(
        string code,
        string message,
        IReadOnlyDictionary<string, string[]>? fieldErrors = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        FieldErrors = fieldErrors;
    }
}

public sealed class NotFoundException : DomainException
{
    public NotFoundException(string entity, object key)
        : base("not_found", $"{entity} '{key}' was not found.")
    {
    }
}

public sealed class ConflictException : DomainException
{
    public ConflictException(string code, string message)
        : base(code, message)
    {
    }
}

public sealed class ForbiddenException : DomainException
{
    public ForbiddenException(string code, string message)
        : base(code, message)
    {
    }
}

/// <summary>
/// The HTTP method is not allowed on this resource — e.g. any client write to <c>stock_items</c>,
/// which is a server-derived materialized balance (SCHEMA "Key invariants" #2). Maps to 405.
/// </summary>
public sealed class MethodNotAllowedException : DomainException
{
    public MethodNotAllowedException(string code, string message)
        : base(code, message)
    {
    }
}

public sealed class ValidationException : DomainException
{
    public ValidationException(IReadOnlyDictionary<string, string[]> fieldErrors)
        : base("validation_failed", "One or more validation errors occurred.", fieldErrors)
    {
    }
}
