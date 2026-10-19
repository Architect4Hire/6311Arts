namespace Arts6311.Domain.Managers.Results;

/// <summary>
/// Stable error codes shared by every module. Codes are part of the API contract (api-contract.md): renaming one
/// is a breaking change. Module-specific codes live in the module and follow the same <c>area.reason</c> shape.
/// </summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "validation.failed";
    public const string NotFound = "resource.not_found";
    public const string Conflict = "resource.conflict";
    public const string ConcurrencyConflict = "concurrency.conflict";
    public const string Forbidden = "access.forbidden";
    public const string CursorInvalid = "paging.cursor.invalid";

    /// <summary>No credential, or one the API would not accept. Returned with a 401.</summary>
    public const string Unauthenticated = "auth.unauthenticated";

    /// <summary>The route does not exist. Returned with a 404 that no module raised.</summary>
    public const string RouteNotFound = "request.route_not_found";

    /// <summary>The route exists but not for this HTTP method.</summary>
    public const string MethodNotAllowed = "request.method_not_allowed";

    /// <summary>The body's media type is not one the route reads.</summary>
    public const string UnsupportedMediaType = "request.unsupported_media_type";

    /// <summary>The request body is larger than the route accepts.</summary>
    public const string PayloadTooLarge = "request.payload_too_large";

    /// <summary>
    /// A request the edge refused for a reason with no more specific code. Each status gets its own code above, so
    /// that one code never means two statuses; this is the remainder.
    /// </summary>
    public const string RequestRejected = "request.rejected";

    /// <summary>An unexpected fault. The cause is in the logs under the correlation id, never in the response.</summary>
    public const string ServerError = "server.error";

    /// <summary>A command that requires an <c>Idempotency-Key</c> was sent without one.</summary>
    public const string IdempotencyKeyRequired = "idempotency.key_required";

    /// <summary>The <c>Idempotency-Key</c> is empty, too long, or not printable ASCII.</summary>
    public const string IdempotencyKeyInvalid = "idempotency.key_invalid";

    /// <summary>The key was already used for a different request (api-contract.md: 422).</summary>
    public const string IdempotencyKeyReused = "idempotency.key_reused";
}
