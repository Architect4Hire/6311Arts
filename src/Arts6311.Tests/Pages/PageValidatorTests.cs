using Arts6311.Domain.Managers.Validation;
using Arts6311.Domain.Modules.Pages.Managers;
using Arts6311.Domain.Modules.Pages.Managers.Validation;
using Arts6311.Domain.Modules.Pages.Managers.ViewModels;

namespace Arts6311.Tests.Pages;

/// <summary>Shape only: the body's content is Business's, once sanitized.</summary>
public sealed class PageValidatorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ACreateNeedsATitleWithSomethingToMakeASlugFrom()
    {
        Assert.Null(await FieldsAsync(new PageCreateViewModel("Privacy Policy")));
        Assert.Equal(["title"], await FieldsAsync(new PageCreateViewModel(" ")));
        Assert.Equal(["title"], await FieldsAsync(new PageCreateViewModel("—")));
        Assert.Null(await FieldsAsync(new PageCreateViewModel("—", Slug: "dash")));
        Assert.Equal(["slug"], await FieldsAsync(new PageCreateViewModel("Fine", Slug: "Not A Slug")));
    }

    [Fact]
    public async Task TheRawBodyIsCappedGenerouslyBeforeSanitizing()
    {
        Assert.Null(await FieldsAsync(new PageCreateViewModel("Big", Body: new string('x', PagePolicy.BodyInputMaxLength))));
        Assert.Equal(["body"], await FieldsAsync(new PageCreateViewModel("Big", Body: new string('x', PagePolicy.BodyInputMaxLength + 1))));
    }

    [Fact]
    public async Task AnEditMustChangeSomethingAndAnEmptyBodyIsAChange()
    {
        var validator = new PageUpdateValidator();

        Assert.NotNull(await validator.ValidateOrErrorAsync(new PageUpdateViewModel(), Ct));
        Assert.Null(await validator.ValidateOrErrorAsync(new PageUpdateViewModel(Body: ""), Ct));
        Assert.NotNull(await validator.ValidateOrErrorAsync(new PageUpdateViewModel(Title: " "), Ct));
    }

    private static async Task<List<string>?> FieldsAsync(PageCreateViewModel create) =>
        (await new PageCreateValidator().ValidateOrErrorAsync(create, Ct))?.FieldErrors.Keys.ToList();
}
