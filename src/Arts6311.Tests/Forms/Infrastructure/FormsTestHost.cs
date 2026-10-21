using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Email;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Forms;
using Arts6311.Domain.Modules.Forms.Data.Entities;
using Arts6311.Domain.Modules.Forms.Facade;
using Arts6311.Domain.Modules.Forms.Managers.ViewModels;
using Arts6311.Domain.Modules.Media;
using Arts6311.Domain.Modules.Media.Data.Entities;
using Arts6311.Domain.Modules.Media.Facade;
using Arts6311.Domain.Modules.Media.Gateways;
using Arts6311.Tests.Kernel;
using Arts6311.Tests.Media.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace Arts6311.Tests.Forms.Infrastructure;

/// <summary>
/// The Forms module as the API and Worker compose it — kernel, Media and Forms, with the four email composers — over a
/// SQLite <see cref="ArtsDbContext"/>, with only the blob container faked.
/// </summary>
internal sealed class FormsTestHost : IAsyncDisposable
{
    public const string EditorId = "editor-1";

    public const string AdminId = "admin-1";

    public const string StaffAddress = "staff@6311arts.localhost";

    public const string SiteOrigin = "https://localhost:7443";

    private readonly SqliteArtsDatabase database;
    private readonly ServiceProvider services;

    private FormsTestHost(SqliteArtsDatabase database, ServiceProvider services, FakeTimeProvider time)
    {
        this.database = database;
        this.services = services;
        Time = time;
    }

    public FakeTimeProvider Time { get; }

    public static async Task<FormsTestHost> CreateAsync(CancellationToken cancellationToken, IInterceptor? interceptor = null)
    {
        var database = await SqliteArtsDatabase.CreateAsync(cancellationToken);
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 21, 12, 0, 0, TimeSpan.Zero));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Forms:StaffAddress"] = StaffAddress,
                ["Email:FromAddress"] = "no-reply@6311arts.test",
                ["Email:SiteOrigin"] = SiteOrigin,
            })
            .Build();

        var collection = new ServiceCollection();
        collection.AddLogging(logging => logging.SetMinimumLevel(LogLevel.Warning));
        collection.AddSingleton<TimeProvider>(time);
        collection.AddScoped(_ => interceptor is null ? database.NewContext() : database.NewContext(interceptor));
        collection.AddArtsKernel();
        collection.AddMediaModule();
        collection.AddFormsModule(configuration);
        collection.Configure<EmailOptions>(configuration.GetSection(EmailOptions.SectionName));
        collection.Replace(ServiceDescriptor.Singleton<IMediaBlobGateway>(new FakeMediaBlobGateway()));

        return new FormsTestHost(
            database, collection.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true }), time);
    }

    public async Task<T> WithFacadeAsync<T>(Func<IFormsFacade, Task<T>> action)
    {
        using var scope = services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IFormsFacade>());
    }

    public async Task<T> WithMediaAsync<T>(Func<IMediaFacade, Task<T>> action)
    {
        using var scope = services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<IMediaFacade>());
    }

    /// <summary>A submission with these pictures, sent as the controller sends them: one stream at a time.</summary>
    public Task<OperationResult> SubmitAsync(ArtistSubmissionViewModel submission, params (string FileName, byte[] Bytes)[] images) =>
        WithFacadeAsync(facade => facade.SubmitAsync(submission, Stream(images), CancellationToken.None));

    /// <summary>The email a kind's composer writes for a subject, as the Worker's handler would ask for it.</summary>
    public async Task<EmailMessage?> ComposeAsync(string kind, string subjectId)
    {
        using var scope = services.CreateScope();
        return await scope.ServiceProvider.GetRequiredKeyedService<IEmailComposer>(kind).ComposeAsync(subjectId, CancellationToken.None);
    }

    public async Task<T> QueryAsync<T>(Func<ArtsDbContext, Task<T>> query)
    {
        await using var context = database.NewContext();
        return await query(context);
    }

    public Task<List<ArtistSubmission>> SubmissionsAsync() =>
        QueryAsync(context => context.Set<ArtistSubmission>().AsNoTracking().Include(submission => submission.Images).ToListAsync());

    public Task<List<ContactMessage>> ContactsAsync() =>
        QueryAsync(context => context.Set<ContactMessage>().AsNoTracking().ToListAsync());

    public Task<List<NewsletterSubscriber>> SubscribersAsync() =>
        QueryAsync(context => context.Set<NewsletterSubscriber>().AsNoTracking().ToListAsync());

    public Task<List<MediaAsset>> OriginalsAsync() =>
        QueryAsync(context => context.Set<MediaAsset>().AsNoTracking()
            .Where(asset => asset.ParentAssetId == null).ToListAsync());

    /// <summary>The <c>email.send-requested</c> messages staged so far, as (kind, subject id).</summary>
    public async Task<List<(string Kind, string SubjectId)>> EmailsAsync()
    {
        var payloads = await QueryAsync(context => context.OutboxMessages.AsNoTracking()
            .Where(message => message.Type == EmailSendRequested.MessageType)
            .Select(message => message.PayloadJson)
            .ToListAsync());
        return [.. payloads.Select(payload => EmailSendRequested.TryParse(payload)!).Select(request => (request.Kind, request.SubjectId))];
    }

    public Task<List<OutboxMessage>> OutboxAsync() =>
        QueryAsync(context => context.OutboxMessages.AsNoTracking().ToListAsync());

    public Task<List<(string Action, string Summary, string ResourceId)>> AuditAsync() =>
        QueryAsync(async context => (await context.AuditLogs.AsNoTracking()
                .Select(entry => new { entry.Action, entry.Summary, entry.ResourceId })
                .ToListAsync())
            .Select(entry => (entry.Action, entry.Summary, entry.ResourceId))
            .ToList());

    public async ValueTask DisposeAsync()
    {
        await services.DisposeAsync();
        await database.DisposeAsync();
    }

    private static async IAsyncEnumerable<SubmissionImageViewModel> Stream((string FileName, byte[] Bytes)[] images)
    {
        foreach (var (fileName, bytes) in images)
        {
            await Task.Yield();
            yield return new SubmissionImageViewModel(fileName, new MemoryStream(bytes));
        }
    }
}

/// <summary>Valid answers to build on, the way the site's form sends them.</summary>
internal static class FormsSamples
{
    public static ArtistSubmissionViewModel Submission(
        string name = "Jane Rivera",
        string email = "jane@example.org",
        IReadOnlyList<string>? wants = null,
        bool newsletter = false,
        string? company = null) =>
        new(
            new SubmissionArtistViewModel(name, email, Pronouns: "she", Neighborhood: "Rogers Park", Zip: "60626",
                Website: "https://jane.example.org", Instagram: "@janerivera"),
            new SubmissionWorkViewModel("Printmaking", Years: 8, PriceBand: "$100 – $300", Bio: "I print.", Statement: "Linocuts of the lake."),
            wants ?? ["sell", "teach"],
            Sell: new SubmissionSellViewModel(12, "limited"),
            Exhibit: new SubmissionExhibitViewModel("Left out", 3),
            Teach: new SubmissionTeachViewModel("Intro to drypoint", 10, "Five-week course", "Plates, ink", true),
            Portfolio: "https://portfolio.example.org",
            Heard: "Instagram",
            Consent: new SubmissionConsentViewModel(OwnWork: true, Newsletter: newsletter),
            Company: company);
}
