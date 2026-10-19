using Arts6311.Domain.Managers.Results;

namespace Arts6311.Domain.Managers.Idempotency;

/// <summary>
/// Facade helper for commands declared idempotent. Not applied automatically: a facade opts in per operation,
/// wrapping its single Business call. The wrapped operation must be short (long work returns 202 and a job)
/// and must write only through the request's DbContext so its writes — and the outbox messages its DataLayer
/// stages — commit with the idempotency record.
/// </summary>
/// <remarks>
/// The executor owns the whole unit: it clears the change tracker before each attempt, so nothing may be staged on
/// the request's DbContext before it is called.
/// </remarks>
public interface IIdempotentCommandExecutor
{
    Task<IdempotentOutcome<T>> ExecuteAsync<T>(
        IdempotentCommand command, Func<CancellationToken, Task<OperationResult<T>>> operation, CancellationToken cancellationToken);
}

internal sealed class IdempotentCommandExecutor(IIdempotencyBusiness business) : IIdempotentCommandExecutor
{
    public Task<IdempotentOutcome<T>> ExecuteAsync<T>(
        IdempotentCommand command, Func<CancellationToken, Task<OperationResult<T>>> operation, CancellationToken cancellationToken) =>
        business.ExecuteAsync(command, operation, cancellationToken);
}
