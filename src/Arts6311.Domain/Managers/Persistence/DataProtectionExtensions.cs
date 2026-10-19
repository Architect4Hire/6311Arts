using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Domain.Managers.Persistence;

public static class DataProtectionExtensions
{
    /// <summary>The blob container the AppHost declares for the key ring (aspire.md).</summary>
    public const string KeyRingContainerName = "dataprotection";

    private const string KeyRingBlobName = "keys.xml";

    /// <summary>
    /// One Data Protection key ring, shared by every host that needs it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not optional. Identity's email-confirmation and password-reset tokens are Data-Protection-protected, and
    /// the two halves of that flow live in different processes: the <strong>Worker</strong> mints the token when
    /// it sends the email, and the <strong>API</strong> validates it when the link is opened. With per-process key
    /// rings — the default, kept in memory or under the user profile — every emailed link would fail, and would
    /// fail intermittently as processes restarted. The token cannot be passed between them any other way, since
    /// an outbox payload may not carry one (ADR 0023).
    /// </para>
    /// <para>
    /// The application name is fixed rather than defaulted from the entry assembly, because the default differs
    /// per host and would isolate the rings even with shared storage.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddArtsDataProtection(this IServiceCollection services, string applicationName)
    {
        services.AddDataProtection()
            .SetApplicationName(applicationName)
            .PersistKeysToAzureBlobStorage(provider =>
                provider.GetRequiredService<Azure.Storage.Blobs.BlobServiceClient>()
                    .GetBlobContainerClient(KeyRingContainerName)
                    .GetBlobClient(KeyRingBlobName));

        return services;
    }
}
