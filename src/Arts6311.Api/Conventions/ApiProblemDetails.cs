using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Domain.Modules.Media.Managers;
using Arts6311.Domain.Modules.Redirects.Managers;
using Microsoft.AspNetCore.Mvc;

namespace Arts6311.Api.Conventions;

/// <summary>
/// Turns an <see cref="OperationError"/> into the wire shape <c>api-contract.md</c> promises: a
/// <c>ProblemDetails</c> carrying a stable <c>code</c> and the <c>correlationId</c>.
/// </summary>
/// <remarks>
/// The code is the contract; the status is derived from it, in one place, so two modules cannot answer the same
/// code with different statuses. Renaming a code is a breaking change (<c>api-contract.md</c> "Compatibility").
/// </remarks>
internal static class ApiProblemDetails
{
    /// <summary>The extension carrying the stable error code.</summary>
    public const string CodeExtension = "code";

    /// <summary>The extension carrying the request's correlation id (the W3C trace id, ADR 0019).</summary>
    public const string CorrelationIdExtension = "correlationId";

    /// <summary>
    /// Code → HTTP status. An unmapped code answers 400: an <see cref="OperationError"/> is an *expected* failure,
    /// which is the caller's to fix. A server fault throws instead and is caught as a 500.
    /// </summary>
    private static readonly Dictionary<string, int> StatusByCode = new(StringComparer.Ordinal)
    {
        [ErrorCodes.ValidationFailed] = StatusCodes.Status400BadRequest,
        [ErrorCodes.CursorInvalid] = StatusCodes.Status400BadRequest,
        [ErrorCodes.IdempotencyKeyRequired] = StatusCodes.Status400BadRequest,
        [ErrorCodes.IdempotencyKeyInvalid] = StatusCodes.Status400BadRequest,
        [ErrorCodes.Forbidden] = StatusCodes.Status403Forbidden,
        [ErrorCodes.NotFound] = StatusCodes.Status404NotFound,
        [ErrorCodes.Conflict] = StatusCodes.Status409Conflict,
        [ErrorCodes.ConcurrencyConflict] = StatusCodes.Status409Conflict,

        // api-contract.md "Concurrency": a write to a row-versioned record with no If-Match cannot be checked.
        [ErrorCodes.PreconditionRequired] = StatusCodes.Status428PreconditionRequired,

        // Media (media.md, ADR 0030). An upload the API cannot take is told why, by the status a client expects.
        [MediaErrorCodes.TypeUnsupported] = StatusCodes.Status415UnsupportedMediaType,
        [MediaErrorCodes.ExtensionMismatch] = StatusCodes.Status415UnsupportedMediaType,
        [MediaErrorCodes.TooLarge] = StatusCodes.Status413PayloadTooLarge,
        [MediaErrorCodes.DimensionsOutOfRange] = StatusCodes.Status400BadRequest,
        [MediaErrorCodes.Undecodable] = StatusCodes.Status400BadRequest,
        [MediaErrorCodes.Duplicate] = StatusCodes.Status409Conflict,
        [MediaErrorCodes.InUse] = StatusCodes.Status409Conflict,

        // CMS content (content.md, ADR 0031): each is a state the editor resolves, so each is a 409.
        [ContentErrorCodes.SlugTaken] = StatusCodes.Status409Conflict,
        [ContentErrorCodes.SlugLocked] = StatusCodes.Status409Conflict,
        [ContentErrorCodes.StatusTransitionInvalid] = StatusCodes.Status409Conflict,
        [ContentErrorCodes.InUse] = StatusCodes.Status409Conflict,

        // The catalog's shared SKU (ADR 0032, 0037): another record in the CMS has it, which the editor resolves by
        // choosing another.
        [CatalogErrorCodes.SkuTaken] = StatusCodes.Status409Conflict,

        // Redirects (ADR 0035): a clash with another redirect is a state the admin resolves.
        [RedirectErrorCodes.FromTaken] = StatusCodes.Status409Conflict,
        [RedirectErrorCodes.LoopOrChain] = StatusCodes.Status409Conflict,

        // Identity, on the internal routes only (auth.md). Each is 400 — the status they already answered by
        // falling through to UnmappedCodeStatus — but declared, so "one code, one status, in one place" (ADR 0021)
        // covers them and the gateway's IdentityApiClient is reading a status the contract states rather than one
        // that happens to be the default. They must never reach a browser: /bff/login collapses the first three
        // into one answer, because telling them apart enumerates accounts (ADR 0023).
        [IdentityErrorCodes.CredentialsInvalid] = StatusCodes.Status400BadRequest,
        [IdentityErrorCodes.LockedOut] = StatusCodes.Status400BadRequest,
        [IdentityErrorCodes.EmailUnconfirmed] = StatusCodes.Status400BadRequest,
        [IdentityErrorCodes.PasswordRejected] = StatusCodes.Status400BadRequest,
        [IdentityErrorCodes.TokenInvalid] = StatusCodes.Status400BadRequest,

        // api-contract.md: the same key with a different body is refused, not retried (ADR 0020).
        [ErrorCodes.IdempotencyKeyReused] = StatusCodes.Status422UnprocessableEntity,

        // Edge codes. They are produced by CodeForStatus below rather than by a module, but they belong in the
        // same map: "one code, one status" has to hold for every code in the contract, not only the ones a
        // facade raises. A later facade raising one of these then gets the status the contract promises.
        [ErrorCodes.Unauthenticated] = StatusCodes.Status401Unauthorized,
        [ErrorCodes.RouteNotFound] = StatusCodes.Status404NotFound,
        [ErrorCodes.MethodNotAllowed] = StatusCodes.Status405MethodNotAllowed,
        [ErrorCodes.UnsupportedMediaType] = StatusCodes.Status415UnsupportedMediaType,
        [ErrorCodes.PayloadTooLarge] = StatusCodes.Status413PayloadTooLarge,
        [ErrorCodes.RequestRejected] = StatusCodes.Status400BadRequest,
        [ErrorCodes.ServerError] = StatusCodes.Status500InternalServerError,
    };

