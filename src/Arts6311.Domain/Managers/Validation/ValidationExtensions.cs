using Arts6311.Domain.Managers.Paging;
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

    /// <summary>
    /// A list's <c>sort</c>: absent, or one of the columns <paramref name="order"/> sorts by, with a leading minus for
    /// descending (ADR 0062). An unknown column is a 400 naming the ones there are, never a quiet fall-back.
    /// </summary>
    public static IRuleBuilderOptions<T, string?> SortableBy<T, TEntity, TRow>(
        this IRuleBuilder<T, string?> rule, ListOrder<TEntity, TRow> order)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(order);

        return rule.Must(order.Allows).WithMessage(
            $"Sort by one of: {string.Join(", ", order.Columns)}. Put '-' before it for descending.");
    }
}
