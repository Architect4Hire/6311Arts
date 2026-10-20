using System.Security.Cryptography;
using Arts6311.Gateway;
using Arts6311.Gateway.Session;
using Arts6311.Tests.Browser;
using Arts6311.Tests.Infrastructure;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Time.Testing;

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

        // AddRedisClient needs a connection string to compose. Nothing ever dials it: the ticket store is
        // replaced below, and so is the Data Protection ring that would otherwise reach for the multiplexer.
        settings["ConnectionStrings:cache"] = "localhost:6379,abortConnect=false";

        // https, not the default http. The session and antiforgery cookies are Secure and __Host- prefixed, and
        // a CookieContainer will not send a Secure cookie over http — so over http every session test would
        // silently exercise an anonymous request.
        ClientOptions.BaseAddress = new Uri("https://localhost");

        Sessions = new InMemorySessionStore(Time);
    }

    /// <summary>The clock the gateway runs on, so a test can reach an idle or absolute limit without waiting.</summary>
    public FakeTimeProvider Time { get; } = new();

    /// <summary>Redis, in a dictionary. Its <c>Keys</c> is how a test asserts a ticket is really gone.</summary>
    public InMemorySessionStore Sessions { get; }

    /// <summary>The internal API the BFF calls.</summary>
    public FakeIdentityApi Identity { get; } = new();

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

        builder.ConfigureTestServices(services =>
        {
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(Time));
            services.Replace(ServiceDescriptor.Singleton<ISessionKeyValueStore>(Sessions));
            services.Replace(ServiceDescriptor.Singleton<IIdentityApiClient>(Identity));

            // The real ring is in Redis (ADR 0024), and the framework initializes it eagerly at startup, so
            // without this every host here would spend seconds failing to reach a Redis that is not running.
            // Everything else about Data Protection stays real, because antiforgery depends on it.
            services.Configure<KeyManagementOptions>(options => options.XmlRepository = new InMemoryXmlRepository());
        });
    }

    /// <summary>
    /// A client that fetches an antiforgery token before its first unsafe request, as a browser does, and keeps
    /// the cookies it is given.
    /// </summary>
    public BffClient CreateBffClient() => new(CreateJsonClient());

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
