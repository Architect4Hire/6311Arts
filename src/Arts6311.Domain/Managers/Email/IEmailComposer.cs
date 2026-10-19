namespace Arts6311.Domain.Managers.Email;

/// <summary>
/// Turns a subject id into the email to send, for one <see cref="Outbox.Events.EmailSendRequested.Kind"/>.
/// </summary>
/// <remarks>
/// <para>
/// Registered with keyed DI under its kind by the module that owns it, so the kernel's handler delivers email for
/// any module without referencing one — the shared kernel never depends on a module (backend.md).
/// </para>
/// <para>
/// A composer reloads current state and resolves the recipient itself: the payload names only what to look at
/// (outbox.md "Never trust the payload's state"). It also mints any Identity token here, at send time, because a
/// token must never travel in an outbox payload.
/// </para>
/// </remarks>
public interface IEmailComposer
{
    /// <summary>
    /// The email, or <c>null</c> when there is nothing to send any more — the subject was deleted, or the thing
    /// the email is about has already happened (an address confirmed since the message was staged). Returning
    /// <c>null</c> is a success: the handler records the delivery as skipped and does not retry.
    /// </summary>
    Task<EmailMessage?> ComposeAsync(string subjectId, CancellationToken cancellationToken);
}
