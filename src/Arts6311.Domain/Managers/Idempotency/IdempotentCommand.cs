using Arts6311.Domain.Managers.Results;

namespace Arts6311.Domain.Managers.Idempotency;

/// <summary>A command a facade has declared idempotent.</summary>
/// <param name="Subject">
/// Who the key belongs to, so one caller's key can never replay another caller's result. A server-owned handle,
/// never request input: <c>user:{id}</c> for an authenticated caller, or for an anonymous command (checkout pay) a
/// handle the server issued, such as <c>cart:{id}</c>.
/// </param>
/// <param name="Operation">A stable operation name, e.g. <c>checkout.pay</c>.</param>
/// <param name="Key">The client's <c>Idempotency-Key</c> header value, if sent.</param>
/// <param name="Fingerprint">
/// What makes two requests "the same": the operation's meaningful inputs. Never include secrets; it is
/// HMAC-hashed and never stored, but it should not need to be sensitive.
/// </param>
/// <param name="KeyRequired">When false, a request without a key simply runs once, unprotected.</param>
public sealed record IdempotentCommand(
    string Subject, string Operation, string? Key, object Fingerprint, bool KeyRequired = true);

/// <summary>The result, and whether it was replayed from an earlier committed request.</summary>
public sealed record IdempotentOutcome<T>(OperationResult<T> Result, bool Replayed);
