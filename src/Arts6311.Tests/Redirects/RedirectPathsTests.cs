using Arts6311.Domain.Modules.Redirects.Managers;

namespace Arts6311.Tests.Redirects;

/// <summary>What an address on a redirect may be, and its one normal form.</summary>
public sealed class RedirectPathsTests
{
    [Theory]
    [InlineData("/gallery", "/gallery")]
    [InlineData(" /Gallery/ ", "/gallery")]
    [InlineData("/Artist/Jane-Doe/page/2/", "/artist/jane-doe/page/2")]
    [InlineData("/?p=123", "/?p=123")]
    [InlineData("/index.php?p=123", "/index.php?p=123")]
    [InlineData("/shop?Category=Prints", "/shop?Category=Prints")]
    [InlineData("/events?", "/events")]
    public void AnOldAddressHasOneNormalForm(string input, string? expected)
    {
        Assert.Equal(expected, RedirectPaths.NormalizeFrom(input).Path);
    }

    [Theory]
    [InlineData("")]
    [InlineData("gallery")]
    [InlineData("//evil.example/x")]
    [InlineData("/a\\b")]
    [InlineData("/a b")]
    [InlineData("/gallery#top")]
    [InlineData("/")]
    [InlineData("/a/../admin")]
    [InlineData("/./x")]
    [InlineData("https://6311arts.com/gallery")]
    [InlineData("/admin")]
    [InlineData("/admin/artists")]
    [InlineData("/API/v1/site/redirects")]
    [InlineData("/bff/login")]
    [InlineData("/media/0199f0a0-0000-7000-8000-0000000000aa/800.webp")]
    [InlineData("/webhooks/square")]
    [InlineData("/signin")]
    [InlineData("/health")]
    public void AnOldAddressMayNotBeAnythingElse(string input)
    {
        var (path, error) = RedirectPaths.NormalizeFrom(input);

        Assert.Null(path);
        Assert.NotNull(error);
    }

    [Fact]
    public void AReservedPrefixIsAPathSegmentNotAPrefixOfText()
    {
        Assert.Equal("/administration", RedirectPaths.NormalizeFrom("/administration").Path);
        Assert.Equal("/apiary", RedirectPaths.NormalizeFrom("/apiary").Path);
    }

    [Theory]
    [InlineData("/Classes/", "/classes")]
    [InlineData("/", "/")]
    [InlineData("/visit#Hours", "/visit#Hours")]
    [InlineData("/shop?artist=piloto", "/shop?artist=piloto")]
    [InlineData("https://squareup.com/store/6311arts", "https://squareup.com/store/6311arts")]
    [InlineData("HTTPS://Example.com", "https://example.com/")]
    [InlineData("/signin", "/signin")]
    public void ADestinationIsASitePathOrAnHttpsAddress(string input, string expected)
    {
        Assert.Equal(expected, RedirectPaths.NormalizeTo(input).Path);
    }

    [Theory]
    [InlineData("http://insecure.example/")]
    [InlineData("https://user:pass@evil.example/")]
    [InlineData("javascript:alert(1)")]
    [InlineData("mailto:info@6311arts.com")]
    [InlineData("/api/v1/cms/artists")]
    [InlineData("/bff/logout")]
    [InlineData("/webhooks/square")]
    [InlineData("//evil.example")]
    public void ADestinationMayNotBeAnythingElse(string input)
    {
        Assert.Null(RedirectPaths.NormalizeTo(input).Path);
    }

    [Fact]
    public void ADestinationsTargetIsItsSiteAddressWithoutTheFragment()
    {
        Assert.Equal("/visit", RedirectPaths.TargetOf("/visit#hours"));
        Assert.Equal("/shop?x=1", RedirectPaths.TargetOf("/shop?x=1"));
        Assert.Null(RedirectPaths.TargetOf("https://squareup.com/"));
        Assert.Equal("/new#hours", RedirectPaths.Retarget("/old#hours", "/new"));
    }
}
