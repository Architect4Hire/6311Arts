using System.Security.Cryptography;
using Arts6311.AppHost.Parameters;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Extensions.Configuration;

namespace Arts6311.Tests.EndToEnd;

/// <summary>
/// The whole AppHost, started once for the end-to-end tests (prompt 1.12): real containers, real hosts, real
/// service discovery, from empty volumes.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Hermetic by construction.</strong> Every parameter the AppHost would generate is supplied fresh per run,
/// as host configuration rather than command-line arguments, so the values never appear in a process list. Startup
/// then proves each parameter took the test's value: one that had not would have been generated and persisted to this
/// machine's user secrets. <see cref="EphemeralAppModel"/> strips volumes and persistent lifetimes, so the run starts
/// from empty storage and never touches the developer's own <c>aspire run</c> containers. <c>Square:Mode</c> is
/// pinned to <c>fake</c> so a developer's sandbox setting cannot reach the test.
/// </para>
/// <para>
/// Needs a container runtime. Tagged <c>Category=E2E</c> so CI can run it as its own job.
/// </para>
/// </remarks>
public sealed class PlatformFixture : IAsyncLifetime
{
    /// <summary>First pulls of the SQL Server image dominate this on a cold CI runner.</summary>
    public static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(10);

    public const string AdminEmail = "e2e-admin@6311arts.test";

    private DistributedApplication? app;

    /// <summary>Generated per run: long enough for the staff rule and certainly not on the breached list.</summary>
    public string AdminSecret { get; } = $"e2e-{Guid.NewGuid():N}";

    public DistributedApplication App => app ?? throw new InvalidOperationException("The platform did not start.");

    /// <summary>The model's non-parameter resources, for the health table.</summary>
    public IReadOnlyList<string> ResourceNames { get; private set; } = [];

    public async ValueTask InitializeAsync()
    {
        using var startup = new CancellationTokenSource(StartupTimeout);
        var cancellationToken = startup.Token;

        // Every parameter the AppHost would otherwise generate and persist, plus the seed admin's address.
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["sql-password"] = SqlServerCompliantSecret(),
            ["redis-password"] = Guid.NewGuid().ToString("N"),
            ["internal-token-signing-key"] = new EcdsaP256PrivateKeyDefault().GetDefaultValue(),
            ["idempotency-fingerprint-key"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            ["seed-admin-email"] = AdminEmail,
            ["seed-admin-password"] = AdminSecret,
        };

        // Square:Mode decides which resources exist, so it is read while the AppHost composes the model and has to be
        // an argument. It is not a secret.
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Arts6311_AppHost>(
            ["--Square:Mode=fake"],
            (options, _) => options.DisableDashboard = true,
            cancellationToken);

        // Parameters resolve when the app starts, so values added now — after the model, before the start — are the
        // ones used, and as the last configuration source they win over anything in user secrets.
        builder.Configuration.AddInMemoryCollection(parameters
            .Select(parameter => KeyValuePair.Create<string, string?>($"Parameters:{parameter.Key}", parameter.Value)));

        EphemeralAppModel.Apply(builder);

        ResourceNames =
        [
            .. builder.Resources
                .Where(resource => resource is not ParameterResource)
                .Select(resource => resource.Name),
        ];

        var parameterResources = builder.Resources.OfType<ParameterResource>()
            .Where(resource => parameters.ContainsKey(resource.Name))
            .ToList();

        app = await builder.BuildAsync(cancellationToken);
        await app.StartAsync(cancellationToken);

        await ProveParametersCameFromTheTestAsync(parameters, parameterResources, cancellationToken);
        await WaitForPlatformAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (app is not null)
        {
            await app.DisposeAsync();
        }
    }

    /// <summary>The latest state of every resource, for the health table and its assertions.</summary>
    public async Task<IReadOnlyList<ResourceHealthRow>> SnapshotAsync(CancellationToken cancellationToken)
    {
        var notifications = App.ResourceNotifications;
        var rows = new List<ResourceHealthRow>();

        foreach (var name in ResourceNames)
        {
            // The watch replays the current state first, so "any event" is the latest one.
            var current = await notifications.WaitForResourceAsync(name, _ => true, cancellationToken);
            rows.Add(new ResourceHealthRow(
                name,
                current.Snapshot.State?.Text ?? "(none)",
                current.Snapshot.HealthStatus?.ToString() ?? "(no checks)",
                current.Snapshot.ExitCode));
        }

        return rows;
    }

    /// <summary>
    /// A parameter that did not take the test's value was generated — and, being <c>persist: true</c>, written to
    /// this machine's user secrets. Stop before anything runs against it. Names only in the message, never values.
    /// </summary>
    private static async Task ProveParametersCameFromTheTestAsync(
        Dictionary<string, string> expected,
        List<ParameterResource> resources,
        CancellationToken cancellationToken)
    {
        var missing = expected.Keys.Except(resources.Select(resource => resource.Name), StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"The model has no parameter named {string.Join(", ", missing)}.");
        }

        foreach (var resource in resources)
        {
            if (!string.Equals(
                    await resource.GetValueAsync(cancellationToken), expected[resource.Name], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Parameter '{resource.Name}' did not take the test's value, so the AppHost generated its own.");
            }
        }
    }

    private async Task WaitForPlatformAsync(CancellationToken cancellationToken)
    {
        var notifications = App.ResourceNotifications;

        // Run-once: it must finish, and finish cleanly, before api and worker are allowed to start.
        var migrations = await notifications.WaitForResourceAsync(
            "migrations",
            resource => KnownResourceStates.TerminalStates.Contains(resource.Snapshot.State?.Text),
            cancellationToken);

        if (migrations.Snapshot.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"migrations ended '{migrations.Snapshot.State?.Text}' with exit code {migrations.Snapshot.ExitCode}.");
        }

        foreach (var name in new[] { "sql", "cache", "storage", "mail", "api", "worker", "site", "admin", "gateway" })
        {
            await notifications.WaitForResourceHealthyAsync(name, cancellationToken);
        }
    }

    /// <summary>SQL Server's complexity rule wants upper, lower, a digit and a symbol.</summary>
    private static string SqlServerCompliantSecret() => $"Aa1!{Guid.NewGuid():N}";
}

public sealed record ResourceHealthRow(string Name, string State, string Health, int? ExitCode);
