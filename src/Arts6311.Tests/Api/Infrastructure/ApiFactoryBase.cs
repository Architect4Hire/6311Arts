using Arts6311.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Api.Infrastructure;

/// <summary>
/// The real API host in memory: the whole pipeline from <c>Program.cs</c>, so authentication, authorization,
/// <c>ProblemDetails</c> and the OpenAPI document are exercised as configured rather than re-assembled for tests.
/// </summary>
/// <remarks>
/// <para>
/// No database is needed: nothing under test reaches SQL. The connection string is present but points nowhere and
/// carries no credential, which is enough — <c>AddSqlServerDbContext</c> requires one to exist at startup and
/// never opens it until a repository does. A test that needs rows uses the SQLite kernel fixture instead.
/// </para>
/// <para>
/// The type argument is <see cref="ApiHost"/> rather than <c>Program</c>: this project and
/// <c>Arts6311.AppHost</c> both use top-level statements, so each has a <c>Program</c> in the global namespace and
/// the test project references both.
/// </para>
/// </remarks>
internal abstract class ApiFactoryBase : WebApplicationFactory<ApiHost>
{
    /// <summary>Stands in for the gateway, which only learns to sign in prompt 1.8. Disposed with the factory.</summary>
    public InternalTokenFactory Tokens { get; } = new();

    protected abstract string EnvironmentName { get; }

    /// <summary>The gateway public key the API should trust. Defaults to <see cref="Tokens"/>' key.</summary>
    protected virtual string PublicKeyPem => Tokens.PublicKeyPem;

    protected virtual void ConfigureTestServices(IServiceCollection services)
    {
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseEnvironment(EnvironmentName);

        // UseSetting, not ConfigureAppConfiguration: Program.cs reads these while composing services, before
        // builder.Build(), and ConfigureAppConfiguration callbacks do not run until Build(). Only host
        // configuration is in place early enough.
        builder.UseSetting(
            "ConnectionStrings:appdb", "Server=localhost,1;Database=arts6311-tests;Encrypt=false;Connect Timeout=1");

        // What the AppHost passes as InternalToken__PublicKeyPem (ADR 0002).
        builder.UseSetting("InternalToken:PublicKeyPem", PublicKeyPem);

        // 32 zero bytes. The fingerprint HMAC is not under test here, only that startup validation passes.
        builder.UseSetting("Idempotency:FingerprintKey", Convert.ToBase64String(new byte[32]));

        // The Data Protection key ring's store. Parsable so the BlobServiceClient can be constructed; never
        // reached, because the key ring is replaced below.
        builder.UseSetting("ConnectionStrings:blobs", "UseDevelopmentStorage=true");

        builder.ConfigureServices(services =>
        {
            // An in-memory key ring, so no test depends on Azurite being up. Production needs the blob-backed
            // one because the Worker mints Identity's tokens and the API validates them in a different process
            // (ADR 0023) — that wiring is verified under `aspire run`, not here.
            services.AddDataProtection().UseEphemeralDataProtectionProvider();

            ConfigureTestServices(services);
        });
    }

    /// <summary>
    /// A client that asks for JSON. <c>ProblemDetails</c> is only written when the <c>Accept</c> header allows it,
    /// so a client without one would hide the bodies these tests assert on.
    /// </summary>
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
            Tokens.Dispose();
        }

        base.Dispose(disposing);
    }
}
