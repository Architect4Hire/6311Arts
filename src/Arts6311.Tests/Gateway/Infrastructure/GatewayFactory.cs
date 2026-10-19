using System.Security.Cryptography;
using Arts6311.Gateway;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arts6311.Tests.Gateway.Infrastructure;

/// <summary>
/// The real gateway host in memory: the whole pipeline from <c>Program.cs</c>, with the route table from its own
/// <c>appsettings.json</c>, so the order of the middleware and the shape of the routes are exercised as shipped.
/// </summary>
/// <remarks>
/// Configured through <c>UseSetting</c> rather than <c>ConfigureAppConfiguration</c>: <c>Program.cs</c> reads the
/// gateway options and the signing key while composing services, before <c>builder.Build()</c>, and
/// <c>ConfigureAppConfiguration</c> callbacks do not run until then (established in 1.7).
/// </remarks>
internal sealed class GatewayFactory : WebApplicationFactory<GatewayHost>
{
    private readonly Dictionary<string, string?> settings = new(StringComparer.Ordinal);
    private readonly string environmentName;

    /// <param name="destination">
    /// Pointed at as the <c>api</c> service, so service discovery resolves the cluster to it. Omit to leave the
    /// cluster unresolvable, which is what <c>/</c> and <c>/admin</c> look like until 2.2/3.3.
    /// </param>
    public GatewayFactory(StubDestination? destination = null, string? environmentName = null)
    {
        this.environmentName = environmentName ?? Environments.Development;

        if (destination is not null)
        {
            // The configuration-based service endpoint provider's shape (Services:<name>:<scheme>:<index>). The
            // AppHost sets the same keys from WithReference, as services__api__http__0.
            settings["Services:api:http:0"] = destination.Address;
        }
    }

    /// <summary>The signing key the gateway uses. Its public half is what <see cref="PublicKeyPem"/> returns.</summary>
    public ECDsa SigningKey { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    /// <summary>The half the API would hold, for the contract test that signs here and verifies there.</summary>
    public string PublicKeyPem => SigningKey.ExportSubjectPublicKeyInfoPem();

    /// <summary>Overrides one configuration value, for the CSP and rate-limit tests.</summary>
    public GatewayFactory With(string key, string? value)
    {
        settings[key] = value;
        return this;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(environmentName);
        builder.UseSetting("InternalToken:SigningKeyPem", SigningKey.ExportPkcs8PrivateKeyPem());

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }

    public HttpClient CreateJsonClient()
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        return client;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SigningKey.Dispose();
        }

        base.Dispose(disposing);
    }
}