    public const int UnmappedCodeStatus = StatusCodes.Status400BadRequest;

    public static int StatusFor(string code) =>
        StatusByCode.TryGetValue(code, out var status) ? status : UnmappedCodeStatus;

    /// <summary>
    /// Whether this code's status is declared above rather than taken from <see cref="UnmappedCodeStatus"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="StatusFor"/> cannot tell a code declared as 400 from one that fell back to 400, so without this
    /// the map's own test had to carry a hand-kept list of codes "genuinely" 400 — and a new 400 code forgotten
    /// in both places looked fine from either side.
    /// </remarks>
    public static bool IsDeclared(string code) => StatusByCode.ContainsKey(code);

    /// <summary>
    /// Builds the response body for an expected failure. Field errors make it a
    /// <see cref="ValidationProblemDetails"/>, so a validation failure looks the same whether it came from model
    /// binding or from a FluentValidation run in a facade.
    /// </summary>
    public static ProblemDetails From(OperationError error, Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(error);

        var status = StatusFor(error.Code);
        var problem = error.FieldErrors.Count > 0
            ? new ValidationProblemDetails(error.FieldErrors.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal))
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = TitleFor(status);

        // The error's own message. Never an exception, a provider message or another caller's data
        // (see OperationError's remarks).
        problem.Detail = error.Message;

        problem.Extensions[CodeExtension] = error.Code;

        if (error.Extensions is { Count: > 0 })
        {
            foreach (var (key, value) in error.Extensions)
            {
                // The code and correlation id are ours; a module cannot overwrite them with an extension.
                if (key is CodeExtension or CorrelationIdExtension)
                {
                    continue;
                }

                problem.Extensions[key] = value;
            }
        }

        Stamp(problem, correlationId);
        return problem;
    }

    /// <summary>
    /// Adds the correlation id to any problem, including the ones the framework writes for a 401, a 403 or an
    /// unhandled exception, so every failure the API returns can be traced back to one request.
    /// </summary>
    public static void Stamp(ProblemDetails problem, Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(problem);

        problem.Extensions[CorrelationIdExtension] = correlationId.ToString("N");
    }

    /// <summary>
    /// The code for a status the framework produced on its own, with no <see cref="OperationError"/> behind it: an
    /// empty 401 or 403, a route that does not exist, an unhandled exception. These are part of the contract too,
    /// so a client can branch on <c>code</c> for every failure rather than only for the ones a module raised.
    /// </summary>
    public static string CodeForStatus(int status) => status switch
    {
        StatusCodes.Status401Unauthorized => ErrorCodes.Unauthenticated,
        StatusCodes.Status403Forbidden => ErrorCodes.Forbidden,
        StatusCodes.Status404NotFound => ErrorCodes.RouteNotFound,
        StatusCodes.Status405MethodNotAllowed => ErrorCodes.MethodNotAllowed,
        StatusCodes.Status409Conflict => ErrorCodes.Conflict,
        StatusCodes.Status413PayloadTooLarge => ErrorCodes.PayloadTooLarge,
        StatusCodes.Status415UnsupportedMediaType => ErrorCodes.UnsupportedMediaType,
        >= 500 => ErrorCodes.ServerError,
        _ => ErrorCodes.RequestRejected,
    };

    /// <summary>
    /// A fixed phrase per status. Generic on purpose: the <c>code</c> says what happened, and a title that
    /// paraphrased the detail would only be one more string to keep in step with it.
    /// </summary>
    public static string TitleFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => "The request was not valid.",
        StatusCodes.Status401Unauthorized => "Authentication is required.",
        StatusCodes.Status403Forbidden => "This request is not allowed.",
        StatusCodes.Status404NotFound => "The resource was not found.",
        StatusCodes.Status409Conflict => "The request conflicts with the current state.",
        StatusCodes.Status428PreconditionRequired => "The request must be conditional.",
        StatusCodes.Status422UnprocessableEntity => "The request could not be processed.",
        StatusCodes.Status500InternalServerError => "The request could not be completed.",
        _ => "The request failed.",
    };
}
