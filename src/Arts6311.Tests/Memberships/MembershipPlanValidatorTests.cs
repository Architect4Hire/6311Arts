using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Memberships.Managers;
using Arts6311.Domain.Modules.Memberships.Managers.Validation;
using Arts6311.Domain.Modules.Memberships.Managers.ViewModels;

namespace Arts6311.Tests.Memberships;

/// <summary>Shape only: what a request must look like before Business sees it, and the field names errors come back under.</summary>
public sealed class MembershipPlanValidatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreateWithANameIsValidAndOneWithoutIsNot()
    {
        Assert.Null(await FieldsAsync(new MembershipPlanCreateViewModel("Gray")));
        Assert.Equal(["name"], await FieldsAsync(new MembershipPlanCreateViewModel(" ")));
        Assert.Equal(["name"], await FieldsAsync(new MembershipPlanCreateViewModel("———")));
    }

    [Fact]
    public async Task PerksAreFewEnoughNeverBlankAndNeverListedTwice()
    {
        Assert.Null(await FieldsAsync(new MembershipPlanCreateViewModel("Gray", Perks: ["5% off", "Tote"])));
        Assert.Null(await FieldsAsync(new MembershipPlanCreateViewModel("Gray", Perks: [])));
        Assert.Equal(["perks"], await FieldsAsync(new MembershipPlanCreateViewModel("Gray", Perks: ["Tote", " "])));
        Assert.Equal(["perks"], await FieldsAsync(new MembershipPlanCreateViewModel("Gray", Perks: ["Tote", "tote"])));
        Assert.Equal(
            ["perks"],
            await FieldsAsync(new MembershipPlanCreateViewModel("Gray", Perks: [new string('x', MembershipPolicy.PerkMaxLength + 1)])));
        Assert.Equal(
            ["perks"],
            await FieldsAsync(new MembershipPlanCreateViewModel(
                "Gray",
                Perks: [.. Enumerable.Range(0, MembershipPolicy.PerksMaxCount + 1).Select(index => $"Perk {index}")])));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(15, true)]
    [InlineData(MembershipPolicy.DiscountMaxPercent, true)]
    [InlineData(-1, false)]
    [InlineData(MembershipPolicy.DiscountMaxPercent + 1, false)]
    public async Task TheClassDiscountIsWholePercent(int discount, bool valid)
    {
        var fields = await FieldsAsync(new MembershipPlanCreateViewModel("Gray", DiscountPercent: discount));

        Assert.Equal(valid ? null : ["discountPercent"], fields);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(MembershipPolicy.DisplayOrderMax, true)]
    [InlineData(0, false)]
    [InlineData(MembershipPolicy.DisplayOrderMax + 1, false)]
    public async Task TheDisplayOrderStartsAtOne(int order, bool valid)
    {
        var fields = await FieldsAsync(new MembershipPlanCreateViewModel("Gray", DisplayOrder: order));

        Assert.Equal(valid ? null : ["displayOrder"], fields);
    }

    [Theory]
    [InlineData(0L, true)]
    [InlineData(10_000L, true)]
    [InlineData(MembershipPolicy.StartingPriceMaxCents, true)]
    [InlineData(-1L, false)]
    [InlineData(MembershipPolicy.StartingPriceMaxCents + 1, false)]
    public async Task TheStartingPriceIsCentsUnderTheTypoGuard(long price, bool valid)
    {
        var fields = await FieldsAsync(new MembershipPlanCreateViewModel("Gray", StartingPriceCents: price));

        Assert.Equal(valid ? null : ["startingPriceCents"], fields);
    }

    [Fact]
    public async Task ACadenceIsOneTheCmsOffers()
    {
        Assert.Null(await FieldsAsync(new MembershipPlanCreateViewModel("Gray", StartingCadence: MembershipCadence.Monthly)));
        Assert.Equal(
            ["startingCadence"],
            await FieldsAsync(new MembershipPlanCreateViewModel("Gray", StartingCadence: (MembershipCadence)7)));
        Assert.Equal(
            ["startingCadence"],
            await FieldsAsync(new MembershipPlanUpdateViewModel(StartingCadence: (MembershipCadence)7)));
    }

    [Fact]
    public async Task AnEditNeedsSomethingToChangeAndNeverBothSidesOfAToggle()
    {
        Assert.Equal(["body"], await FieldsAsync(new MembershipPlanUpdateViewModel()));
        Assert.Equal(
            ["removeStartingPrice"],
            await FieldsAsync(new MembershipPlanUpdateViewModel(StartingPriceCents: 1000, RemoveStartingPrice: true)));
        Assert.Null(await FieldsAsync(new MembershipPlanUpdateViewModel(Perks: [])));
        Assert.Null(await FieldsAsync(new MembershipPlanUpdateViewModel(Featured: true)));
    }

    [Fact]
    public async Task ASlugIsCheckedForShapeOnBothPaths()
    {
        Assert.Equal(["slug"], await FieldsAsync(new MembershipPlanCreateViewModel("Gray", Slug: "Not A Slug")));
        Assert.Equal(["slug"], await FieldsAsync(new MembershipPlanUpdateViewModel(Slug: "Not A Slug")));
    }

    [Fact]
    public async Task TheListLimitsItsPageSizeStatusAndSearch()
    {
        var validator = new MembershipPlanListValidator();

        Assert.True((await validator.ValidateAsync(
            new MembershipPlanListViewModel(Limit: PageRequest.MaxLimit, Status: PublishStatus.Archived), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new MembershipPlanListViewModel(Limit: 0), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new MembershipPlanListViewModel(Status: (PublishStatus)5), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(
            new MembershipPlanListViewModel(Search: new string('x', MembershipPolicy.SearchMaxLength + 1)), Ct)).IsValid);
    }

    private static async Task<List<string>?> FieldsAsync(MembershipPlanCreateViewModel create) =>
        (await new MembershipPlanCreateValidator().ValidateOrErrorAsync(create, Ct))?.FieldErrors.Keys.ToList();

    private static async Task<List<string>?> FieldsAsync(MembershipPlanUpdateViewModel update) =>
        (await new MembershipPlanUpdateValidator().ValidateOrErrorAsync(update, Ct))?.FieldErrors.Keys.ToList();
}
