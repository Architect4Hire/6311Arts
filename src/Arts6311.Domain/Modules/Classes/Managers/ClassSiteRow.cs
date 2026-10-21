using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Modules.Classes.Managers.ServiceModels;
using Arts6311.Domain.Modules.Instructors.Managers.ServiceModels;
using Arts6311.Domain.Modules.Media.Managers.ServiceModels;

namespace Arts6311.Domain.Modules.Classes.Managers;

/// <summary>
/// One class offering as Business hands it to the facade for the site: everything the site's models carry except the
/// hero image, which only Media can describe, and each run's instructor, which only Instructors can (backend.md:
/// cross-module reads go facade to facade).
/// </summary>
/// <param name="Runs">The upcoming runs only, soonest first: Business has already decided which those are.</param>
internal sealed record ClassSiteRow(
    Guid Id,
    string Slug,
    string Title,
    string? Summary,
    string? Body,
    string? ClassType,
    string? Level,
    Guid? HeroMediaId,
    IReadOnlyList<ClassSiteRunRow> Runs,
    DateTimeOffset UpdatedAt)
{
    /// <summary>The lowest price Square has reported for a run still to come.</summary>
    public long? FromPriceCents => Runs.Min(run => run.PriceCents);

    public SiteClassSummaryServiceModel ToSummary(
        IReadOnlyDictionary<Guid, SiteImageServiceModel> images,
        IReadOnlyDictionary<Guid, SiteInstructorCreditServiceModel> instructors) => new(
        Id, Slug, Title, Summary, ClassType, Level, Hero(images), FromPriceCents, CatalogPolicy.Currency,
        [.. Runs.Select(run => run.ToServiceModel(instructors))]);

    public SiteClassServiceModel ToServiceModel(
        IReadOnlyDictionary<Guid, SiteImageServiceModel> images,
        IReadOnlyDictionary<Guid, SiteInstructorCreditServiceModel> instructors) => new(
        Id, Slug, Title, Summary, Body, ClassType, Level, Hero(images), FromPriceCents, CatalogPolicy.Currency,
        [.. Runs.Select(run => run.ToServiceModel(instructors))]);

    private SiteImageServiceModel? Hero(IReadOnlyDictionary<Guid, SiteImageServiceModel> images) =>
        HeroMediaId is { } id && images.TryGetValue(id, out var image) ? image : null;
}

/// <summary>One upcoming run as Business hands it to the facade: the site's model but the instructor, named by id.</summary>
/// <param name="Sessions">Every sitting, in start order. Never empty: a run with no sessions has nothing to come.</param>
internal sealed record ClassSiteRunRow(
    Guid Id,
    string? Label,
    Guid? InstructorId,
    string? Cadence,
    string DisplayTimeZone,
    int Capacity,
    long? PriceCents,
    int? SeatsRemaining,
    bool SoldOut,
    IReadOnlyList<ClassSessionServiceModel> Sessions)
{
    public DateTimeOffset StartsAt => Sessions[0].StartsAt;

    public SiteClassRunServiceModel ToServiceModel(IReadOnlyDictionary<Guid, SiteInstructorCreditServiceModel> instructors) => new(
        Id,
        Label,
        InstructorId is { } id && instructors.TryGetValue(id, out var instructor) ? instructor : null,
        Cadence,
        DisplayTimeZone,
        StartsAt,
        Sessions.Max(session => session.EndsAt),
        Sessions.Count,
        Capacity,
        PriceCents,
        CatalogPolicy.Currency,
        SeatsRemaining,
        SoldOut,
        Sessions);
}
