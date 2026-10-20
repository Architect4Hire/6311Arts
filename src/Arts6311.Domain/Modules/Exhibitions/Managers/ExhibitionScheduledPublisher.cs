using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Exhibitions.Facade;

namespace Arts6311.Domain.Modules.Exhibitions.Managers;

/// <summary>
/// How the Worker's scheduled publisher reaches exhibitions (ADR 0036): the kernel port over this module's facade,
/// so the job works through facades and the kernel still names no module (add-background-job step 3).
/// </summary>
internal sealed class ExhibitionScheduledPublisher(IExhibitionsFacade facade) : IScheduledPublisher
{
    public string ContentType => ExhibitionAudit.ResourceType;

    public Task<IReadOnlyList<Guid>> ListDueAsync(DateTimeOffset asOf, int take, CancellationToken cancellationToken) =>
        facade.ListDueAsync(asOf, take, cancellationToken);

    public Task<ScheduledPublishOutcome> PublishDueAsync(Guid id, DateTimeOffset asOf, CancellationToken cancellationToken) =>
        facade.PublishScheduledAsync(id, asOf, cancellationToken);
}
