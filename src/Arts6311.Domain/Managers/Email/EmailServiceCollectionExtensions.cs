using Arts6311.Domain.Managers.Outbox;
using Arts6311.Domain.Managers.Outbox.Events;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Arts6311.Domain.Managers.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Registers the one <c>email.send-requested</c> handler and its options. Called by the Worker only: it is
    /// the only host that sends, so no other process needs a sender or carries an SMTP client (ADR 0023).
    /// </summary>
    /// <remarks>
    /// The <c>IEmailSender</c> implementation is registered by the host, not here: the Domain declares the
    /// provider boundary and the Worker supplies SMTP for Mailpit (external.md).
    /// </remarks>
    public static IServiceCollection AddArtsEmail(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.SectionName))
            .Validate(options =>
            {
                options.Validate();
                return true;
            })
            .ValidateOnStart();

        services.TryAddKeyedScoped<IOutboxMessageHandler, EmailSendRequestedHandler>(EmailSendRequested.MessageType);
        return services;
    }
}
