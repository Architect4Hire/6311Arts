using System.Buffers.Text;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;

namespace Arts6311.Gateway.Session;

/// <summary>The two emails a browser can ask for by address. Each has its own cooldown.</summary>
internal enum AccountEmailKind
{
    /// <summary><c>/bff/forgot-password</c> (ADR 0028).</summary>
    PasswordReset,

    /// <summary><c>/bff/resend-confirmation</c> (ADR 0029).</summary>
    EmailConfirmation,
}

/// <summary>
/// What <c>/bff/forgot-password</c> and <c>/bff/resend-confirmation</c> do with an address: the cooldown, then a
/// hand-off to <see cref="AccountEmailDispatcher"/>. Never a call to the API.
/// </summary>
/// <remarks>
/// <para>
/// <strong>This is how a request by address stops being a timing oracle (ADR 0028).</strong> The API does more work
/// for a registered address than for an unknown one — an audit row, an outbox row and a save — so a browser that
/// waited for it could time the difference. Nothing that knows whether the address is registered (or, for a
/// confirmation, already confirmed) runs before the browser has its answer, so the answer's timing cannot depend on
/// it.
/// </para>
/// <para>
/// The cost is at-most-once delivery from here to the API: requests still in the queue when the process stops are
/// lost, and their senders ask again once the cooldown has passed. Nothing is persisted, so nothing grows.
/// </para>
/// </remarks>
internal interface IAccountEmailRequests
{
    /// <summary>
    /// Applies the cooldown and queues the request. Returns once that is done; the API is called later. The same
    /// work, in the same order, for every address.
    /// </summary>
    Task SubmitAsync(AccountEmailKind kind, string email, CancellationToken cancellationToken);
}

/// <summary>
/// One queued request, the trace it came from so the API call joins it (ADR 0019), and the cooldown key it holds so
/// a failed dispatch can give it back.
/// </summary>
internal sealed record AccountEmailRequest(
    AccountEmailKind Kind, string Email, string CooldownKey, ActivityContext Origin)
{
    /// <summary>The kind only; the generated <c>ToString</c> would print the address.</summary>
    public override string ToString() => $"{nameof(AccountEmailRequest)} {{ Kind = {Kind} }}";
}

internal sealed partial class AccountEmailRequests(
    ISessionKeyValueStore store,
    AccountEmailQueue queue,
    AccountEmailOptions options,
    ILogger<AccountEmailRequests> logger) : IAccountEmailRequests
{
    public async Task SubmitAsync(AccountEmailKind kind, string email, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(email);

        // SET NX: the first request for an address in a cooldown window wins and is queued; the rest are dropped
        // here. The gateway cannot tell a registered address from an unknown one, so this behaves identically for
        // both by construction — which is the property a cooldown has to have, or it becomes the oracle itself.
        var key = CooldownKey(kind, email);
        if (!await store.AddIfAbsentAsync(key, "1", options.Cooldown, cancellationToken))
        {
            return;
        }

        if (!queue.Writer.TryWrite(new AccountEmailRequest(kind, email, key, Activity.Current?.Context ?? default)))
        {
            // Never the address: it is personal data, and a log line is not where it belongs.
            LogQueueFull(logger, kind);

            // Nothing will be sent, so the cooldown is given back and the person's retry is not silently dropped.
            // Not an oracle: the browser was told 202 either way, and the cooldown's state is never visible to it.
            await store.RemoveAsync(key, cancellationToken);
        }
    }

    /// <summary>
    /// The address as Identity normalizes it, hashed, under a prefix per kind. Normalized the same way so that every
    /// spelling that reaches the same account shares one cooldown; hashed so the store never holds a list of who
    /// asked; one prefix per kind so that asking for a reset never holds back a confirmation, or the other way round.
    /// </summary>
    /// <remarks>
    /// Unsalted, so reversible by dictionary for someone who can read Redis and already has candidate addresses —
    /// the same limit ADR 0023 records for the email ledger. What it would reveal is that somebody asked for that
    /// email for that address in the last few minutes, which is true of unknown addresses as well.
    /// </remarks>
    internal static string CooldownKey(AccountEmailKind kind, string email) =>
        KeyPrefix(kind) + Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(NormalizeEmail(email))));

    /// <summary>
    /// Identity's <c>UpperInvariantLookupNormalizer</c>, mirrored: the gateway holds no Identity code (ADR 0022),
    /// and <c>SessionContractTests</c> holds the two together.
    /// </summary>
    internal static string NormalizeEmail(string email) => email.Normalize().ToUpperInvariant();

    private static string KeyPrefix(AccountEmailKind kind) => kind switch
    {
        AccountEmailKind.PasswordReset => "6311:password-reset-cooldown:",
        AccountEmailKind.EmailConfirmation => "6311:email-confirmation-cooldown:",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown account email kind."),
    };

    [LoggerMessage(
        Level = LogLevel.Warning,
        Message = "An account email request ({Kind}) was dropped: the hand-off queue is full.")]
    private static partial void LogQueueFull(ILogger logger, AccountEmailKind kind);
}

/// <summary>
/// The bounded, in-memory hand-off between the two request endpoints and <see cref="AccountEmailDispatcher"/>. A
/// singleton, so both ends see the same channel; one for both kinds, which share a capacity and a dispatcher.
/// </summary>
internal sealed class AccountEmailQueue(AccountEmailOptions options)
{
    // Wait, not DropWrite: with Wait, TryWrite returns false when the channel is full, which is how a drop is
    // noticed and logged. DropWrite reports success for an item it has thrown away.
    private readonly Channel<AccountEmailRequest> channel = Channel.CreateBounded<AccountEmailRequest>(
        new BoundedChannelOptions(options.QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
        });

    public ChannelWriter<AccountEmailRequest> Writer => channel.Writer;

    public ChannelReader<AccountEmailRequest> Reader => channel.Reader;
}
