using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Artists.Managers;
using Arts6311.Domain.Modules.Artists.Managers.Validation;
using Arts6311.Domain.Modules.Artists.Managers.ViewModels;

namespace Arts6311.Tests.Artists;

/// <summary>Shape only: what a request must look like before Business sees it.</summary>
public sealed class ArtistValidatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreateWithJustANameIsValid()
    {
        Assert.True((await new ArtistCreateValidator().ValidateAsync(new ArtistCreateViewModel("Jane Doe"), Ct)).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ACreateNeedsAName(string name)
    {
        var result = await new ArtistCreateValidator().ValidateAsync(new ArtistCreateViewModel(name), Ct);

        Assert.Contains(result.Errors, error => error.PropertyName == "Name");
    }

    [Fact]
    public async Task ANameWithNothingToMakeASlugFromNeedsASlug()
    {
        var validator = new ArtistCreateValidator();

        Assert.False((await validator.ValidateAsync(new ArtistCreateViewModel("—"), Ct)).IsValid);
        Assert.True((await validator.ValidateAsync(new ArtistCreateViewModel("—", Slug: "dash"), Ct)).IsValid);
    }

    [Fact]
    public async Task FieldsAtTheirLimitsPassAndOnePastFails()
    {
        var validator = new ArtistCreateValidator();
        var atLimit = new ArtistCreateViewModel(
            new string('n', ArtistPolicy.NameMaxLength),
            new string('s', Slugs.MaxLength),
            new string('m', ArtistPolicy.MediumMaxLength),
            new string('b', ArtistPolicy.BioMaxLength));

        Assert.True((await validator.ValidateAsync(atLimit, Ct)).IsValid);

        var pastLimit = new ArtistCreateViewModel(
            new string('n', ArtistPolicy.NameMaxLength + 1),
            new string('s', Slugs.MaxLength + 1),
            new string('m', ArtistPolicy.MediumMaxLength + 1),
            new string('b', ArtistPolicy.BioMaxLength + 1));
        var result = await validator.ValidateAsync(pastLimit, Ct);

        Assert.Equal(
            ["Bio", "Medium", "Name", "Slug"],
            result.Errors.Select(error => error.PropertyName).Distinct().Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData("Jane-Doe")]
    [InlineData("jane doe")]
    [InlineData("")]
    public async Task ASlugThatIsSentMustBeASlug(string slug)
    {
        var result = await new ArtistCreateValidator().ValidateAsync(new ArtistCreateViewModel("Jane", Slug: slug), Ct);

        Assert.Contains(result.Errors, error => error.PropertyName == "Slug");
    }

    [Fact]
    public async Task AnEmptyPortraitIdIsRefused()
    {
        var result = await new ArtistCreateValidator().ValidateAsync(new ArtistCreateViewModel("Jane", PortraitMediaId: Guid.Empty), Ct);

        Assert.Contains(result.Errors, error => error.PropertyName == "PortraitMediaId");
    }

    [Fact]
    public async Task AnUpdateMustChangeSomething()
    {
        var validator = new ArtistUpdateValidator();

        Assert.Contains((await validator.ValidateAsync(new ArtistUpdateViewModel(), Ct)).Errors, error => error.PropertyName == "body");
        Assert.True((await validator.ValidateAsync(new ArtistUpdateViewModel(Active: false), Ct)).IsValid);
        Assert.True((await validator.ValidateAsync(new ArtistUpdateViewModel(RemovePortrait: true), Ct)).IsValid);

        // removePortrait: false asks for nothing.
        Assert.False((await validator.ValidateAsync(new ArtistUpdateViewModel(RemovePortrait: false), Ct)).IsValid);
    }

    [Fact]
    public async Task AnUpdateMayClearMediumAndBioButNotTheName()
    {
        var validator = new ArtistUpdateValidator();

        Assert.True((await validator.ValidateAsync(new ArtistUpdateViewModel(Medium: "", Bio: ""), Ct)).IsValid);
        Assert.Contains((await validator.ValidateAsync(new ArtistUpdateViewModel(Name: " "), Ct)).Errors, error => error.PropertyName == "Name");
    }

    [Fact]
    public async Task AnUpdateCannotBothSetAndRemoveThePortrait()
    {
        var result = await new ArtistUpdateValidator().ValidateAsync(
            new ArtistUpdateViewModel(PortraitMediaId: Guid.NewGuid(), RemovePortrait: true), Ct);

        Assert.Contains(result.Errors, error => error.PropertyName == "RemovePortrait");
    }

    [Fact]
    public async Task AListLimitStaysInRangeAndTheStatusIsKnown()
    {
        var validator = new ArtistListValidator();

        Assert.True((await validator.ValidateAsync(new ArtistListViewModel(Limit: PageRequest.MaxLimit), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new ArtistListViewModel(Limit: 0), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new ArtistListViewModel(Limit: PageRequest.MaxLimit + 1), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new ArtistListViewModel(Status: (PublishStatus)7), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new ArtistListViewModel(Search: new string('x', ArtistPolicy.SearchMaxLength + 1)), Ct)).IsValid);
    }
}
