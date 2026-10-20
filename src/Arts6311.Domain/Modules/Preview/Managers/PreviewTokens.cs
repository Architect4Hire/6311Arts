using Arts6311.Domain.Managers.Time;
using Microsoft.AspNetCore.DataProtection;

namespace Arts6311.Domain.Modules.Preview.Managers;

/// <summary>What a preview token says: which record to show. Nothing else is in it.</summary>
internal sealed record PreviewSubject(string EntityType, Guid EntityId);

/// <summary>Mints and reads preview tokens (ADR 0036).</summary>
internal interface IPreviewTokens
{
    /// <summary>A token for one record, and when it stops working.</summary>
    (string Token, DateTimeOffset ExpiresAt) Create(string entityType, Guid entityId);

    /// <summary>
    /// What the token names, or <c>null</c> when it is not one of ours, has been tampered with, has expired, or
    /// names a type previews are not made for. Every one of those is the same answer, so a caller cannot learn
    /// which.
    /// </summary>
    PreviewSubject? Read(string? token);
}

/// <summary>
/// Data Protection's time-limited protector over a payload of <c>type:id</c>.
/// </summary>
/// <remarks>
/// <para>
/// The key ring is the one every host shares (<c>AddArtsDataProtection</c>), so a token minted by the API is
/// readable by the API after a restart or on another replica. Expiry is the protector's own: an expired token
/// fails to unprotect rather than being read and checked afterwards.
/// </para>
/// <para>
/// <strong>The token's life is a lifetime, not a deadline we compute.</strong> Data Protection validates the
/// expiry it embedded against the system clock, so handing it an instant from <see cref="IClock"/> would make a
/// token's validity depend on two clocks agreeing. It is given <see cref="PreviewPolicy.TokenLifetime"/> instead,
/// and <see cref="IClock"/> only says what staff are told the expiry is — the same instant in any host that
/// reads time the way the rules require.
/// </para>
/// <para>
/// The token is the whole credential, so it carries no user, no role and no row version: it grants one read of
/// one record and nothing else (ADR 0036). It is signed and encrypted, so a holder cannot read or change which
/// record it names.
/// </para>
/// </remarks>
internal sealed class PreviewTokens(IDataProtectionProvider provider, IClock clock) : IPreviewTokens
{
    private readonly ITimeLimitedDataProtector protector =
        provider.CreateProtector(PreviewPolicy.ProtectorPurpose).ToTimeLimitedDataProtector();

    public (string Token, DateTimeOffset ExpiresAt) Create(string entityType, Guid entityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityType);
        ArgumentOutOfRangeException.ThrowIfEqual(entityId, Guid.Empty);

        return (
            protector.Protect($"{entityType}:{entityId:N}", PreviewPolicy.TokenLifetime),
            clock.UtcNow + PreviewPolicy.TokenLifetime);
    }

    public PreviewSubject? Read(string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > PreviewPolicy.TokenMaxLength)
        {
            return null;
        }

        string payload;
        try
        {
            payload = protector.Unprotect(token);
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            // Expired, tampered with, from another purpose, or never a token at all. The caller answers 404 for
            // all of them alike.
            return null;
        }

        var separator = payload.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0 || !Guid.TryParseExact(payload[(separator + 1)..], "N", out var entityId))
        {
            return null;
        }

        var entityType = payload[..separator];
        return PreviewPolicy.EntityTypes.Contains(entityType) ? new PreviewSubject(entityType, entityId) : null;
    }
}
