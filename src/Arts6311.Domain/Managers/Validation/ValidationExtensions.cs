using Arts6311.Domain.Managers.Results;
using FluentValidation;

namespace Arts6311.Domain.Managers.Validation;

/// <summary>
/// Turns a FluentValidation run into the facade's own currency, an <see cref="OperationError"/>, so shape
/// validation never throws across a layer boundary (<c>backend.md</c> "Validation and transactions": shape at the
/// edge, invariants in Business).
/// </summary>
public static class ValidationExtensions
{
    /// <summary>
    /// Validates <paramref name="model"/> and returns <c>null</c> when it is valid, or the
    /// <c>validation.failed</c> error with its field errors. The API renders that as a 400 whose <c>errors</c> are
    /// keyed by camelCase field name, identical in shape to a model-binding failure.
    /// </summary>
    public static async Task<OperationError?> ValidateOrErrorAsync<T>(
        this IValidator<T> validator, T model, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(validator);

        var result = await validator.ValidateAsync(model, cancellationToken);

        return result.IsValid
            ? null
            : OperationError.Validation(
                "One or more fields are not valid.",
                result.Errors.Select(failure => (failure.PropertyName, failure.ErrorMessage)));
    }
}
