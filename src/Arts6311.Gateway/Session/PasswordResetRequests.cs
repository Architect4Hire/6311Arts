using System.Buffers.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace Arts6311.Gateway.Session;

/// <summary>
/// What <c>/bff/forgot-password</c> does with an address: the cooldown, then a hand-off to
/// <see cref="PasswordResetDispatcher"/>. Never a call to the API.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is how the reset request stops being a timing oracle (ADR 0028).</strong> The API does more work
/// for a registered address than for an unknown one — an audit row, an outbox row and a save — so a browser that
/// waited for it could time the difference. Nothing that knows whether the address is registered runs before the
/// browser has its answer, so the answer's timing cannot depend on it.
/// </para>
/// <para>
/// The cost is at-most-once delivery from here to the API: requests still in the queue when the process stops are
/// lost, and their senders ask again once the cooldown has passed. Nothing is persisted, so nothing grows.
/// </para>
/// </remarks>
internal interface IPasswordResetRequests
{
    /// <summary>
    /// Applies the cooldown and queues the request. Returns once that is done; the API is called later. The same
    /// work, in the same order, for every address.
    /// </summary>
    Task SubmitAsync(string email, CancellationToken cancellationToken);
}

/// <summary>
/// One queued request, the trace it came from so the API call joins it (ADR 0019), and the cooldown key it holds so
/// a failed dispatch can give it back.
/// </summary>
internal sealed record PasswordResetRequest(string Email, string CooldownKey, ActivityContext Origin);

internal sealed partial class PasswordResetRequests(
    ISessionKeyValueStore store,
    PasswordResetQueue queue,
    PasswordResetOptions options,
    ILogger<PasswordResetRequests> logger) : IPasswordResetRequests
{
    private const string KeyPrefix = "6311:password-reset-cooldown:";

    public async Task SubmitAsync(string email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        // SET NX: the first request for an address in a cooldown window wins and is queued; the rest are dropped
        // here. The gateway cannot tell a registered address from an unknown one, so this behaves identically for
        // both by construction — which is the property a cooldown has to have, or it becomes the oracle itself.
        var key = CooldownKey(email);
        if (!await store.AddIfAbsentAsync(key, "1", options.Cooldown, cancellationToken))
        {
            return;
        }

        if (!queue.Writer.TryWrite(new PasswordResetRequest(email, key, Activity.Current?.Context ?? default)))
        {
            // Never the address: it is personal data, and a log line is not where it belongs.
            LogQueueFull(logger);

            // Nothing will be sent, so the cooldown is given back and the person's retry is not silently dropped.
            // Not an oracle: the browser was told 202 either way, and the cooldown's state is never visible to it.
            await store.RemoveAsync(key, cancellationToken);
        }
    }

    /// <summary>
    /// The address as Identity normalizes it, hashed. Normalized the same way so that every spelling that reaches
    /// the same account shares one cooldown; hashed so the store never holds a list of who asked for a reset.
    /// </summary>
    /// <remarks>
    /// Unsalted, so reversible by dictionary for someone who can read Redis and already has candidate addresses —
    /// the same limit ADR 0023 records for the email ledger. What it would reveal is that somebody asked for a
    /// reset for that address in the last few minutes, which is true of unknown addresses as well.
    /// </remarks>
    internal static string CooldownKey(string email) =>
        KeyPrefix + Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeEmail(email))));

    /// <summary>
    /// Identity's <c>UpperInvariantLookupNormalizer</c>, mirrored: the gateway holds no Identity code (ADR 0022),
    /// and <c>SessionContractTests</c> holds the two together.
    /// </summary>
    internal static string NormalizeEmail(string email) => email.Normalize().ToUpperInvariant();

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "A password reset request was dropped: the hand-off queue is full.")]
    private static partial void LogQueueFull(ILogger logger);
}

/// <summary>
/// The bounded, in-memory hand-off between <c>/bff/forgot-password</c> and <see cref="PasswordResetDispatcher"/>.
/// A singleton, so both ends see the same channel.
/// </summary>
internal sealed class PasswordResetQueue(PasswordResetOptions options)
{
    // Wait, not DropWrite: with Wait, TryWrite returns false when the channel is full, which is how a drop is
    // noticed and logged. DropWrite reports success for an item it has thrown away.
    private readonly Channel<PasswordResetRequest> channel = Channel.CreateBounded<PasswordResetRequest>(
        new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    public ChannelWriter<PasswordResetRequest> Writer => channel.Writer;

    public ChannelReader<PasswordResetRequest> Reader => channel.Reader;
}
