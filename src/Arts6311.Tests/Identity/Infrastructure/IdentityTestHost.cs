using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Email;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Modules.Identity;
using Arts6311.Domain.Modules.Identity.Data.Entities;
using Arts6311.Domain.Modules.Identity.Facade;
using Arts6311.Domain.Modules.Identity.Managers;
using Arts6311.Tests.Kernel;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Identity.Infrastructure;

/// <summary>
/// The Identity module over a SQLite <see cref="ArtsDbContext"/>, with a real <c>UserManager</c>.
/// </summary>
/// <remarks>
/// Real Identity, not a fake: password hashing, the lockout counters and the security stamp are the behaviour
/// under test, and a fake <c>UserManager</c> would only assert that the test's own idea of lockout works. The
/// store is SQLite because none of it depends on SQL Server.
/// </remarks>
internal sealed class IdentityTestHost : IAsyncDisposable
{
    private readonly SqliteArtsDatabase database;
    private readonly ServiceProvider services;

    private IdentityTestHost(
        SqliteArtsDatabase database, ServiceProvider services, FakeTimeProvider time, RecordingEmailSender email)
    {
        this.database = database;
        this.services = services;
        Time = time;
        Email = email;
    }

    public FakeTimeProvider Time { get; }

    /// <summary>Counts what the kernel's email handler actually sent, for the duplicate-delivery tests.</summary>
    public RecordingEmailSender Email { get; }

    public static async Task<IdentityTestHost> CreateAsync(
        CancellationToken cancellationToken,
        SeedAdminOptions? seedAdmin = null,
        Action<IServiceCollection>? configureServices = null)
    {
        var database = await SqliteArtsDatabase.CreateAsync(cancellationToken);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 19, 12, 0, 0, TimeSpan.Zero));

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SeedAdmin:Email"] = seedAdmin?.Email,
            ["SeedAdmin:Password"] = seedAdmin?.Password,
            ["SeedAdmin:DisplayName"] = seedAdmin?.DisplayName,
            ["Email:FromAddress"] = "no-reply@6311arts.test",
            ["Email:SiteOrigin"] = "https://6311arts.test",
        }).Build();

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddSingleton<IConfiguration>(configuration);

        // One context per scope, over the fixture's shared in-memory connection.
        collection.AddScoped(_ => database.NewContext());

        collection.AddArtsKernel();

        // Ephemeral keys: the same process mints and validates in these tests, so there is nothing to share.
        // Production needs the blob-backed ring instead, because the Worker and the API are different processes.
        collection.AddDataProtection().UseEphemeralDataProtectionProvider();

        collection.AddIdentityModule(configuration)
            .AddTokenProvider<DataProtectorTokenProvider<ArtsUser>>(TokenOptions.DefaultProvider);

        collection.AddIdentitySeeder();
        collection.AddArtsEmail(configuration);

        var email = new RecordingEmailSender();
        collection.AddSingleton<IEmailSender>(email);

        configureServices?.Invoke(collection);

        return new IdentityTestHost(database, collection.BuildServiceProvider(), time, email);
    }

    public IServiceScope CreateScope() => services.CreateScope();

    public static T Resolve<T>(IServiceScope scope) where T : notnull => scope.ServiceProvider.GetRequiredService<T>();

    /// <summary>Creates a user straight through <c>UserManager</c>, as a later admin screen would.</summary>
    public async Task<ArtsUser> CreateUserAsync(
        string email, string password, bool emailConfirmed, params string[] roles)
    {
        using var scope = CreateScope();
        var users = Resolve<UserManager<ArtsUser>>(scope);
        var roleManager = Resolve<RoleManager<ArtsRole>>(scope);

        foreach (var role in roles)
        {
            if (!await roleManager.RoleExistsAsync(role))
            {
                await roleManager.CreateAsync(new ArtsRole(role));
            }
        }

        var user = new ArtsUser
        {
            UserName = email,
            Email = email,
            DisplayName = email.Split('@')[0],
            EmailConfirmed = emailConfirmed,
            CreatedAt = Time.GetUtcNow(),
        };

        var created = await users.CreateAsync(user, password);
        Assert.True(created.Succeeded, string.Join("; ", created.Errors.Select(error => error.Description)));

        if (roles.Length > 0)
        {
            Assert.True((await users.AddToRolesAsync(user, roles)).Succeeded);
        }

        return user;
    }

    public static IIdentityFacade Facade(IServiceScope scope) => Resolve<IIdentityFacade>(scope);

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();
    }
}

/// <summary>Counts and keeps what was sent, so "exactly once" can be asserted rather than inferred.</summary>
internal sealed class RecordingEmailSender : IEmailSender
{
    private readonly List<EmailMessage> sent = [];

    public IReadOnlyList<EmailMessage> Sent
    {
        get
        {
            lock (sent)
            {
                return [.. sent];
            }
        }
    }

    /// <summary>Set to make the next send fail, for the retry and sanitisation tests.</summary>
    public Exception? Throw { get; set; }

    /// <summary>
    /// Runs inside the send, for the race test: it lets another delivery's ledger row land between this
    /// handler's check and its own record.
    /// </summary>
    public Action? OnSend { get; set; }

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        if (Throw is { } failure)
        {
            throw failure;
        }

        OnSend?.Invoke();

        lock (sent)
        {
            sent.Add(message);
        }

        return Task.CompletedTask;
    }
}
