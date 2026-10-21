using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Redirects.Managers.Validation;
using Arts6311.Domain.Modules.Redirects.Managers.ViewModels;
using FluentValidation;

namespace Arts6311.Tests.Redirects;

/// <summary>
/// The redirect validators' own boundaries.
/// </summary>
/// <remarks>
/// Worth having directly: <c>RedirectsEndpointTests</c> runs against a recording fake facade, so the real
/// validator never executes on the HTTP path, and the 301/302 allow-list had nothing proving a 307 is refused
/// (test-gap review). Shape only — collisions, loops and chains are Business's, against the table.
/// </remarks>
public sealed class RedirectValidatorTests
{
    private static readonly RedirectCreateValidator Create = new();
    private static readonly RedirectUpdateValidator Update = new();
    private static readonly RedirectListValidator List = new();

    private static IEnumerable<string> Fields<T>(AbstractValidator<T> validator, T model) =>
        validator.Validate(model).Errors.Select(failure => failure.PropertyName).Distinct();

    [Theory]
    [InlineData(301)]
    [InlineData(302)]
    public void ThePermittedStatusCodesAreAccepted(int code)
    {
        Assert.True(Create.Validate(new RedirectCreateViewModel("/old", "/new", code)).IsValid);
        Assert.True(Update.Validate(new RedirectUpdateViewModel(StatusCode: code)).IsValid);
    }

    [Theory]
    [InlineData(307)]
    [InlineData(308)]
    [InlineData(303)]
    [InlineData(200)]
    [InlineData(404)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99_999)]
    public void EveryOtherStatusCodeIsRefused(int code)
    {
        // 307 and 308 are the modern equivalents and are deliberately *not* offered: content.md fixes the set at
        // 301 and 302, and the site's redirect middleware answers with what the row says.
        Assert.Contains("StatusCode", Fields(Create, new RedirectCreateViewModel("/old", "/new", code)));
        Assert.Contains("StatusCode", Fields(Update, new RedirectUpdateViewModel(StatusCode: code)));
    }

    [Fact]
    public void AMissingStatusCodeIsFineBecauseThePolicySuppliesTheDefault()
    {
        Assert.True(Create.Validate(new RedirectCreateViewModel("/old", "/new")).IsValid);
        Assert.Equal(301, RedirectPolicy.DefaultStatusCode);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("old")]
    [InlineData("/old#fragment")]
    [InlineData("https://6311arts.com/old")]
    [InlineData("/admin/anything")]
    [InlineData("/api/v1/site/classes")]
    [InlineData("/bff/login")]
    public void AnOldAddressARedirectCannotLeaveFromIsRefused(string from)
    {
        // The home page, a fragment, another origin and the reserved prefixes (content.md, ADR 0035). Each would
        // either shadow something the gateway answers or name an address the site never sees.
        Assert.Contains("fromPath", Fields(Create, new RedirectCreateViewModel(from, "/new")));
        Assert.Contains("fromPath", Fields(Update, new RedirectUpdateViewModel(FromPath: from)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("new")]
    [InlineData("http://6311arts.com/new")]
    [InlineData("//evil.test/new")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://user:pass@6311arts.com/new")]
    public void ADestinationARedirectCannotLeadToIsRefused(string to)
    {
        // A destination is a site path or an https:// address. A protocol-relative one, a scheme that is not
        // https, and credentials in the authority are all refused rather than normalised.
        Assert.Contains("toPath", Fields(Create, new RedirectCreateViewModel("/old", to)));
        Assert.Contains("toPath", Fields(Update, new RedirectUpdateViewModel(ToPath: to)));
    }

    [Theory]
    [InlineData("/new")]
    [InlineData("/new/deeper")]
    [InlineData("https://6311arts.com/new")]
    public void ASitePathOrAnHttpsAddressIsAccepted(string to)
    {
        Assert.True(Create.Validate(new RedirectCreateViewModel("/old", to)).IsValid);
    }

    [Fact]
    public void AnAddressAtTheLengthLimitIsAcceptedAndOnePastItIsNot()
    {
        var from = "/" + new string('a', RedirectPolicy.FromMaxLength - 1);
        var to = "/" + new string('b', RedirectPolicy.ToMaxLength - 1);

        Assert.True(Create.Validate(new RedirectCreateViewModel(from, to)).IsValid);
        Assert.Contains("fromPath", Fields(Create, new RedirectCreateViewModel(from + "a", to)));
        Assert.Contains("toPath", Fields(Create, new RedirectCreateViewModel(from, to + "b")));
    }

    [Fact]
    public void AnEditThatChangesNothingIsRefusedRatherThanSavedAsANoOp()
    {
        var errors = Update.Validate(new RedirectUpdateViewModel()).Errors;

        Assert.Equal("body", Assert.Single(errors).PropertyName);
    }

    [Fact]
    public void TheListsLimitAndSearchHaveBoundaries()
    {
        Assert.True(List.Validate(new RedirectListViewModel(Limit: 1)).IsValid);
        Assert.True(List.Validate(new RedirectListViewModel(Limit: PageRequest.MaxLimit)).IsValid);
        Assert.False(List.Validate(new RedirectListViewModel(Limit: 0)).IsValid);
        Assert.False(List.Validate(new RedirectListViewModel(Limit: PageRequest.MaxLimit + 1)).IsValid);

        Assert.True(List.Validate(new RedirectListViewModel(Search: new string('x', RedirectPolicy.SearchMaxLength))).IsValid);
        Assert.False(List.Validate(new RedirectListViewModel(Search: new string('x', RedirectPolicy.SearchMaxLength + 1))).IsValid);

        // A source that is not one of the enum's values cannot come from the admin, but it can come from a query
        // string, and an unchecked cast would read as "no filter" and list everything.
        Assert.False(List.Validate(new RedirectListViewModel(Source: (RedirectSource)99)).IsValid);
    }
}
