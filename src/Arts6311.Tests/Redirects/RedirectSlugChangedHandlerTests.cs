using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Paging;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Redirects.Facade;
using Arts6311.Domain.Modules.Redirects.Managers;
using Arts6311.Domain.Modules.Redirects.Managers.ServiceModels;
using Arts6311.Domain.Modules.Redirects.Managers.ViewModels;
using Microsoft.Extensions.Logging.Abstractions;

namespace Arts6311.Tests.Redirects;

/// <summary>
/// How the <c>redirects.slug-changed</c> handler treats each way the facade can refuse it.
/// </summary>
/// <remarks>
/// The distinction matters more than it looks. A refusal about the addresses themselves can never succeed, so it
/// is poisoned at once; anything else — a staff edit holding the rows this message needs — has to retry, because
/// poisoning it means the rename's old address silently never redirects, and the only trace is a row on a Sync
/// screen (test-gap review: only the poison path was covered).
/// </remarks>
public sealed class RedirectSlugChangedHandlerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static OutboxMessageEnvelope Envelope(int attempt = 1) => new(
        Guid.NewGuid(),
        RedirectSlugChanged.MessageType,
        new RedirectSlugChanged("artist", Guid.NewGuid(), "/artists/jane-doe", "/artists/jane-q-doe", DateTimeOffset.UtcNow)
            .Serialize(),
        Guid.NewGuid(),
        attempt);

    private static RedirectSlugChangedHandler Handler(StubRedirectsFacade facade) =>
        new(facade, NullLogger<RedirectSlugChangedHandler>.Instance);

    [Fact]
    public async Task AConcurrencyConflictIsRetriedRatherThanPoisoned()
    {
        // The case that matters: a staff member saved a redirect while this message was in flight. Retrying reads
        // the table as it now is and applies the rename; poisoning would lose the redirect for good.
        var facade = new StubRedirectsFacade(
            OperationResult.Failure(OperationError.Of(ErrorCodes.ConcurrencyConflict, "Someone else saved first.")));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Handler(facade).HandleAsync(Envelope(), Ct));

        Assert.IsNotType<OutboxPoisonException>(thrown);
        Assert.Contains(ErrorCodes.ConcurrencyConflict, thrown.Message, StringComparison.Ordinal);
        Assert.Equal(1, facade.Calls);
    }

    [Fact]
    public async Task AValidationRefusalIsPoisonedBecauseRetryingCannotChangeIt()
    {
        var facade = new StubRedirectsFacade(
            OperationResult.Failure(OperationError.Validation("Not a redirectable address.", [("fromPath", "Reserved.")])));

        await Assert.ThrowsAsync<OutboxPoisonException>(() => Handler(facade).HandleAsync(Envelope(), Ct));
        Assert.Equal(1, facade.Calls);
    }

    [Fact]
    public async Task AThrownFailureIsReportedByTypeAndNeverByItsMessage()
    {
        // outbox.md: the dispatcher writes what it is given into LastError, which every Admin can read. A SQL
        // exception's message names the server, and a connection string names rather more than that.
        var facade = new StubRedirectsFacade(
            new InvalidOperationException("Login failed for 'sa' on sql-prod.internal,1433 (password 'hunter2')."));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => Handler(facade).HandleAsync(Envelope(), Ct));

        Assert.Equal("Applying the slug change failed (InvalidOperationException).", thrown.Message);
        Assert.DoesNotContain("sql-prod", thrown.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hunter2", thrown.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ASuccessfulApplyAsksTheFacadeOnceAndThrowsNothing()
    {
        var facade = new StubRedirectsFacade(OperationResult.Success());

        await Handler(facade).HandleAsync(Envelope(), Ct);

        Assert.Equal(1, facade.Calls);
        var change = Assert.Single(facade.Applied);
        Assert.Equal("artist", change.ContentType);
        Assert.Equal("/artists/jane-doe", change.FromPath);
        Assert.Equal("/artists/jane-q-doe", change.ToPath);
    }

    [Fact]
    public async Task AMalformedPayloadIsPoisonedWithoutAskingTheFacadeAnything()
    {
        var facade = new StubRedirectsFacade(OperationResult.Success());
        var malformed = new OutboxMessageEnvelope(
            Guid.NewGuid(), RedirectSlugChanged.MessageType, "{\"fromPath\":\"/a\"}", Guid.NewGuid(), 1);

        await Assert.ThrowsAsync<OutboxPoisonException>(() => Handler(facade).HandleAsync(malformed, Ct));
        Assert.Equal(0, facade.Calls);
    }

    /// <summary>Answers <c>ApplySlugChangeAsync</c> with one prepared outcome; every other member is unreachable here.</summary>
    private sealed class StubRedirectsFacade : IRedirectsFacade
    {
        private readonly OperationResult? result;
        private readonly Exception? throws;

        public StubRedirectsFacade(OperationResult result) => this.result = result;

        public StubRedirectsFacade(Exception throws) => this.throws = throws;

        public int Calls { get; private set; }

        public List<RedirectSlugChangeViewModel> Applied { get; } = [];

        public Task<OperationResult> ApplySlugChangeAsync(
            RedirectSlugChangeViewModel change, CancellationToken cancellationToken)
        {
            Calls++;
            Applied.Add(change);

            return throws is not null ? throw throws : Task.FromResult(result!);
        }

        public Task<OperationResult<CursorPage<RedirectServiceModel>>> ListAsync(
            RedirectListViewModel request, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationResult<IReadOnlyList<SiteRedirectServiceModel>>> ListForSiteAsync(
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationResult<RedirectServiceModel>> GetAsync(
            Guid id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<OperationResult<RedirectServiceModel>> CreateAsync(
            RedirectCreateViewModel create, string actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult<RedirectServiceModel>> UpdateAsync(
            Guid id, RedirectUpdateViewModel update, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<OperationResult> DeleteAsync(
            Guid id, string? ifMatch, string actorUserId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
