using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Classes.Managers;
using Arts6311.Domain.Modules.Classes.Managers.Validation;
using Arts6311.Domain.Modules.Classes.Managers.ViewModels;

namespace Arts6311.Tests.Classes;

/// <summary>Shape only: what a request must look like before Business sees it, and the field names errors come back under.</summary>
public sealed class ClassValidatorTests
{
    private static readonly DateTimeOffset Starts = new(2026, 10, 24, 19, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreateWithATitleIsValidAndOneWithoutIsNot()
    {
        Assert.Null(await FieldsAsync(new ClassCreateViewModel("Intro to Screen Printing")));
        Assert.Equal(["title"], await FieldsAsync(new ClassCreateViewModel(" ")));
    }

    [Fact]
    public async Task ASessionNeedsBothTimesAndSeatsAndCannotEndBeforeItStarts()
    {
        Assert.Null(await FieldsAsync(With(new ClassSessionViewModel(Starts, Starts.AddHours(3), 10))));
        Assert.Equal(["sessions"], await FieldsAsync(With(new ClassSessionViewModel(Starts, null, 10))));
        Assert.Equal(["sessions"], await FieldsAsync(With(new ClassSessionViewModel(Starts, Starts.AddHours(3), null))));
        Assert.Equal(["sessions"], await FieldsAsync(With(new ClassSessionViewModel(Starts, Starts.AddHours(-1), 10))));
        Assert.Equal(["sessions"], await FieldsAsync(With(new ClassSessionViewModel(Starts, Starts.AddDays(8), 10))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ClassPolicy.CapacityMaxSeats + 1)]
    public async Task SeatsRunFromOneToTheCeiling(int capacity)
    {
        Assert.Equal(["sessions"], await FieldsAsync(With(new ClassSessionViewModel(Starts, Starts.AddHours(3), capacity))));
        Assert.Null(await FieldsAsync(With(new ClassSessionViewModel(Starts, Starts.AddHours(3), 1))));
    }

    [Theory]
    [InlineData("CLS-4-1", true)]
    [InlineData("cls-4-1", true)]
    [InlineData("WORKSHOP.2B", true)]
    [InlineData("CLS 4 1", false)]
    [InlineData("CLS--4", false)]
    [InlineData("", false)]
    public async Task ASkuIsLettersDigitsHyphensAndDots(string sku, bool valid)
    {
        var fields = await FieldsAsync(With(new ClassSessionViewModel(Starts, Starts.AddHours(3), 10, Sku: sku)));

        Assert.Equal(valid ? null : ["sessions"], fields);
    }

    [Fact]
    public async Task ASkuLongerThanTheColumnIsRefused()
    {
        var sku = new string('A', SkuPolicy.MaxLength + 1);

        Assert.Equal(["sessions"], await FieldsAsync(With(new ClassSessionViewModel(Starts, Starts.AddHours(3), 10, Sku: sku))));
    }

    [Fact]
    public async Task TwoSessionsCannotShareASkuOrAnId()
    {
        var id = Guid.NewGuid();
        var one = new ClassSessionViewModel(Starts, Starts.AddHours(3), 10, Sku: "CLS-4-1");
        var two = new ClassSessionViewModel(Starts.AddDays(7), Starts.AddDays(7).AddHours(3), 10, Sku: "cls-4-1");

        Assert.Equal(["sessions"], await FieldsAsync(With(one, two)));
        Assert.Equal(["sessions"], await FieldsAsync(With(one with { Id = id, Sku = null }, two with { Id = id, Sku = null })));
    }

    [Fact]
    public async Task ADisplayZoneMustBeAnIanaZone()
    {
        Assert.Null(await FieldsAsync(With(new ClassSessionViewModel(Starts, Starts.AddHours(3), 10, DisplayTimeZone: "America/New_York"))));
        Assert.Equal(
            ["sessions"],
            await FieldsAsync(With(new ClassSessionViewModel(Starts, Starts.AddHours(3), 10, DisplayTimeZone: "Chicago"))));
    }

    [Fact]
    public async Task AClassHasAtMostTheSessionCeiling()
    {
        var sessions = Enumerable.Range(0, ClassPolicy.SessionsMaxCount + 1)
            .Select(week => new ClassSessionViewModel(Starts.AddDays(7 * week), Starts.AddDays(7 * week).AddHours(3), 10))
            .ToList();

        Assert.Equal(["sessions"], await FieldsAsync(new ClassCreateViewModel("Course", Sessions: sessions)));
    }

    [Theory]
    [InlineData(0L, true)]
    [InlineData(30000L, true)]
    [InlineData(-1L, false)]
    [InlineData(ClassPolicy.StartingPriceMaxCents + 1, false)]
    public async Task TheStartingPriceIsCentsWithinTheTypoGuard(long cents, bool valid)
    {
        var fields = await FieldsAsync(new ClassCreateViewModel("Course", StartingPriceCents: cents));

        Assert.Equal(valid ? null : ["startingPriceCents"], fields);
    }

    [Fact]
    public async Task AnEditNeedsSomethingToChangeAndNeverBothSidesOfAToggle()
    {
        Assert.Equal(["body"], await FieldsAsync(new ClassUpdateViewModel()));
        Assert.Equal(["removeHero"], await FieldsAsync(new ClassUpdateViewModel(HeroMediaId: Guid.NewGuid(), RemoveHero: true)));
        Assert.Equal(
            ["removeStartingPrice"],
            await FieldsAsync(new ClassUpdateViewModel(StartingPriceCents: 1000, RemoveStartingPrice: true)));
        Assert.Null(await FieldsAsync(new ClassUpdateViewModel(Sessions: [])));
    }

    [Fact]
    public async Task ASlugIsCheckedForShapeOnBothPaths()
    {
        Assert.Equal(["slug"], await FieldsAsync(new ClassCreateViewModel("Course", Slug: "Not A Slug")));
        Assert.Equal(["slug"], await FieldsAsync(new ClassUpdateViewModel(Slug: "Not A Slug")));
        Assert.Equal(["title"], await FieldsAsync(new ClassCreateViewModel("———")));
    }

    [Fact]
    public async Task TheListLimitsItsPageSizeStatusAndSearch()
    {
        var validator = new ClassListValidator();

        Assert.True((await validator.ValidateAsync(new ClassListViewModel(Limit: PageRequest.MaxLimit, Status: PublishStatus.Archived), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new ClassListViewModel(Limit: 0), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new ClassListViewModel(Status: (PublishStatus)5), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new ClassListViewModel(Search: new string('x', ClassPolicy.SearchMaxLength + 1)), Ct)).IsValid);
    }

    private static ClassCreateViewModel With(params ClassSessionViewModel[] sessions) =>
        new("Course", Sessions: sessions);

    private static async Task<List<string>?> FieldsAsync(ClassCreateViewModel create) =>
        (await new ClassCreateValidator().ValidateOrErrorAsync(create, Ct))?.FieldErrors.Keys.ToList();

    private static async Task<List<string>?> FieldsAsync(ClassUpdateViewModel update) =>
        (await new ClassUpdateValidator().ValidateOrErrorAsync(update, Ct))?.FieldErrors.Keys.ToList();
}
