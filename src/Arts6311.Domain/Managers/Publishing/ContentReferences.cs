namespace Arts6311.Domain.Managers.Publishing;

/// <summary>
/// One live record that names the record someone is trying to delete, as staff would recognise it (ADR 0052).
/// </summary>
/// <param name="ReferencingType">The naming record's content type, as <see cref="SiteContentTypes"/> spells it.</param>
/// <param name="Label">What staff call it — a title or a name — so the refusal says where to go.</param>
public sealed record ContentReference(string ReferencingType, string Label);

/// <summary>
/// What one module can say about the records <strong>it</strong> holds that name another type's record: an
/// exhibition crediting an artist, a class run naming an instructor (ADR 0033, 0052).
/// </summary>
/// <remarks>
/// <para>
/// Registered by the module that holds the reference, with <c>TryAddEnumerable</c>, exactly as
/// <see cref="IScheduledPublisher"/> is. That keeps the dependency pointing the way it already does: Exhibitions
/// knows about Artists, so Exhibitions answers for its own credits, and Artists never names a module that uses it.
/// </para>
/// <para>
/// A source answers for <strong>live</strong> records only. Each one's query starts at its own soft-deletable
/// entity, so the query filter does that by itself: a credit on an exhibition that is itself deleted holds nothing
/// up.
/// </para>
/// </remarks>
public interface IContentReferenceSource
{
    /// <summary>
    /// The records this module holds that name <paramref name="entityType"/> <paramref name="entityId"/>, at most
    /// <paramref name="take"/> of them — the refusal names a few, it does not list a back catalogue. Empty when
    /// this module holds no reference to that type at all, which is the usual answer.
    /// </summary>
    Task<IReadOnlyList<ContentReference>> ReferencesToAsync(
        string entityType, Guid entityId, int take, CancellationToken cancellationToken);
}

/// <summary>Who still names a record, asked across every module that could (ADR 0052).</summary>
public interface IContentReferences
{
    /// <summary>
    /// The live records naming <paramref name="entityType"/> <paramref name="entityId"/>. A content facade asks
    /// this before it deletes one, and hands the answer to Business — the same shape as asking Media about an
    /// image (backend.md: cross-module reads belong to the facade).
    /// </summary>
    Task<IReadOnlyList<ContentReference>> ToAsync(
        string entityType, Guid entityId, CancellationToken cancellationToken);
}

/// <summary>
/// Asks every registered <see cref="IContentReferenceSource"/> in turn and gathers what they say, stopping once
/// there are enough to name.
/// </summary>
internal sealed class ContentReferences(IEnumerable<IContentReferenceSource> sources) : IContentReferences
{
    /// <summary>
    /// How many referencing records a refusal names. Enough to point somewhere, few enough to read: beyond this
    /// the message says how many more, and staff work through them.
    /// </summary>
    internal const int Take = 5;

    public async Task<IReadOnlyList<ContentReference>> ToAsync(
        string entityType, Guid entityId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(entityType);

        var found = new List<ContentReference>();
        foreach (var source in sources)
        {
            found.AddRange(await source.ReferencesToAsync(entityType, entityId, Take - found.Count, cancellationToken));
            if (found.Count >= Take)
            {
                break;
            }
        }

        return found;
    }
}

/// <summary>
/// What the facade's question came back with, for Business to decide on — the module's own
/// <c>*Check</c> records are the same arrangement (<c>ArtistPortraitCheck</c>, <c>ClassInstructorCheck</c>).
/// </summary>
/// <param name="Referencing">The live records that name this one. Empty means nothing does.</param>
public sealed record ContentReferenceCheck(IReadOnlyList<ContentReference> Referencing)
{
    /// <summary>The answer when nothing could reference the record: nothing to check.</summary>
    public static ContentReferenceCheck None { get; } = new([]);

    /// <summary>
    /// Why the record can't be deleted, or <c>null</c> when it can — one refusal, worded the same for every type,
    /// naming what to clear first (ADR 0052).
    /// </summary>
    /// <param name="what">The record being deleted, as the message calls it: "this artist".</param>
    public string? Refusal(string what)
    {
        if (Referencing.Count == 0)
        {
            return null;
        }

        var named = string.Join(", ", Referencing.Select(reference => $"“{reference.Label}”"));
        if (Referencing.Count >= ContentReferences.Take)
        {
            return $"{named} and others still name {what}. Take it off those records first, then delete it.";
        }

        return Referencing.Count == 1
            ? $"{named} still names {what}. Take it off that record first, then delete it."
            : $"{named} still name {what}. Take it off those records first, then delete it.";
    }
}
