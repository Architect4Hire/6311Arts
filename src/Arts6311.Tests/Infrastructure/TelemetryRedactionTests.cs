using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenTelemetry.Logs;

namespace Arts6311.Tests.Infrastructure;

/// <summary>
/// ServiceDefaults keeps a credential carried in a URL out of traces *and* logs: a preview token is in the path,
/// because the site's server reads it while rendering (ADR 0036), and both `url.path` and a log scope are exported
/// and kept.
/// </summary>
public sealed class TelemetryRedactionTests
{
    [Fact]
    public void LogScopesAreNotExportedSoTheRequestPathNeverCarriesAPreviewToken()
    {
        // ASP.NET Core's hosting scope holds RequestPath for the whole request, so with scopes exported every log
        // record written while serving `/api/v1/site/preview/{token}` — EF Core's "Executed DbCommand" at
        // Information among them — carried a live capability URL into the dashboard and into whatever collects
        // logs in Azure (security review). A processor cannot rewrite a scope, so the fix is not to export them;
        // TraceId and SpanId still tie each record to its trace (ADR 0019).
        var builder = Host.CreateApplicationBuilder();
        builder.AddServiceDefaults();
        using var host = builder.Build();

        var options = host.Services.GetRequiredService<IOptions<OpenTelemetryLoggerOptions>>().Value;

        Assert.False(
            options.IncludeScopes,
            "Exported log scopes carry ASP.NET Core's RequestPath, and a preview token is a path segment "
                + "(auth.md: a token is never a log line).");
        Assert.True(options.IncludeFormattedMessage);
    }

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
