using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Managers.Time;
using Microsoft.Extensions.Options;

namespace Arts6311.Domain.Managers.Idempotency;

internal interface IIdempotencyBusiness
{
    Task<IdempotentOutcome<T>> ExecuteAsync<T>(
        IdempotentCommand command, Func<CancellationToken, Task<OperationResult<T>>> operation, CancellationToken cancellationToken);
}

internal sealed class IdempotencyBusiness(
    IIdempotencyDataLayer dataLayer, IClock clock, IOptions<IdempotencyOptions> options) : IIdempotencyBusiness
{
    private static readonly JsonSerializerOptions ResultJson = new(JsonSerializerDefaults.Web);

    public async Task<IdempotentOutcome<T>> ExecuteAsync<T>(
        IdempotentCommand command, Func<CancellationToken, Task<OperationResult<T>>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(operation);
        EnsureStorable(typeof(T));

        // A bad subject or operation name is the calling facade's bug, not the client's, so it throws. Printable ASCII
        // like the key: the scope columns are varchar (IdempotencyRecordConfiguration).
        if (!IsPrintableAscii(command.Subject, IdempotencyPolicy.SubjectMaxLength))
        {
            throw new ArgumentException(
                $"Idempotency subjects are required: 1–{IdempotencyPolicy.SubjectMaxLength} printable ASCII characters.",
                nameof(command));
        }

        if (!IsPrintableAscii(command.Operation, IdempotencyPolicy.OperationMaxLength))
        {
            throw new ArgumentException(
                $"Operation names are required: 1–{IdempotencyPolicy.OperationMaxLength} printable ASCII characters.",
                nameof(command));
        }

        if (string.IsNullOrEmpty(command.Key))
        {
            return command.KeyRequired
                ? Failure<T>(ErrorCodes.IdempotencyKeyRequired, $"This request requires an {IdempotencyPolicy.KeyHeader} header.")
                : new IdempotentOutcome<T>(await operation(cancellationToken), Replayed: false);
        }

        if (!IsPrintableAscii(command.Key, IdempotencyPolicy.KeyMaxLength))
        {
            return Failure<T>(ErrorCodes.IdempotencyKeyInvalid,
                $"{IdempotencyPolicy.KeyHeader} must be 1–{IdempotencyPolicy.KeyMaxLength} printable ASCII characters.");
        }

        var scope = new IdempotencyScope(command.Subject, command.Operation, command.Key);
        var fingerprint = Fingerprint(command.Operation, command.Fingerprint);
        var now = clock.UtcNow;

        var attempt = await dataLayer.ExecuteAsync(scope, fingerprint, now, now + IdempotencyPolicy.Retention, operation,
            value => JsonSerializer.Serialize(value, ResultJson), cancellationToken);

        if (attempt.Executed is { } executed)
        {
            return new IdempotentOutcome<T>(executed, Replayed: false);
        }

        // Constant-time: how much of a guessed fingerprint matches must not leak through timing.
        var existing = attempt.Existing!;
        if (!CryptographicOperations.FixedTimeEquals(existing.FingerprintHash, fingerprint))
        {
            return Failure<T>(ErrorCodes.IdempotencyKeyReused,
                "This idempotency key was already used for a different request.");
        }

        var replayed = JsonSerializer.Deserialize<T>(existing.ResultJson, ResultJson)!;
        return new IdempotentOutcome<T>(OperationResult.Success(replayed), Replayed: true);
    }

    /// <summary>HMAC-SHA256 of the operation and a canonical (key-sorted) JSON form of the fingerprint.</summary>
    /// <remarks>
    /// Keyed so the stored hash cannot be tested against guessed payloads by anyone without the server key, and
    /// canonical so two requests that differ only in property order are the same request.
    /// </remarks>
    internal byte[] Fingerprint(string operation, object fingerprint)
    {
        ArgumentNullException.ThrowIfNull(fingerprint);

        var canonical = Canonicalize(JsonSerializer.SerializeToNode(fingerprint, fingerprint.GetType(), ResultJson));
        return HMACSHA256.HashData(
            options.Value.KeyBytes, Encoding.UTF8.GetBytes(operation + "\n" + (canonical?.ToJsonString() ?? "null")));
    }

    private static JsonNode? Canonicalize(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj
            .OrderBy(property => property.Key, StringComparer.Ordinal)
            .Select(property => KeyValuePair.Create(property.Key, Canonicalize(property.Value)))),
        JsonArray array => new JsonArray([.. array.Select(Canonicalize)]),
        _ => node?.DeepClone(),
    };

    // '!' to '~': printable, and no spaces, so nothing can differ only by invisible characters.
    private static bool IsPrintableAscii(string? value, int maxLength) =>
        !string.IsNullOrEmpty(value)
        && value.Length <= maxLength
        && value.All(character => character is >= '!' and <= '~');

    /// <summary>Results are stored as JSON for replay; binary payloads are never stored.</summary>
    private static void EnsureStorable(Type type)
    {
        if (type == typeof(byte[]) || typeof(Stream).IsAssignableFrom(type) || type == typeof(ReadOnlyMemory<byte>)
            || type == typeof(Memory<byte>))
        {
            throw new NotSupportedException($"Idempotent results of type {type.Name} cannot be stored; return a ServiceModel.");
        }
    }

    private static IdempotentOutcome<T> Failure<T>(string code, string message) =>
        new(OperationResult.Failure<T>(OperationError.Of(code, message)), Replayed: false);
}
