namespace Arts6311.Domain.Managers.Results;

/// <summary>
/// An expected application failure with a stable, machine-readable code (<see cref="ErrorCodes"/>). The API maps
/// it to <c>ProblemDetails</c> with the code and the correlation id (backend.md "Contracts").
/// </summary>
/// <param name="Code">Stable error code, e.g. <c>resource.not_found</c>.</param>
/// <param name="Message">Human-readable summary. Never carries secrets, provider messages or another user's data.</param>
/// <param name="FieldErrors">Errors keyed by camelCase request field name.</param>
/// <param name="Extensions">
/// Facts the caller has to act on (surfaced as ProblemDetails extensions), never an explanation and never anything
/// the caller may not see. Values must be JSON-serializable.
/// </param>
public sealed record OperationError(
    string Code,
    string Message,
    IReadOnlyDictionary<string, string[]> FieldErrors,
    IReadOnlyDictionary<string, object?>? Extensions = null)
{
    private static readonly IReadOnlyDictionary<string, string[]> NoFieldErrors = new Dictionary<string, string[]>();

    public static OperationError Of(string code, string message) => new(code, message, NoFieldErrors);

    public static OperationError Validation(string message, IEnumerable<(string Field, string Error)> errors)
    {
        ArgumentNullException.ThrowIfNull(errors);

        return new(
            ErrorCodes.ValidationFailed,
            message,
            errors
                .GroupBy(error => ToCamelCase(error.Field), StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Select(error => error.Error).ToArray(), StringComparer.Ordinal));
    }

    private static string ToCamelCase(string field) =>
        string.IsNullOrEmpty(field) ? field : char.ToLowerInvariant(field[0]) + field[1..];
}

/// <summary>
/// The outcome of a facade operation: a value, or an expected <see cref="OperationError"/>. Created through
/// <see cref="OperationResult.Success{T}(T)"/> and <see cref="OperationResult.Failure{T}(OperationError)"/>.
/// </summary>
public sealed class OperationResult<T>
{
    internal OperationResult(T? value, OperationError? error) => (Value, Error) = (value, error);

    public T? Value { get; }

    public OperationError? Error { get; }

    public bool Succeeded => Error is null;
}

/// <summary>
/// The outcome of a facade command that returns no value, and the factory for <see cref="OperationResult{T}"/>.
/// </summary>
public sealed class OperationResult
{
    public static OperationResult<T> Success<T>(T value) => new(value, null);

    public static OperationResult<T> Failure<T>(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error);
    }

    private static readonly OperationResult SuccessResult = new(null);

    private OperationResult(OperationError? error) => Error = error;

    public OperationError? Error { get; }

    public bool Succeeded => Error is null;

    public static OperationResult Success() => SuccessResult;

    public static OperationResult Failure(OperationError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error);
    }
}
