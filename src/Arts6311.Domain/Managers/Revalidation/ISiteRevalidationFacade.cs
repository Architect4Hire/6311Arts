using Arts6311.Domain.Managers.Outbox.Events;

namespace Arts6311.Domain.Managers.Revalidation;

/// <summary>
/// Applies one <c>site.revalidate-requested</c> event: the cache entries a change affects stop being served
/// (ADR 0036). The kernel owns it because no module owns the cache, as with <c>email.send-requested</c>'s
/// delivery (backend.md "Where an ambiguous rule goes", add-background-job step 3).
/// </summary>
/// <remarks>
/// It is the only thing in the system that invalidates by tag. Facades read and write the cache (backend.md), and
/// invalidation is never inline after a save: it arrives here, from the Worker, through the outbox (outbox.md).
/// </remarks>
public interface ISiteRevalidationFacade
{
    /// <summary>
    /// Invalidates the type tag, the entity tag and each path tag the event names. There is nothing to reload: the
    /// event is the fact that something changed, and the next reader runs the factory again.
    /// </summary>
    Task RevalidateAsync(SiteRevalidateRequested request, CancellationToken cancellationToken);
}
