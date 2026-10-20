using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Redirects.Facade;
using Arts6311.Domain.Modules.Redirects.Managers.ViewModels;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Modules.Redirects.Managers;

/// <summary>
/// Applies a published record's address change (<c>redirects.slug-changed</c>, ADR 0035): the old address leads to the
/// new one. Idempotent, and safe out of order: the facade compares the rename's time with each redirect's own.
/// </summary>
/// <remarks>
/// <para>
/// The dispatcher gives each message its own DI scope (outbox.md), so the facade injected here is this message's.
/// There is no state to reload: the event is the fact of a rename, and the redirects it touches are read fresh.
/// </para>
/// <para>
/// No terminal state is recorded on the last attempt: this handler calls no provider, so it can only fail when the
/// database does, and a terminal-state write would fail with it (outbox.md's exception). The Poisoned message on the
/// Sync screen is its visible state, and a replay applies it as if it were new.
/// </para>
/// </remarks>
internal sealed partial class RedirectSlugChangedHandler(
    IRedirectsFacade facade, ILogger<RedirectSlugChangedHandler> logger) : IOutboxMessageHandler
{
    public async Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var change = RedirectSlugChanged.TryParse(message.PayloadJson)
            ?? throw new OutboxPoisonException("Malformed redirects.slug-changed payload.");

        var result = await Apply(change, message.Attempt, cancellationToken);

        if (!result.Succeeded)
        {
            // A refusal is about the addresses themselves (one a redirect can't use); retrying can't change it.
            if (result.Error!.Code == ErrorCodes.ValidationFailed)
            {
                throw new OutboxPoisonException("The slug change's addresses can't be redirected.");
            }

            // A concurrency conflict with a staff edit: try again with the table as it now is.
            throw new InvalidOperationException($"Applying the slug change failed ({result.Error.Code}).");
        }
    }

    private async Task<OperationResult> Apply(
        RedirectSlugChanged change, int attempt, CancellationToken cancellationToken)
    {
        try
        {
            return await facade.ApplySlugChangeAsync(
                new RedirectSlugChangeViewModel(change.ContentType, change.ContentId, change.FromPath, change.ToPath, change.OccurredAt),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only the type: a SQL message names the server (outbox.md, LastError is sanitized).
            LogFailed(change.ContentId, exception.GetType().Name, attempt);
            throw new InvalidOperationException($"Applying the slug change failed ({exception.GetType().Name}).");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The slug change for {ContentId} failed ({ExceptionType}) on attempt {Attempt}.")]
    private partial void LogFailed(Guid contentId, string exceptionType, int attempt);
}
