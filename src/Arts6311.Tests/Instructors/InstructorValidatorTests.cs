using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Publishing;
using Arts6311.Domain.Modules.Instructors.Managers;
using Arts6311.Domain.Modules.Instructors.Managers.Validation;
using Arts6311.Domain.Modules.Instructors.Managers.ViewModels;

namespace Arts6311.Tests.Instructors;

/// <summary>Shape only: what a request must look like before Business sees it.</summary>
public sealed class InstructorValidatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreateWithJustANameIsValid()
    {
        Assert.True((await new InstructorCreateValidator().ValidateAsync(new InstructorCreateViewModel("Jane Doe"), Ct)).IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ACreateNeedsAName(string name)
    {
        var result = await new InstructorCreateValidator().ValidateAsync(new InstructorCreateViewModel(name), Ct);

        Assert.Contains(result.Errors, error => error.PropertyName == "Name");
    }

    [Fact]
    public async Task ANameWithNothingToMakeASlugFromNeedsASlug()
    {
        var validator = new InstructorCreateValidator();

        Assert.False((await validator.ValidateAsync(new InstructorCreateViewModel("—"), Ct)).IsValid);
        Assert.True((await validator.ValidateAsync(new InstructorCreateViewModel("—", Slug: "dash"), Ct)).IsValid);
    }

    [Fact]
    public async Task FieldsAtTheirLimitsPassAndOnePastFails()
    {
        var validator = new InstructorCreateValidator();
        var atLimit = new InstructorCreateViewModel(
            new string('n', InstructorPolicy.NameMaxLength),
            new string('s', Slugs.MaxLength),
            new string('m', InstructorPolicy.MediumMaxLength),
            new string('b', InstructorPolicy.BioMaxLength));

        Assert.True((await validator.ValidateAsync(atLimit, Ct)).IsValid);

        var pastLimit = new InstructorCreateViewModel(
            new string('n', InstructorPolicy.NameMaxLength + 1),
            new string('s', Slugs.MaxLength + 1),
            new string('m', InstructorPolicy.MediumMaxLength + 1),
            new string('b', InstructorPolicy.BioMaxLength + 1));
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
        var result = await new InstructorCreateValidator().ValidateAsync(new InstructorCreateViewModel("Jane", Slug: slug), Ct);

        Assert.Contains(result.Errors, error => error.PropertyName == "Slug");
    }

    [Theory]
    [InlineData("https://janedoe.studio", true)]
    [InlineData("https://janedoe.studio/work?x=1", true)]
    [InlineData("http://janedoe.studio", false)]
    [InlineData("janedoe.studio", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("https://user:pass@janedoe.studio", false)]
    public async Task AWebsiteMustBeAnAbsoluteHttpsAddress(string website, bool valid)
    {
        var create = await new InstructorCreateValidator().ValidateAsync(new InstructorCreateViewModel("Jane", Website: website), Ct);
        var update = await new InstructorUpdateValidator().ValidateAsync(new InstructorUpdateViewModel(Website: website), Ct);

        Assert.Equal(valid, create.IsValid);
        Assert.Equal(valid, update.IsValid);
    }

    [Theory]
    [InlineData("jane.doe_1", true)]
    [InlineData("@Jane.Doe", true)]
    [InlineData("jane doe", false)]
    [InlineData("jane-doe", false)]
    [InlineData("abcdefghijklmnopqrstuvwxyz12345", false)]
    public async Task AnInstagramHandleHasTheShapeInstagramAllows(string handle, bool valid)
    {
        var result = await new InstructorCreateValidator().ValidateAsync(new InstructorCreateViewModel("Jane", Instagram: handle), Ct);

        Assert.Equal(valid, result.IsValid);
    }

    [Fact]
    public async Task AnUpdateCannotBothSetAndRemoveTheArtistLink()
    {
        var result = await new InstructorUpdateValidator().ValidateAsync(
            new InstructorUpdateViewModel(ArtistId: Guid.NewGuid(), RemoveArtist: true), Ct);

        Assert.Contains(result.Errors, error => error.PropertyName == "RemoveArtist");
    }

    [Fact]
    public async Task AnEmptyPortraitIdIsRefused()
    {
        var result = await new InstructorCreateValidator().ValidateAsync(new InstructorCreateViewModel("Jane", PortraitMediaId: Guid.Empty), Ct);

        Assert.Contains(result.Errors, error => error.PropertyName == "PortraitMediaId");
    }

    [Fact]
    public async Task AnUpdateMustChangeSomething()
    {
        var validator = new InstructorUpdateValidator();

        Assert.Contains((await validator.ValidateAsync(new InstructorUpdateViewModel(), Ct)).Errors, error => error.PropertyName == "body");
        Assert.True((await validator.ValidateAsync(new InstructorUpdateViewModel(Active: false), Ct)).IsValid);
        Assert.True((await validator.ValidateAsync(new InstructorUpdateViewModel(RemovePortrait: true), Ct)).IsValid);

        // removePortrait: false asks for nothing.
        Assert.False((await validator.ValidateAsync(new InstructorUpdateViewModel(RemovePortrait: false), Ct)).IsValid);
    }

    [Fact]
    public async Task AnUpdateMayClearMediumAndBioButNotTheName()
    {
        var validator = new InstructorUpdateValidator();

        Assert.True((await validator.ValidateAsync(new InstructorUpdateViewModel(Medium: "", Bio: ""), Ct)).IsValid);
        Assert.Contains((await validator.ValidateAsync(new InstructorUpdateViewModel(Name: " "), Ct)).Errors, error => error.PropertyName == "Name");
    }

    [Fact]
    public async Task AnUpdateCannotBothSetAndRemoveThePortrait()
    {
        var result = await new InstructorUpdateValidator().ValidateAsync(
            new InstructorUpdateViewModel(PortraitMediaId: Guid.NewGuid(), RemovePortrait: true), Ct);

        Assert.Contains(result.Errors, error => error.PropertyName == "RemovePortrait");
    }

    [Fact]
    public async Task AListLimitStaysInRangeAndTheStatusIsKnown()
    {
        var validator = new InstructorListValidator();

        Assert.True((await validator.ValidateAsync(new InstructorListViewModel(Limit: PageRequest.MaxLimit), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new InstructorListViewModel(Limit: 0), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new InstructorListViewModel(Limit: PageRequest.MaxLimit + 1), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new InstructorListViewModel(Status: (PublishStatus)7), Ct)).IsValid);
        Assert.False((await validator.ValidateAsync(new InstructorListViewModel(Search: new string('x', InstructorPolicy.SearchMaxLength + 1)), Ct)).IsValid);
    }
}
