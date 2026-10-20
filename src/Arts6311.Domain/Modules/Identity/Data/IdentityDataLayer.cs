using Arts6311.Domain.Managers.Audit;
using Arts6311.Domain.Managers.Correlation;
using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Arts6311.Domain.Modules.Identity.Data.Entities;
using Arts6311.Domain.Modules.Identity.Data.Repositories;
using Arts6311.Domain.Modules.Identity.Managers;
using Microsoft.AspNetCore.Identity;

namespace Arts6311.Domain.Modules.Identity.Data;

/// <summary>
/// The Identity module's DataLayer.
/// </summary>
/// <remarks>
/// <para>
/// <strong><c>UserManager</c> lives here, not in Business.</strong> It is the adapter over Identity's store —
/// the equivalent of a repository — and Business has no business knowing about password hashers or lockout
/// columns.
/// </para>
/// <para>
/// <strong>The credential path makes two saves, not one.</strong> <c>UserManager.AccessFailedAsync</c> and
/// <c>ResetAccessFailedCountAsync</c> write immediately through their own store; our audit row is a second save.
/// That departs from backend.md's "one <c>SaveChangesAsync</c>", and it is accepted here because nothing in that
/// path stages an outbox message, so there is no commit-together guarantee to lose: a lockout counter and an
/// audit row that disagree by one row are a worse-than-nothing problem, not a correctness one. Using a reset or
/// confirmation link has the same shape and the same reason — Identity's store saves, then the audit row — and
/// stages nothing either (ADR 0028). The email-request paths below <em>do</em> stage a message, and those make
/// exactly one save (ADR 0023).
/// </para>
/// </remarks>
internal sealed class IdentityDataLayer(
    IIdentityRepository repository,
    UserManager<ArtsUser> users,
    IAuditWriter audit,
    IOutboxWriter outbox,
    ICorrelationContext correlation) : IIdentityDataLayer
{
    /// <summary>
    /// A hash of a value nobody knows, used to spend the same CPU on a missing account as on a wrong password.
    /// Generated once per process: it is never compared for a real decision, only for its cost.
    /// </summary>
    private static readonly Lazy<string> DummyHash = new(() =>
        new PasswordHasher<ArtsUser>().HashPassword(new ArtsUser(), Guid.NewGuid().ToString("N")));

    public Task<ArtsUser?> FindByEmailAsync(string email, CancellationToken cancellationToken) =>
        repository.FindByNormalizedEmailAsync(users.NormalizeEmail(email), cancellationToken);

    public Task<ArtsUser?> FindByIdAsync(string userId, CancellationToken cancellationToken) =>
        repository.FindByIdAsync(userId, cancellationToken);

    public async Task<IReadOnlyList<string>> GetRolesAsync(ArtsUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return [.. await users.GetRolesAsync(user)];
    }

    public async Task<bool> IsLockedOutAsync(ArtsUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await users.IsLockedOutAsync(user);
    }

    public async Task<bool> CheckPasswordAsync(ArtsUser user, string password, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // CheckPasswordAsync deliberately does not touch lockout; Business decides what a failure costs.
        return await users.CheckPasswordAsync(user, password);
    }

    public void VerifyDummyPassword(string password) =>
        users.PasswordHasher.VerifyHashedPassword(new ArtsUser(), DummyHash.Value, password);

    public async Task RecordFailedAccessAsync(ArtsUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Identity increments this as a read-modify-write guarded by the concurrency stamp, so simultaneous wrong
        // guesses collide: without the retry some increments are lost and the loser surfaces as a 500 rather than
        // a refusal. Re-reading and retrying keeps the count honest and the response a 401. The gateway's
        // bff-login rate limit remains the real backstop against a parallel flood (ADR 0023).
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var result = await users.AccessFailedAsync(user);
            if (result.Succeeded)
            {
                return;
            }

            if (await users.FindByIdAsync(user.Id) is not { } reloaded)
            {
                return;
            }

            user = reloaded;
        }
    }

    public async Task ResetFailedAccessAsync(ArtsUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (await users.GetAccessFailedCountAsync(user) > 0)
        {
            await users.ResetAccessFailedCountAsync(user);
        }
    }

    public async Task RevokeSessionsAsync(ArtsUser user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();

        // UpdateSecurityStampAsync writes through Identity's own store immediately, so this is the same two-save
        // shape as the credential path and accepted for the same reason: nothing here stages an outbox message
        // (ADR 0023). The stamp is what matters — an audit row that failed to commit would leave the sessions
        // revoked, which is the safe direction.
        await users.UpdateSecurityStampAsync(user);

        audit.Record(new AuditEntry(
            ActorUserId: user.Id,
            IdentityAudit.SessionsRevoked,
            IdentityAudit.UserResourceType,
            user.Id,
            correlation.Current,
            "All sessions revoked: security stamp rotated."));

        await repository.SaveChangesAsync(cancellationToken);
    }

    public Task RecordAuditAsync(
        string? userId, string action, string summary, CancellationToken cancellationToken)
    {
        // No email address and no password in the summary: an audit row is readable by every Admin, and the
        // address is already on the user row this entry points at.
        audit.Record(new AuditEntry(
            ActorUserId: userId,
            action,
            IdentityAudit.UserResourceType,
            userId ?? "unknown",
            correlation.Current,
            summary));

        return repository.SaveChangesAsync(cancellationToken);
    }

    public Task RequestEmailAsync(
        string userId, string kind, string auditAction, string summary, CancellationToken cancellationToken)
    {
        var correlationId = correlation.Current;

        audit.Record(new AuditEntry(
            ActorUserId: userId,
            auditAction,
            IdentityAudit.UserResourceType,
            userId,
            correlationId,
            summary));

        // Ids only: the kind and the user id. The address is resolved and the token minted by the composer at
        // send time, because neither may travel in a payload (add-outbox-message).
        outbox.Enqueue(
            EmailSendRequested.MessageType,
            new EmailSendRequested(kind, userId).Serialize(),
            correlationId);

        // One save: the audit row and the message commit together, or neither does.
        return repository.SaveChangesAsync(cancellationToken);
    }

    public async Task<string> GeneratePasswordResetTokenAsync(ArtsUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await users.GeneratePasswordResetTokenAsync(user);
    }

    public async Task<string> GenerateEmailConfirmationTokenAsync(ArtsUser user, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await users.GenerateEmailConfirmationTokenAsync(user);
    }

    public async Task<bool> VerifyPasswordResetTokenAsync(
        ArtsUser user, string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await users.VerifyUserTokenAsync(
            user,
            users.Options.Tokens.PasswordResetTokenProvider,
            UserManager<ArtsUser>.ResetPasswordTokenPurpose,
            token);
    }

    public async Task<bool> VerifyEmailConfirmationTokenAsync(
        ArtsUser user, string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await users.VerifyUserTokenAsync(
            user,
            users.Options.Tokens.EmailConfirmationTokenProvider,
            UserManager<ArtsUser>.ConfirmEmailTokenPurpose,
            token);
    }

    /// <remarks>
    /// <para>
    /// The same two-save shape as the credential path, accepted for the same reason: <c>ResetPasswordAsync</c>
    /// writes through Identity's store immediately, our audit row is a separate save, and nothing here stages an
    /// outbox message (ADR 0023). The stamp rotation inside it is what ends every live session and what spends
    /// the token: Data Protection tokens embed the stamp, so the same link cannot verify twice.
    /// </para>
    /// <para>
    /// A <c>ConcurrencyFailure</c> is a second use of the same link losing the race to the row — both verified
    /// before either saved. It is reported as a spent link, which is exactly what it is.
    /// </para>
    /// <para>
    /// <strong>Every failure reloads the entity.</strong> Identity validates the password before it touches the
    /// user, but sets the new hash and stamp <em>before</em> its user validators and the save — so a refusal from
    /// either leaves them on the tracked entity, and the refusal's audit save would commit exactly what was
    /// refused.
    /// </para>
    /// </remarks>
    public async Task<PasswordResetOutcome> ResetPasswordAsync(
        ArtsUser user, string token, string newPassword, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await users.ResetPasswordAsync(user, token, newPassword);
        if (result.Succeeded)
        {
            return PasswordResetOutcome.Success;
        }

        await repository.ReloadAsync(user, cancellationToken);

        var describer = users.ErrorDescriber;
        if (result.Errors.Any(error => error.Code == describer.InvalidToken().Code
            || error.Code == describer.ConcurrencyFailure().Code))
        {
            return PasswordResetOutcome.Rejected;
        }

        // A failure that names no reason would otherwise read as success (no token problem, no password errors)
        // and be audited as a completed reset. It is a fault, not an answer.
        if (!result.Errors.Any())
        {
            throw new InvalidOperationException("Identity refused a password reset without saying why.");
        }

        // Identity's descriptions, and BreachedPasswordValidator's: written for the person, and none of them
        // quotes the password.
        return new PasswordResetOutcome(false, [.. result.Errors.Select(error => error.Description)]);
    }

    public async Task<bool> ConfirmEmailAsync(ArtsUser user, string token, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if ((await users.ConfirmEmailAsync(user, token)).Succeeded)
        {
            return true;
        }

        // A refused token changes nothing, but a lost race or a user-validator refusal leaves the entity modified
        // in the tracker (see above).
        await repository.ReloadAsync(user, cancellationToken);
        return false;
    }
}
