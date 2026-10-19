using Arts6311.AppHost.Parameters;
using Aspire.Hosting;
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
        "migrations", "api", "gateway", "worker",
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
    public async Task OnlyTheGatewayIsPubliclyReachable()
    {
        // ADR 0001: one origin. Everything the browser sees comes through the gateway, which is what makes the
        // session cookie, antiforgery, CSP and the Square webhook signature all mean something. A second external
        // endpoint would quietly undo all of it, so this is asserted on the model rather than trusted to review.
        await using var appHost = await CreateRunModeAsync(TestContext.Current.CancellationToken);

        var external = appHost.Resources
            .Where(resource => resource.Annotations
                .OfType<EndpointAnnotation>()
                .Any(endpoint => endpoint.IsExternal))
            .Select(resource => resource.Name)
            .ToList();

        Assert.Equal(["gateway"], external);
    }

    [Fact(Timeout = TimeoutMs)]
    public async Task TheGatewayWaitsForTheApiAndTheCache()
    {
        // It proxies to the API and will keep session tickets in Redis (1.10), so starting before either is ready
        // would mean a window of 503s on every request.
        await using var appHost = await CreateRunModeAsync(TestContext.Current.CancellationToken);

        var gateway = Assert.Single(appHost.Resources, resource => resource.Name == "gateway");
        var waitsFor = gateway.Annotations
            .OfType<WaitAnnotation>()
            .Select(annotation => annotation.Resource.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("api", waitsFor);
        Assert.Contains("cache", waitsFor);
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

    /// <summary>The API gets the fingerprint key from its parameter; the Worker, whose sweep needs none, gets nothing.</summary>
    [Fact(Timeout = TimeoutMs)]
    public async Task TheIdempotencyFingerprintKeyGoesToTheApiOnly()
    {
        await using var appHost = await CreateRunModeAsync(TestContext.Current.CancellationToken);

        var api = await EnvironmentOfAsync(appHost, "api");
        var worker = await EnvironmentOfAsync(appHost, "worker");

        var key = Assert.IsType<ParameterResource>(api["Idempotency__FingerprintKey"]);
        Assert.Equal("idempotency-fingerprint-key", key.Name);
        Assert.True(key.Secret);
        Assert.DoesNotContain(worker.Keys, name => name.StartsWith("Idempotency__", StringComparison.Ordinal));
    }

    /// <summary>
    /// Runs a resource's environment callbacks without resolving any value, so no secret is generated or read: the
    /// entries are the parameter and expression objects themselves.
    /// </summary>
    /// <remarks>
    /// A callback that awaits a value (the API's internal-token key derivation) would wait forever in a model that
    /// never starts (see the class remarks), so each callback gets a short deadline and one that blocks is skipped.
    /// Reference-style callbacks only place objects and finish at once.
    /// </remarks>
    private static async Task<Dictionary<string, object>> EnvironmentOfAsync(
        IDistributedApplicationTestingBuilder appHost, string resourceName)
    {
        var resource = appHost.Resources.Single(candidate => candidate.Name == resourceName);
        var variables = new Dictionary<string, object>();

        foreach (var callback in resource.Annotations.OfType<EnvironmentCallbackAnnotation>())
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));
            var context = new EnvironmentCallbackContext(
                new DistributedApplicationExecutionContext(DistributedApplicationOperation.Run), variables, deadline.Token);

            try
            {
                await callback.Callback(context);
            }
            catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                // Waits on a parameter value; not what these tests inspect.
            }
        }

        return variables;
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
