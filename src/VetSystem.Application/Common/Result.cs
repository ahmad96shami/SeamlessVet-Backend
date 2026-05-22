namespace VetSystem.Application.Common;

/// <summary>
/// Standard error payload returned from Application services. Mirrors the public API error shape
/// <c>{ code, message, fieldErrors? }</c> that <c>ExceptionHandlingMiddleware</c> emits.
/// </summary>
public sealed record Error(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]>? FieldErrors = null)
{
    public static readonly Error None = new(string.Empty, string.Empty);

    public static Error NotFound(string entity, object key)
        => new("not_found", $"{entity} '{key}' was not found.");

    public static Error Conflict(string code, string message) => new(code, message);

    public static Error Forbidden(string code, string message) => new(code, message);

    public static Error Validation(IReadOnlyDictionary<string, string[]> fieldErrors)
        => new("validation_failed", "One or more validation errors occurred.", fieldErrors);
}

public class Result
{
    protected Result(bool isSuccess, Error error)
    {
        if (isSuccess && error != Error.None)
        {
            throw new InvalidOperationException("A successful result cannot carry an error.");
        }

        if (!isSuccess && error == Error.None)
        {
            throw new InvalidOperationException("A failed result must carry an error.");
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) => new(value, true, Error.None);

    public static Result<T> Failure<T>(Error error) => new(default, false, error);
}

public sealed class Result<T> : Result
{
    private readonly T? _value;

    internal Result(T? value, bool isSuccess, Error error) : base(isSuccess, error)
    {
        _value = value;
    }

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException("Cannot read Value on a failed Result.");

    public static implicit operator Result<T>(T value) => Success(value);
}
