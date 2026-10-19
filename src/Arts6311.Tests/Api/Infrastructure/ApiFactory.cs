using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Arts6311.Tests.Api.Infrastructure;

/// <summary>
/// The API as it runs on a laptop. Pinned to <c>Development</c>, because that is where <c>MapOpenApi</c>, the
/// health endpoints and the 1.5 ping scaffolding exist.
/// </summary>
internal sealed class ApiFactory : ApiFactoryBase
{
    private readonly Action<IServiceCollection>? configureServices;
    private readonly string? publicKeyPem;

    /// <param name="publicKeyPem">
    /// Overrides the key the API trusts. A test proving a foreign key is refused passes a different one; a test
    /// proving the host will not start without one passes empty.
    /// </param>
    /// <param name="configureServices">Replaces a service, for example <c>TimeProvider</c> with a fake.</param>
    public ApiFactory(string? publicKeyPem = null, Action<IServiceCollection>? configureServices = null)
    {
        this.publicKeyPem = publicKeyPem;
        this.configureServices = configureServices;
    }

    protected override string EnvironmentName => Environments.Development;

    protected override string PublicKeyPem => publicKeyPem ?? base.PublicKeyPem;

    protected override void ConfigureTestServices(IServiceCollection services) => configureServices?.Invoke(services);
}
