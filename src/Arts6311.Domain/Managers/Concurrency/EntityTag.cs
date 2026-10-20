using Arts6311.Domain.Managers.Results;

namespace Arts6311.Domain.Managers.Concurrency;

/// <summary>
/// The <c>ETag</c> / <c>If-Match</c> form of a record's row version (api-contract.md "Concurrency"). Every editable
/// CMS record carries a <see cref="Guid"/> row version the application rotates on each staff write; this is how it
/// travels over HTTP. Moved here from Media (2.3) when Artists (2.4) became the second user.
/// </summary>
public static class EntityTag
{
    public static string Format(Guid rowVersion) => $"\"{rowVersion:N}\"";

    /// <summary>The row version a strong <c>If-Match</c> names, or <c>null</c> for anything else (weak, several, <c>*</c>).</summary>
    public static Guid? Parse(string? ifMatch)
    {
        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return null;
        }

        // Strong only: If-Match never matches a weak tag (RFC 9110 section 13.1.1).
        var value = ifMatch.Trim();
        return value.Length == 34 && value[0] == '"' && value[^1] == '"'
            && Guid.TryParseExact(value[1..^1], "N", out var version)
                ? version
                : null;
    }

    /// <summary>
    /// Checks a write's <c>If-Match</c> against the version it would change: <c>null</c> when they agree, otherwise
    /// <c>concurrency.precondition_required</c> (absent, 428), <c>validation.failed</c> (not one strong tag of ours,
    /// 400) or <c>concurrency.conflict</c> (stale, 409).
    /// </summary>
    /// <param name="what">How the record is named to the editor, for example "this artist".</param>
    public static OperationError? Verify(Guid currentVersion, string? ifMatch, string what) =>
        Precondition(ifMatch, what) ?? (Parse(ifMatch) == currentVersion ? null : Conflict(what));

    /// <summary>
    /// The half of <see cref="Verify"/> that needs no database: <c>null</c> when <paramref name="ifMatch"/> is one
    /// strong tag of ours, otherwise the 428 or 400. A facade checks this before validating the body or asking another
    /// module, so a write without <c>If-Match</c> is always a 428 (api-contract.md), whatever else is wrong with it.
    /// </summary>
    public static OperationError? Precondition(string? ifMatch, string what)
    {
        ArgumentException.ThrowIfNullOrEmpty(what);

        if (string.IsNullOrWhiteSpace(ifMatch))
        {
            return OperationError.Of(
                ErrorCodes.PreconditionRequired, "Send the ETag you last read as If-Match, so a newer edit is not overwritten.");
        }

        // Present but not one strong ETag of ours ("*", a weak tag, a list, garbage): the request is malformed, not
        // stale, so it is a 400 rather than a 409 the editor would try to resolve.
        return Parse(ifMatch) is null
            ? OperationError.Validation(
                "If-Match is not valid.",
                [("ifMatch", $"If-Match must be the single ETag {what} was read with.")])
            : null;
    }

    /// <summary>The <c>concurrency.conflict</c> a stale write gets, whether the check or the save caught it.</summary>
    public static OperationError Conflict(string what) =>
        OperationError.Of(ErrorCodes.ConcurrencyConflict, $"Someone else changed {what}. Reload it and try again.");
}
