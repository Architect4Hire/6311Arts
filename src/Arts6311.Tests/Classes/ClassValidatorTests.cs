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
    public async Task ASessionNeedsBothTimesAndCannotEndBeforeItStartsOrRunForDays()
    {
        Assert.Null(await FieldsAsync(With(Run(new ClassSessionViewModel(Starts, Starts.AddHours(3))))));
        Assert.Equal(["runs"], await FieldsAsync(With(Run(new ClassSessionViewModel(Starts, null)))));
        Assert.Equal(["runs"], await FieldsAsync(With(Run(new ClassSessionViewModel(Starts, Starts.AddHours(-1))))));
        Assert.Equal(["runs"], await FieldsAsync(With(Run(new ClassSessionViewModel(Starts, Starts.AddDays(8))))));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ClassPolicy.CapacityMaxSeats + 1)]
    public async Task SeatsRunFromOneToTheCeiling(int capacity)
    {
        Assert.Equal(["runs"], await FieldsAsync(With(new ClassRunViewModel(capacity))));
        Assert.Null(await FieldsAsync(With(new ClassRunViewModel(1))));
    }

    [Fact]
    public async Task EveryRunNeedsSeats()
    {
        Assert.Equal(["runs"], await FieldsAsync(With(new ClassRunViewModel(null))));
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
        var fields = await FieldsAsync(With(new ClassRunViewModel(10, Sku: sku)));

        Assert.Equal(valid ? null : ["runs"], fields);
    }

    [Fact]
    public async Task ASkuLongerThanTheColumnIsRefused()
    {
        Assert.Equal(["runs"], await FieldsAsync(With(new ClassRunViewModel(10, Sku: new string('A', SkuPolicy.MaxLength + 1)))));
    }

    [Fact]
    public async Task TwoRunsCannotShareASkuOrAnIdAndTwoSessionsCannotShareAnId()
    {
        var id = Guid.NewGuid();
        var one = new ClassRunViewModel(10, Sku: "CLS-4-1");
        var two = new ClassRunViewModel(10, Sku: "cls-4-1");
        var session = new ClassSessionViewModel(Starts, Starts.AddHours(3), Id: Guid.NewGuid());

        Assert.Equal(["runs"], await FieldsAsync(With(one, two)));
        Assert.Equal(["runs"], await FieldsAsync(With(one with { Id = id, Sku = null }, two with { Id = id, Sku = null })));
        Assert.Equal(["runs"], await FieldsAsync(With(Run(session), Run(session) with { Sku = "CLS-4-2" })));
    }

    [Fact]
    public async Task ADisplayZoneMustBeAnIanaZone()
    {
        Assert.Null(await FieldsAsync(With(new ClassRunViewModel(10, DisplayTimeZone: "America/New_York"))));
        Assert.Equal(["runs"], await FieldsAsync(With(new ClassRunViewModel(10, DisplayTimeZone: "Chicago"))));
    }

    [Fact]
    public async Task AnInstructorIdMayNotBeEmpty()
    {
        Assert.Equal(["runs"], await FieldsAsync(With(new ClassRunViewModel(10, InstructorId: Guid.Empty))));
        Assert.Null(await FieldsAsync(With(new ClassRunViewModel(10, InstructorId: Guid.NewGuid()))));
    }

    [Fact]
    public async Task ARunsLabelAndCadenceStayWithinTheirColumns()
    {
        Assert.Equal(["runs"], await FieldsAsync(With(new ClassRunViewModel(10, Label: new string('x', ClassPolicy.RunLabelMaxLength + 1)))));
        Assert.Equal(["runs"], await FieldsAsync(With(new ClassRunViewModel(10, Cadence: new string('x', ClassPolicy.CadenceMaxLength + 1)))));
    }

    [Fact]
    public async Task AClassHasAtMostTheRunCeilingAndARunAtMostTheSessionCeiling()
    {
        var runs = Enumerable.Range(0, ClassPolicy.RunsMaxCount + 1).Select(_ => new ClassRunViewModel(10)).ToArray();
        var sessions = Enumerable.Range(0, ClassPolicy.SessionsMaxCount + 1)
            .Select(week => new ClassSessionViewModel(Starts.AddDays(7 * week), Starts.AddDays(7 * week).AddHours(3)))
            .ToArray();

        Assert.Equal(["runs"], await FieldsAsync(With(runs)));
        Assert.Equal(["runs"], await FieldsAsync(With(Run(sessions))));
    }

    [Theory]
    [InlineData(0L, true)]
    [InlineData(30000L, true)]
    [InlineData(-1L, false)]
    [InlineData(ClassPolicy.StartingPriceMaxCents + 1, false)]
    public async Task ARunsStartingPriceIsCentsWithinTheTypoGuard(long cents, bool valid)
    {
        var fields = await FieldsAsync(With(new ClassRunViewModel(10, StartingPriceCents: cents)));

        Assert.Equal(valid ? null : ["runs"], fields);
    }

    [Fact]
    public async Task AnEditNeedsSomethingToChangeAndNeverBothSidesOfAToggle()
    {
        Assert.Equal(["body"], await FieldsAsync(new ClassUpdateViewModel()));
        Assert.Equal(["removeHero"], await FieldsAsync(new ClassUpdateViewModel(HeroMediaId: Guid.NewGuid(), RemoveHero: true)));
        Assert.Null(await FieldsAsync(new ClassUpdateViewModel(Runs: [])));
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

    private static ClassRunViewModel Run(params ClassSessionViewModel[] sessions) => new(10, Sessions: sessions);

    private static ClassCreateViewModel With(params ClassRunViewModel[] runs) => new("Course", Runs: runs);

    private static async Task<List<string>?> FieldsAsync(ClassCreateViewModel create) =>
        (await new ClassCreateValidator().ValidateOrErrorAsync(create, Ct))?.FieldErrors.Keys.ToList();

    private static async Task<List<string>?> FieldsAsync(ClassUpdateViewModel update) =>
        (await new ClassUpdateValidator().ValidateOrErrorAsync(update, Ct))?.FieldErrors.Keys.ToList();
}
