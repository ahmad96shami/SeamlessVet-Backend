using FluentValidation;
using ValidationException = VetSystem.Domain.Common.ValidationException;

namespace VetSystem.API.Filters;

/// <summary>
/// Endpoint filter that resolves an <see cref="IValidator{T}"/> for the first argument of type
/// <typeparamref name="TRequest"/> and short-circuits with a <see cref="ValidationException"/>
/// when invalid. Attach via <c>group.AddEndpointFilter&lt;ValidationFilter&lt;FooDto&gt;&gt;()</c>.
/// </summary>
public sealed class ValidationFilter<TRequest> : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var validator = context.HttpContext.RequestServices.GetService<IValidator<TRequest>>();
        if (validator is null)
        {
            return await next(context);
        }

        var request = context.Arguments.OfType<TRequest>().FirstOrDefault();
        if (request is null)
        {
            return await next(context);
        }

        var result = await validator.ValidateAsync(request, context.HttpContext.RequestAborted);
        if (!result.IsValid)
        {
            var fieldErrors = result.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(
                    g => g.Key,
                    g => g.Select(e => e.ErrorMessage).ToArray());

            throw new ValidationException(fieldErrors);
        }

        return await next(context);
    }
}
