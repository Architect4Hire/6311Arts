using System.Text.Encodings.Web;
using Arts6311.Domain.Managers.Email;
using Arts6311.Domain.Modules.Identity.Facade;
using Microsoft.Extensions.Options;

namespace Arts6311.Domain.Modules.Identity.Managers;

/// <summary>
/// Shared shape for this module's two emails: resolve the recipient and mint the token through the facade, then
/// build a link against the configured public origin.
/// </summary>
/// <remarks>
/// A composer reloads current state rather than trusting the payload (outbox.md), which is why it takes only a
/// user id. Links point at the gateway, the only public endpoint (ADR 0001) — never at the API or a container
/// hostname — and re-authorize when opened.
/// </remarks>
internal abstract class IdentityEmailComposer(IIdentityFacade facade, IOptions<EmailOptions> options)
    : IEmailComposer
{
    protected IIdentityFacade Facade => facade;

    public async Task<EmailMessage?> ComposeAsync(string subjectId, CancellationToken cancellationToken)
    {
        var token = await LoadAsync(subjectId, cancellationToken);

        // Nothing to send: the account is gone, or the thing this email was about already happened. The handler
        // records a skip rather than retrying.
        if (token is null)
        {
            return null;
        }

        var link = BuildLink(subjectId, token.Token);
        return Build(token.Email, token.DisplayName, link);
    }

    protected abstract Task<ServiceModels.EmailTokenServiceModel?> LoadAsync(
        string userId, CancellationToken cancellationToken);

    protected abstract string Path { get; }

    protected abstract EmailMessage Build(string to, string displayName, string link);

    /// <summary>
    /// The link, with the user id and the token in the <strong>fragment</strong>, not the query string.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A browser never sends a fragment to the server, which keeps the token out of access logs, out of
    /// <c>Referer</c> on any onward navigation, and out of the gateway's OpenTelemetry spans — which record
    /// <c>url.query</c> and redact only a handful of well-known key names, so a token in the query would land in
    /// the Aspire dashboard and in whatever collects traces in Azure. auth.md says tokens never appear in URLs or
    /// logs; this is how that holds for a link somebody has to click (ADR 0023).
    /// </para>
    /// <para>
    /// <strong>1.10's confirm and reset pages must therefore read <c>location.hash</c> client-side and POST the
    /// token</strong> rather than expecting a query parameter.
    /// </para>
    /// <para>
    /// Both values are URL-encoded: Identity's protector returns Base64 containing <c>+</c> and <c>/</c>, which
    /// would otherwise be altered in transit.
    /// </para>
    /// </remarks>
    private string BuildLink(string userId, string token)
    {
        var origin = options.Value.SiteOrigin.TrimEnd('/');
        var encoder = UrlEncoder.Default;

        return $"{origin}{Path}#userId={encoder.Encode(userId)}&token={encoder.Encode(token)}";
    }
}

internal sealed class EmailConfirmationComposer(IIdentityFacade facade, IOptions<EmailOptions> options)
    : IdentityEmailComposer(facade, options)
{
    protected override string Path => "/account/confirm-email";

    protected override Task<ServiceModels.EmailTokenServiceModel?> LoadAsync(
        string userId, CancellationToken cancellationToken) =>
        Facade.GetEmailConfirmationTokenAsync(userId, cancellationToken);

    protected override EmailMessage Build(string to, string displayName, string link) => new(
        to,
        "Confirm your 6311arts email address",
        $"""
        Hello {displayName},

        Confirm your email address to finish setting up your 6311arts account:

        {link}

        If you did not create this account, you can ignore this message.

        6311arts
        6311 N. Clark St., Chicago
        """);
}

internal sealed class PasswordResetComposer(IIdentityFacade facade, IOptions<EmailOptions> options)
    : IdentityEmailComposer(facade, options)
{
    protected override string Path => "/account/reset-password";

    protected override Task<ServiceModels.EmailTokenServiceModel?> LoadAsync(
        string userId, CancellationToken cancellationToken) =>
        Facade.GetPasswordResetTokenAsync(userId, cancellationToken);

    protected override EmailMessage Build(string to, string displayName, string link) => new(
        to,
        "Reset your 6311arts password",
        $"""
        Hello {displayName},

        Use this link to choose a new password:

        {link}

        If you did not ask to reset your password, nothing has changed and you can ignore this message.

        6311arts
        6311 N. Clark St., Chicago
        """);
}
