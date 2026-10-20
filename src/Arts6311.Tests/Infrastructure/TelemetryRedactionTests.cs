namespace Arts6311.Tests.Infrastructure;

/// <summary>
/// ServiceDefaults keeps a credential carried in a URL out of traces: a preview token is in the path, because the
/// site's server reads it while rendering (ADR 0036), and `url.path` is exported and kept.
/// </summary>
public sealed class TelemetryRedactionTests
{
    [Theory]
    [InlineData("/preview/CfDJ8-token", "/preview/REDACTED")]
    [InlineData("/api/v1/site/preview/CfDJ8-token", "/api/v1/site/preview/REDACTED")]
    [InlineData("/PREVIEW/CfDJ8-token", "/PREVIEW/CfDJ8-token")]
    public void ThePathAfterPreviewIsReplaced(string path, string expected)
    {
        Assert.Equal(expected, Microsoft.Extensions.Hosting.Extensions.RedactSecretSegments(path));
    }

    [Theory]
    [InlineData("/api/v1/cms/artists/0199f0a0-0000-7000-8000-000000000001")]
    [InlineData("/api/v1/cms/preview")]
    [InlineData("/preview/")]
    [InlineData("/")]
    [InlineData("")]
    public void EveryOtherPathIsLeftAlone(string path)
    {
        // The mint route ends at /preview, with nothing after it to hide, and an ordinary route keeps its ids:
        // only a secret is replaced.
        Assert.Equal(path, Microsoft.Extensions.Hosting.Extensions.RedactSecretSegments(path));
    }

    [Fact]
    public void ANullPathSurvives()
    {
        Assert.Null(Microsoft.Extensions.Hosting.Extensions.RedactSecretSegments(null));
    }
}
