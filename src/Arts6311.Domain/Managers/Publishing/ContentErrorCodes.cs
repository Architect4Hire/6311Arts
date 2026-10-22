namespace Arts6311.Domain.Managers.Publishing;

/// <summary>
/// The error codes every CMS content type shares. Part of the API contract; each has its status in
/// <c>ApiProblemDetails</c> (api-contract.md).
/// </summary>
public static class ContentErrorCodes
{
    /// <summary>Another record of the same type already has this slug. 409.</summary>
    public const string SlugTaken = "cms.slug_taken";

    /// <summary>
    /// The record has been published, so its slug is someone's bookmark. 409. From 2.7 a change creates a
    /// <c>Redirect</c> instead of being refused (content.md, ADR 0031).
    /// </summary>
    public const string SlugLocked = "cms.slug_locked";

    /// <summary>The status command does not apply to the record's current status, for example publishing an archived record. 409.</summary>
    public const string StatusTransitionInvalid = "cms.status_transition_invalid";

    /// <summary>
    /// Another record names this one — an exhibition credits the artist, a class run names the instructor — so it
    /// can't be deleted while that is true (content.md "Deletion", ADR 0052). 409. The refusal names what
    /// references it, so staff know where to go; it is the content equivalent of <c>media.in_use</c>.
    /// </summary>
    public const string InUse = "cms.in_use";
}
