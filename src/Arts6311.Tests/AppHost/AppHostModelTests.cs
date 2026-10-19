using Arts6311.AppHost.Parameters;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace Arts6311.Tests.AppHost;

/// <summary>
/// The AppHost's resource model (prompt 1.2), built but never started, so no container runtime is needed.
/// </summary>
/// <remarks>
/// Parameter values only resolve once the app starts, so these tests check the model's shape, never the values
/// a resource receives: awaiting one here waits forever. That the API gets only the public key is covered by
/// <see cref="GeneratedSecretTests"/> plus the run check in prompt 1.2. The signing key is supplied as
/// configuration, so building the model never generates or persists anything into user secrets.
/// </remarks>
public sealed class AppHostModelTests
{
    private const int TimeoutMs = 60_000;

    private static readonly string[] RunModeResources =
    [
        "sql", "appdb", "storage", "blobs", "media", "dataprotection", "cache", "mail", "dbgate",
        "migrations", "api", "worker",
        "sql-password", "redis-password", "internal-token-signing-key", "idempotency-fingerprint-key",
        "seed-admin-email", "seed-admin-password",
    ];

    [Fact(Timeout = TimeoutMs)]
    public async Task RunModeDeclaresEveryResourceByItsStableName()
    {
        await using var appHost = await CreateRunModeAsync(TestContext.Current.CancellationToken);

        var names = appHost.Resources.Select(resource => resource.Name).ToHashSet(StringComparer.Ordinal);

        Assert.All(RunModeResources, name => Assert.Contains(name, names));
    }

    [Fact(Timeout = TimeoutMs)]
    public async Task OneDbGateServesBothSqlAndRedis()
    {
        await using var appHost = await CreateRunModeAsync(TestContext.Current.CancellationToken);

        Assert.Single(appHost.Resources, resource => resource.Name == "dbgate");
    }

    [Fact(Timeout = TimeoutMs)]
    public async Task MigrationsRunAgainstAppdbAndGateTheApiAndWorker()
    {
        await using var appHost = await CreateRunModeAsync(TestContext.Current.CancellationToken);

        Assert.True(WaitsFor(appHost, "migrations", "appdb", WaitType.WaitUntilHealthy));
        Assert.True(WaitsFor(appHost, "api", "migrations", WaitType.WaitForCompletion));
        Assert.True(WaitsFor(appHost, "worker", "migrations", WaitType.WaitForCompletion));
    }

    private static bool WaitsFor(IDistributedApplicationTestingBuilder appHost, string resource, string dependency, WaitType waitType) =>
        appHost.Resources.Single(candidate => candidate.Name == resource)
            .Annotations.OfType<WaitAnnotation>()
            .Any(wait => wait.Resource.Name == dependency && wait.WaitType == waitType);

    private static Task<IDistributedApplicationTestingBuilder> CreateRunModeAsync(CancellationToken cancellationToken) =>
        DistributedApplicationTestingBuilder.CreateAsync<Projects.Arts6311_AppHost>(
            [$"--Parameters:internal-token-signing-key={new EcdsaP256PrivateKeyDefault().GetDefaultValue()}"],
            cancellationToken);
}
