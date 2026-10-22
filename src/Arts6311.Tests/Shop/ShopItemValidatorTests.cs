using Arts6311.Domain.Managers.Catalog;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Shop.Managers;
using Arts6311.Domain.Modules.Shop.Managers.Validation;
using Arts6311.Domain.Modules.Shop.Managers.ViewModels;

namespace Arts6311.Tests.Shop;

/// <summary>Shape only: what a request must look like before Business sees it, and the field names errors come back under.</summary>
public sealed class ShopItemValidatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreateWithATitleIsValidAndOneWithoutIsNot()
    {
        Assert.Null(await FieldsAsync(new ShopItemCreateViewModel("Copper Cuff")));
        Assert.Equal(["title"], await FieldsAsync(new ShopItemCreateViewModel(" ")));
        Assert.Equal(["title"], await FieldsAsync(new ShopItemCreateViewModel("———")));
    }

    [Theory]
    [InlineData("jewelry", true)]
    [InlineData("books-magazines", true)]
    [InlineData("merch", false)]
    [InlineData("Jewelry", false)]
    [InlineData("", false)]
    public async Task ACategoryComesFromTheAllowList(string category, bool valid)
    {
        var fields = await FieldsAsync(new ShopItemCreateViewModel("A Piece", Categories: [category]));

        Assert.Equal(valid ? null : ["categories"], fields);
    }

    [Fact]
    public async Task EveryAllowedCategoryIsAcceptedAndNoneTwice()
    {
        var all = ShopPolicy.Categories.Select(category => category.Slug).ToList();

        Assert.Null(await FieldsAsync(new ShopItemCreateViewModel("A Piece", Categories: all)));
        Assert.Equal(
            ["categories"],
            await FieldsAsync(new ShopItemCreateViewModel("A Piece", Categories: ["jewelry", "jewelry"])));
    }

    [Theory]
    [InlineData("ART-17", true)]
    [InlineData("art-17", true)]
    [InlineData("CUFF.001", true)]
    [InlineData("ART 17", false)]
    [InlineData("ART--17", false)]
    public async Task ASkuIsLettersDigitsHyphensAndDots(string sku, bool valid)
    {
        var fields = await FieldsAsync(new ShopItemCreateViewModel("A Piece", Sku: sku));

        Assert.Equal(valid ? null : ["sku"], fields);
    }

    [Fact]
    public async Task ASkuLongerThanTheColumnIsRefused()
    {
        var sku = new string('A', SkuPolicy.MaxLength + 1);

        Assert.Equal(["sku"], await FieldsAsync(new ShopItemCreateViewModel("A Piece", Sku: sku)));
    }

    [Fact]
    public async Task TheGalleryHasRealImagesEachOnceAndNoMoreThanTheCeiling()
    {
        var image = Guid.NewGuid();
        var tooMany = Enumerable.Range(0, ShopPolicy.GalleryMaxCount + 1).Select(_ => Guid.NewGuid()).ToList();

        Assert.Null(await FieldsAsync(new ShopItemCreateViewModel("A Piece", ImageMediaIds: [image])));
        Assert.Equal(
            ["imageMediaIds"],
            await FieldsAsync(new ShopItemCreateViewModel("A Piece", ImageMediaIds: [image, image])));
        Assert.Equal(
            ["imageMediaIds"],
            await FieldsAsync(new ShopItemCreateViewModel("A Piece", ImageMediaIds: [Guid.Empty])));
        Assert.Equal(
            ["imageMediaIds"],
            await FieldsAsync(new ShopItemCreateViewModel("A Piece", ImageMediaIds: tooMany)));
    }

    [Theory]
    [InlineData(0L, true)]
    [InlineData(52500L, true)]
    [InlineData(-1L, false)]
    [InlineData(ShopPolicy.StartingPriceMaxCents + 1, false)]
    public async Task TheStartingPriceIsCentsWithinTheTypoGuard(long cents, bool valid)
    {
        var fields = await FieldsAsync(new ShopItemCreateViewModel("A Piece", StartingPriceCents: cents));

        Assert.Equal(valid ? null : ["startingPriceCents"], fields);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(25, true)]
    [InlineData(0, false)]
    [InlineData(ShopPolicy.StartingStockMaxQuantity + 1, false)]
    public async Task TheStartingCountIsAtLeastOne(int quantity, bool valid)
    {
        var fields = await FieldsAsync(new ShopItemCreateViewModel("A Piece", StartingStockQuantity: quantity));

        Assert.Equal(valid ? null : ["startingStockQuantity"], fields);
    }

    [Fact]
    public async Task AnEditNeedsSomethingToChangeAndNeverBothSidesOfAToggle()
    {
        Assert.Equal(["body"], await FieldsAsync(new ShopItemUpdateViewModel()));
        Assert.Equal(
            ["removeArtist"],
            await FieldsAsync(new ShopItemUpdateViewModel(ArtistId: Guid.NewGuid(), RemoveArtist: true)));
        Assert.Equal(
            ["removeStartingPrice"],
            await FieldsAsync(new ShopItemUpdateViewModel(StartingPriceCents: 1000, RemoveStartingPrice: true)));
        Assert.Null(await FieldsAsync(new ShopItemUpdateViewModel(Categories: [])));
        Assert.Null(await FieldsAsync(new ShopItemUpdateViewModel(OneOfAKind: true)));
    }

    [Fact]
    public async Task ASlugIsCheckedForShapeOnBothPaths()
    {
        Assert.Equal(["slug"], await FieldsAsync(new ShopItemCreateViewModel("A Piece", Slug: "Not A Slug")));
        Assert.Equal(["slug"], await FieldsAsync(new ShopItemUpdateViewModel(Slug: "Not A Slug")));
    }

    [Fact]
    public async Task TheListLimitsItsPageSizeStatusSearchAndCategory()
    {
        var validator = new ShopItemListValidator();

        Assert.True((await validator.ValidateAsync(new ShopItemListViewModel(Limit: PageRequest.MaxLimit, Status: PublishStatus.Archived), Ct)).IsValid);
        Assert.True((await validator.ValidateAsync(new ShopItemListViewModel(Category: "prints"), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new ShopItemListViewModel(Limit: 0), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new ShopItemListViewModel(Status: (PublishStatus)5), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new ShopItemListViewModel(Category: "merch"), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new ShopItemListViewModel(Search: new string('x', ShopPolicy.SearchMaxLength + 1)), Ct)).IsValid);
    }

    [Fact]
    public async Task AnImportNeedsSomeTextWithinTheLimitAndAFileNameFitForTheAuditTrail()
    {
        var validator = new ShopImportValidator();

        Assert.Null(await validator.ValidateOrErrorAsync(new ShopImportViewModel("title\nBowl", "artwork.csv"), Ct));
        Assert.Null(await validator.ValidateOrErrorAsync(new ShopImportViewModel(new string('x', ShopPolicy.ImportMaxCharacters)), Ct));
        Assert.Equal(["csv"], await FieldsAsync(validator, new ShopImportViewModel(" ")));
        Assert.Equal(["csv"], await FieldsAsync(validator, new ShopImportViewModel(new string('x', ShopPolicy.ImportMaxCharacters + 1))));
        Assert.Equal(["csv"], await FieldsAsync(validator, new ShopImportViewModel("title\nGicl\uFFFDe")));
        Assert.Equal(["csv"], await FieldsAsync(validator, new ShopImportViewModel("title\nBo\0wl")));
        Assert.Null(await validator.ValidateOrErrorAsync(new ShopImportViewModel("title,sku\r\n\"Bowl\tone\",A-1\n"), Ct));
        Assert.Equal(["fileName"], await FieldsAsync(validator, new ShopImportViewModel("title\nBowl", "art\nwork.csv")));
        Assert.Equal(["fileName"], await FieldsAsync(validator, new ShopImportViewModel("title\nBowl", new string('a', ShopPolicy.ImportFileNameMaxLength + 1))));
    }

    private static async Task<List<string>?> FieldsAsync(ShopImportValidator validator, ShopImportViewModel import) =>
        (await validator.ValidateOrErrorAsync(import, Ct))?.FieldErrors.Keys.ToList();

    private static async Task<List<string>?> FieldsAsync(ShopItemCreateViewModel create) =>
        (await new ShopItemCreateValidator().ValidateOrErrorAsync(create, Ct))?.FieldErrors.Keys.ToList();

    private static async Task<List<string>?> FieldsAsync(ShopItemUpdateViewModel update) =>
        (await new ShopItemUpdateValidator().ValidateOrErrorAsync(update, Ct))?.FieldErrors.Keys.ToList();
}
