using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Exhibitions.Managers;
using Arts6311.Domain.Modules.Exhibitions.Managers.Validation;
using Arts6311.Domain.Modules.Exhibitions.Managers.ViewModels;

namespace Arts6311.Tests.Exhibitions;

/// <summary>Shape only: what a request must look like before Business sees it, and the field names errors come back under.</summary>
public sealed class ExhibitionValidatorTests
{
    private static readonly DateOnly Start = new(2026, 10, 24);
    private static readonly DateOnly End = new(2026, 12, 5);
    private static readonly DateTimeOffset SixPm = new(2026, 10, 24, 18, 0, 0, TimeSpan.FromHours(-5));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreateWithATitleAndBothDatesIsValid()
    {
        Assert.Null(await FieldsAsync(new ExhibitionCreateViewModel("Boats & Pianos", Start, End)));
    }

    [Fact]
    public async Task ACreateNeedsATitleAndBothDates()
    {
        var fields = await FieldsAsync(new ExhibitionCreateViewModel(" ", null, null));

        Assert.Equal(["endDate", "startDate", "title"], fields!.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AShowMayOpenAndCloseOnOneDayButNeverEndBeforeItStarts()
    {
        Assert.Null(await FieldsAsync(new ExhibitionCreateViewModel("One night", Start, Start)));
        Assert.Equal(["endDate"], await FieldsAsync(new ExhibitionCreateViewModel("Backwards", Start, Start.AddDays(-1))));
        Assert.Equal(["endDate"], await FieldsAsync(new ExhibitionUpdateViewModel(StartDate: Start, EndDate: Start.AddDays(-1))));

        // One date alone is Business's to compare with the one stored.
        Assert.Null(await FieldsAsync(new ExhibitionUpdateViewModel(EndDate: Start.AddDays(-1))));
    }

    [Fact]
    public async Task ATitleWithNothingToMakeASlugFromNeedsASlug()
    {
        Assert.Equal(["title"], await FieldsAsync(new ExhibitionCreateViewModel("—", Start, End)));
        Assert.Null(await FieldsAsync(new ExhibitionCreateViewModel("—", Start, End, Slug: "dash")));
        Assert.Equal(["slug"], await FieldsAsync(new ExhibitionCreateViewModel("Fine", Start, End, Slug: "Not A Slug")));
    }

    [Fact]
    public async Task FieldsAtTheirLimitsPassAndOnePastFails()
    {
        var atLimits = new ExhibitionCreateViewModel(
            new string('t', ExhibitionPolicy.TitleMaxLength), Start, End,
            Summary: new string('s', ExhibitionPolicy.SummaryMaxLength),
            Body: new string('b', ExhibitionPolicy.BodyMaxLength),
            Reception: new ExhibitionReceptionViewModel(SixPm, SixPm.AddHours(3), new string('n', ExhibitionPolicy.ReceptionNoteMaxLength)));
        var pastLimits = atLimits with
        {
            Title = atLimits.Title + "t",
            Summary = atLimits.Summary + "s",
            Body = atLimits.Body + "b",
            Reception = atLimits.Reception! with { Note = atLimits.Reception.Note + "n" },
        };

        Assert.Null(await FieldsAsync(atLimits));
        Assert.Equal(["body", "reception.note", "summary", "title"], (await FieldsAsync(pastLimits))!.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task AReceptionHasBothTimesAndEndsAfterItStarts()
    {
        Assert.Equal(
            ["reception.endsAt", "reception.startsAt"],
            (await FieldsAsync(new ExhibitionCreateViewModel("Show", Start, End, Reception: new(null, null))))!.Order(StringComparer.Ordinal));

        var backwards = await new ExhibitionCreateValidator().ValidateOrErrorAsync(
            new ExhibitionCreateViewModel("Show", Start, End, Reception: new(SixPm, SixPm)), Ct);
        Assert.Equal(["The reception must end after it starts."], backwards!.FieldErrors["reception.endsAt"]);

        // Offsets are any offset: Business keeps the instant as UTC.
        Assert.Null(await FieldsAsync(new ExhibitionCreateViewModel(
            "Show", Start, End, Reception: new(SixPm, SixPm.ToUniversalTime().AddHours(3)))));
    }

    [Fact]
    public async Task CreditsAreRealIdsEachOnceAndNoMoreThanThePolicyAllows()
    {
        var jane = Guid.NewGuid();
        var tooMany = Enumerable.Range(0, ExhibitionPolicy.ArtistsMaxCount + 1).Select(_ => Guid.NewGuid()).ToList();

        Assert.Null(await FieldsAsync(new ExhibitionCreateViewModel("Show", Start, End, ArtistIds: [jane, Guid.NewGuid()])));
        Assert.Equal(["artistIds"], await FieldsAsync(new ExhibitionCreateViewModel("Show", Start, End, ArtistIds: [jane, jane])));
        Assert.Equal(["artistIds"], await FieldsAsync(new ExhibitionCreateViewModel("Show", Start, End, ArtistIds: [Guid.Empty])));
        Assert.Equal(["artistIds"], await FieldsAsync(new ExhibitionCreateViewModel("Show", Start, End, ArtistIds: tooMany)));
        Assert.Equal(["artistIds"], await FieldsAsync(new ExhibitionUpdateViewModel(ArtistIds: [jane, jane])));
    }

    [Fact]
    public async Task AnEmptyCreditListIsAnEditThatClearsThem()
    {
        Assert.Null(await FieldsAsync(new ExhibitionUpdateViewModel(ArtistIds: [])));
    }

    [Fact]
    public async Task AnEditMustChangeSomethingAndCannotBothSetAndRemove()
    {
        Assert.Equal(["body"], await FieldsAsync(new ExhibitionUpdateViewModel()));
        Assert.Equal(["removeHero"], await FieldsAsync(new ExhibitionUpdateViewModel(HeroMediaId: Guid.NewGuid(), RemoveHero: true)));
        Assert.Equal(
            ["removeReception"],
            await FieldsAsync(new ExhibitionUpdateViewModel(Reception: new(SixPm, SixPm.AddHours(1)), RemoveReception: true)));
        Assert.Null(await FieldsAsync(new ExhibitionUpdateViewModel(RemoveReception: true)));
    }

    [Fact]
    public async Task TheListLimitsItsPageSizeStatusAndSearch()
    {
        var validator = new ExhibitionListValidator();

        Assert.True((await validator.ValidateAsync(new ExhibitionListViewModel(Limit: PageRequest.MaxLimit, Status: PublishStatus.Archived), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new ExhibitionListViewModel(Limit: 0), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new ExhibitionListViewModel(Status: (PublishStatus)5), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new ExhibitionListViewModel(Search: new string('x', ExhibitionPolicy.SearchMaxLength + 1)), Ct)).IsValid);
    }

    private static async Task<List<string>?> FieldsAsync(ExhibitionCreateViewModel create) =>
        (await new ExhibitionCreateValidator().ValidateOrErrorAsync(create, Ct))?.FieldErrors.Keys.ToList();

    private static async Task<List<string>?> FieldsAsync(ExhibitionUpdateViewModel update) =>
        (await new ExhibitionUpdateValidator().ValidateOrErrorAsync(update, Ct))?.FieldErrors.Keys.ToList();
}
