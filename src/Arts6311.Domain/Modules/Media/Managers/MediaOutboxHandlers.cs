using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Results;
using Arts6311.Domain.Modules.Media.Facade;
using Microsoft.Extensions.Logging;

namespace Arts6311.Domain.Modules.Media.Managers;

/// <summary>
/// Produces an original's renditions (<c>media.rendition-requested</c>). Content-addressed, so a redelivery finds
/// every rendition in place and encodes nothing (outbox.md "Make renditions content-addressed").
/// </summary>
/// <remarks>
/// The dispatcher gives each message its own DI scope (outbox.md), so the facade injected here is this message's.
/// The facade reloads the asset: the payload only says which one.
/// </remarks>
internal sealed partial class MediaRenditionRequestedHandler(
    IMediaFacade facade, ILogger<MediaRenditionRequestedHandler> logger) : IOutboxMessageHandler
{
    public async Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var request = MediaRenditionRequested.TryParse(message.PayloadJson)
            ?? throw new OutboxPoisonException("Malformed media.rendition-requested payload.");

        var lastAttempt = message.Attempt >= OutboxPolicy.MaxAttempts;
        OperationResult result;
        try
        {
            result = await facade.ProduceRenditionsAsync(request.AssetId, message.Attempt, lastAttempt, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Only the type: an Azure SDK or SQL message names the account, the server or the request, and the
            // dispatcher writes what it is given into OutboxMessage.LastError (outbox.md).
            LogFailed(request.AssetId, exception.GetType().Name, message.Attempt);
            throw new InvalidOperationException($"Rendering media failed ({exception.GetType().Name}).");
        }

        if (!result.Succeeded)
        {
            // The asset is already marked Failed; retrying cannot make the master decode.
            throw new OutboxPoisonException("The media master does not decode.");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Renditions for {AssetId} failed ({ExceptionType}) on attempt {Attempt}.")]
    private partial void LogFailed(Guid assetId, string exceptionType, int attempt);
}

/// <summary>
/// Deletes a deleted asset's blobs (<c>media.purge-requested</c>). Idempotent: purging an empty prefix deletes
/// nothing and succeeds.
/// </summary>
internal sealed partial class MediaPurgeRequestedHandler(
    IMediaFacade facade, ILogger<MediaPurgeRequestedHandler> logger) : IOutboxMessageHandler
{
    public async Task HandleAsync(OutboxMessageEnvelope message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var request = MediaPurgeRequested.TryParse(message.PayloadJson)
            ?? throw new OutboxPoisonException("Malformed media.purge-requested payload.");

        try
        {
            await facade.PurgeAsync(
                request.AssetId, message.Attempt, message.Attempt >= OutboxPolicy.MaxAttempts, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            LogFailed(request.AssetId, exception.GetType().Name, message.Attempt);
            throw new InvalidOperationException($"Purging media failed ({exception.GetType().Name}).");
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Purging {AssetId} failed ({ExceptionType}) on attempt {Attempt}.")]
    private partial void LogFailed(Guid assetId, string exceptionType, int attempt);
}
