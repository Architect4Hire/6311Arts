using System.Data.Common;
using System.Diagnostics;
using Arts6311.Domain.Managers;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Persistence;
using Arts6311.Tests.Kernel;
using Arts6311.Worker;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace Arts6311.Tests.Worker;

/// <summary>
/// The noise fix must not cost the trace: a real pass, real dispatcher and real (SQLite) database under the Worker's
/// sampler. A command interceptor stands in for SqlClient's instrumentation, starting a span for every command.
/// </summary>
public sealed class OutboxPollTracingTests : IAsyncLifetime
{
    private const string MessageType = "test.traced-requested";

    private readonly FakeTimeProvider time = new(new DateTimeOffset(2026, 10, 19, 18, 0, 0, TimeSpan.Zero));
    private readonly ActivitySource sql = new($"test.sql.{Guid.NewGuid():N}");
    private readonly ActivitySource work = new($"test.work.{Guid.NewGuid():N}");
    private readonly List<Activity> exported = [];
    private SqliteArtsDatabase database = null!;
    private SqlSpans spans = null!;
    private ServiceProvider services = null!;
    private TracerProvider tracing = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        database = await SqliteArtsDatabase.CreateAsync(Ct);
        spans = new SqlSpans(sql);
        services = new ServiceCollection()
            .AddSingleton<TimeProvider>(time)
            .AddScoped(_ => database.NewContext(spans))
            .AddArtsKernel()
            .AddKeyedScoped<IOutboxMessageHandler>(MessageType, (_, _) => new WorkingHandler(work))
            .BuildServiceProvider(validateScopes: true);

        // As Worker/Program.cs configures it.
        tracing = Sdk.CreateTracerProviderBuilder()
            .AddSource(OutboxDiagnostics.ActivitySourceName, sql.Name, work.Name)
            .SetSampler(new ParentBasedSampler(new AlwaysOnSampler()))
            .AddProcessor(new Capture(exported))
            .Build();
    }

    public async ValueTask DisposeAsync()
    {
        tracing.Dispose();
        sql.Dispose();
        work.Dispose();
        await services.DisposeAsync();
        await database.DisposeAsync();
    }

    [Fact]
    public async Task APassExportsTheHandlersSpansInTheRequestsTraceAndNothingOfItsOwn()
    {
        ActivityTraceId requestTrace;
        await using (var request = services.CreateAsyncScope())
        {
            using var span = new Activity("POST request").SetIdFormat(ActivityIdFormat.W3C);
            span.ActivityTraceFlags = ActivityTraceFlags.Recorded;
            span.Start();
            requestTrace = span.TraceId;
            request.ServiceProvider.GetRequiredService<IOutboxWriter>()
                .Enqueue(MessageType, "{}", request.ServiceProvider.GetRequiredService<ICorrelationContext>().Current);
            await request.ServiceProvider.GetRequiredService<ArtsDbContext>().SaveChangesAsync(Ct);
        }

        lock (exported)
        {
            exported.Clear(); // only the pass is under test
        }

        Activity.Current = null;
        using var service = new OutboxDispatcherHostedService(
            services.GetRequiredService<IServiceScopeFactory>(), time, NullLogger<OutboxDispatcherHostedService>.Instance);
        Assert.Equal(1, (await service.DispatchOnceAsync(Ct))?.Completed);

        List<Activity> mine;
        lock (exported)
        {
            // Other tests may dispatch in parallel; the test sources are this test's alone.
            mine = [.. exported.Where(activity =>
                activity.TraceId == requestTrace || activity.Source == sql || activity.Source == work)];
        }

        var process = Assert.Single(mine, activity => activity.Source.Name == OutboxDiagnostics.ActivitySourceName);
        Assert.Equal(requestTrace, process.TraceId);

        // The handler's own span, and the outcome UPDATE, are children of the process span...
        var handlerSpan = Assert.Single(mine, activity => activity.Source == work);
        Assert.Equal(process.SpanId, handlerSpan.ParentSpanId);
        var outcome = Assert.Single(mine, activity => activity.Source == sql);
        Assert.Equal("UPDATE", outcome.DisplayName);
        Assert.Equal(process.SpanId, outcome.ParentSpanId);

        // ...and the pass's own SQL — the two due reads and the claim UPDATE — exported nothing at all.
        Assert.Equal(3, mine.Count);
        Assert.Equal(4, spans.Started);
    }

    private sealed class WorkingHandler(ActivitySource work) : IOutboxMessageHandler
    {
        public Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken)
        {
            using var span = work.StartActivity("handler work");
            return Task.CompletedTask;
        }
    }

    /// <summary>Starts (and ends) one span per SQL command, as SqlClient instrumentation would, under the current activity.</summary>
    private sealed class SqlSpans(ActivitySource source) : DbCommandInterceptor
    {
        private int started;

        public int Started => Volatile.Read(ref started);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Span(command);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Span(command);
            return ValueTask.FromResult(result);
        }

        private void Span(DbCommand command)
        {
            // Only the dispatcher's commands (the enqueue's INSERT is before the clear, and not counted either).
            var verb = command.CommandText.TrimStart().Split(' ', 2)[0].ToUpperInvariant();
            if (verb == "INSERT")
            {
                return;
            }

            Interlocked.Increment(ref started);
            using var span = source.StartActivity(verb);
        }
    }

    private sealed class Capture(List<Activity> exported) : BaseProcessor<Activity>
    {
        public override void OnEnd(Activity data)
        {
            lock (exported)
            {
                exported.Add(data);
            }
        }
    }
}
