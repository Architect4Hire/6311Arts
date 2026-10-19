using Microsoft.Extensions.Hosting;

namespace Arts6311.Tests.Api.Infrastructure;

/// <summary>
/// The same host outside Development, for the things that must <em>not</em> exist there: the OpenAPI document, and
/// the 1.5 ping scaffolding.
/// </summary>
internal sealed class ProductionApiFactory : ApiFactoryBase
{
    protected override string EnvironmentName => Environments.Production;
}
