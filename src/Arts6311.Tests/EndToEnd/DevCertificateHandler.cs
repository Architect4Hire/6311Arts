using System.Net.Security;
using System.Security.Cryptography.X509Certificates;

namespace Arts6311.Tests.EndToEnd;

/// <summary>
/// An HTTP handler for the gateway's run-mode HTTPS endpoint, which serves the ASP.NET Core developer certificate.
/// </summary>
/// <remarks>
/// A laptop that has run <c>dotnet dev-certs https --trust</c> already trusts it; a CI runner may not. Rather than
/// depend on machine state, this accepts a certificate the platform would reject only when the request is to a
/// loopback address <em>and</em> the certificate is one of this machine's own developer certificates — matched by
/// thumbprint against the current user's store, where Kestrel loads it from. The marker extension alone is not
/// enough: anyone can mint a certificate carrying it. Anything else is validated normally. Cookies are left to
/// <see cref="CookieJarHandler"/>.
/// </remarks>
internal static class DevCertificateHandler
{
    /// <summary>The extension <c>dotnet dev-certs</c> stamps on the ASP.NET Core HTTPS development certificate.</summary>
    private const string AspNetHttpsOid = "1.3.6.1.4.1.311.84.1.1";

    private static readonly Lazy<HashSet<string>> DeveloperThumbprints = new(LoadDeveloperThumbprints);

    public static HttpClientHandler Create() => new()
    {
        UseCookies = false,
        ServerCertificateCustomValidationCallback = Validate,
    };

    private static bool Validate(
        HttpRequestMessage request, X509Certificate2? certificate, X509Chain? chain, SslPolicyErrors errors) =>
        errors == SslPolicyErrors.None
        || (request.RequestUri is { IsLoopback: true }
            && certificate is not null
            && DeveloperThumbprints.Value.Contains(certificate.Thumbprint));

    private static HashSet<string> LoadDeveloperThumbprints()
    {
        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);

        var thumbprints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var certificate in store.Certificates)
        {
            using (certificate)
            {
                if (certificate.Extensions[AspNetHttpsOid] is not null)
                {
                    thumbprints.Add(certificate.Thumbprint);
                }
            }
        }

        return thumbprints;
    }
}
