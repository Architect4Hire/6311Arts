using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Arts6311.Domain.Modules.Operations.Facade;
using Arts6311.Tests.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Arts6311.Tests.Api;

/// <summary>
/// The API accepts the gateway's ES256 internal token and nothing else (ADR 0002, auth.md "Gateway → API trust").
/// Every case below is the happy-path token with exactly one thing changed.
/// </summary>
/// <remarks>
/// The route used throughout is <c>/api/v1/cms/ops/ping</c>: a product route with a role policy, so one request
/// exercises authentication, <c>token_use</c> and a role at once. It is scaffolding deleted in 1.12; when it goes,
/// these tests move to whichever CMS route exists then.
/// </remarks>
public sealed class InternalTokenAuthenticationTests
{
    private const string ProductRoute = "/api/v1/cms/ops/ping";
    private const string AnonymousRoute = "/api/v1/site/health-info";

    [Fact]
    public async Task AValidAdminTokenReachesAProductRoute()
    {
        // Proves the negative cases below fail for the reason claimed, and not because the route never works. The
        // facade is faked because the real one stages an outbox message, which would need a database.
        var facade = new FakeOperationsFacade();
        using var api = new ApiFactory(
            configureServices: services => services.AddScoped<IOperationsFacade>(_ => facade));
        using var client = api.CreateJsonClient();

        var response = await Post(client, api.Tokens.UserToken("Admin"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(1, facade.PingCalls);
    }

    [Fact]
    public async Task NoTokenIs401()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        var response = await client.PostAsync(ProductRoute, content: null, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TheWrongAudienceIs401()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        var response = await Post(client, api.Tokens.Token(audience: "6311-someone-else", roles: ["Admin"]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task TheWrongIssuerIs401()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        var response = await Post(client, api.Tokens.Token(issuer: "someone-elses-gateway", roles: ["Admin"]));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnExpiredTokenIs401()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        // Issued three minutes ago with a two-minute life: expired a minute ago. Clock skew is zero, so there is
        // no grace period to wait out.
        var expired = api.Tokens.Token(
            roles: ["Admin"], issuedAt: DateTimeOffset.UtcNow.AddMinutes(-3), lifetime: TimeSpan.FromMinutes(2));

        var response = await Post(client, expired);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ALifetimeLongerThanTwoMinutesIs401EvenThoughTheSignatureIsGood()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        // Not yet expired, correctly signed, right issuer and audience — and still refused. "≤ 2 minutes" is a
        // property of the token, so a long-lived one must never be accepted just because it is presented early.
        var response = await Post(client, api.Tokens.Token(roles: ["Admin"], lifetime: TimeSpan.FromHours(1)));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ATokenWithNoStartInstantIs401()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        // Correctly signed, right issuer and audience, not expired — and carrying neither nbf nor iat, so its
        // lifetime has no start instant to measure from. Refused rather than treated as short-lived, which is what
        // would otherwise let an unbounded token slip past the ≤ 2 minute rule.
        var response = await Post(client, api.Tokens.TokenWithNoStartInstant());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ATokenSignedByAnotherKeyIs401()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        var response = await Post(client, api.Tokens.TokenFromAnotherKey());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnHmacSignedTokenIs401()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        // Algorithm confusion: rejected on the header by ValidAlgorithms, before any key is consulted.
        var response = await Post(client, api.Tokens.TokenSignedWithHmac());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AnUnsignedTokenIs401()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        var response = await Post(client, UnsignedToken());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GarbageInTheAuthorizationHeaderIs401()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        var response = await Post(client, "not-a-token");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ATamperedPayloadIs401()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        // A real token with one payload character changed and the original signature kept: the signature no longer
        // matches the content it was made for.
        var parts = api.Tokens.UserToken("Member").Split('.');
        var payload = parts[1];
        var tampered = string.Concat(parts[0], ".", payload[..^1], payload[^1] == 'A' ? 'B' : 'A', ".", parts[2]);

        var response = await Post(client, tampered);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AKeyTheApiDoesNotHoldIs401()
    {
        // The API trusts one key. Here it is handed a different gateway's public key, so a perfectly formed token
        // from "our" gateway no longer verifies.
        using var other = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var api = new ApiFactory(publicKeyPem: other.ExportSubjectPublicKeyInfoPem());
        using var client = api.CreateJsonClient();

        var response = await Post(client, api.Tokens.UserToken("Admin"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AServiceTokenOnAProductRouteIs403()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        // It authenticates — the gateway signed it — and is then refused by the policy: product routes take only
        // token_use=user. 403, not 401: the caller is known, the credential is simply the wrong kind.
        var response = await Post(client, api.Tokens.ServiceToken());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AUserTokenWithNoTokenUseClaimIs403()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        var response = await Post(client, api.Tokens.Token(tokenUse: string.Empty, roles: ["Admin"]));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task TheWrongRoleIs403()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        var response = await Post(client, api.Tokens.UserToken("Member"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task NoRoleAtAllIs403()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        var response = await Post(client, api.Tokens.UserToken());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AllowAnonymousWorksWithNoToken()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        var response = await client.GetAsync(AnonymousRoute, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task AnAnonymousRouteIgnoresAnUnusableToken()
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        // [AllowAnonymous] means the route does not care. An expired token must not turn a public read into a 401.
        using var request = new HttpRequestMessage(HttpMethod.Get, AnonymousRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            api.Tokens.Token(issuedAt: DateTimeOffset.UtcNow.AddMinutes(-9), lifetime: TimeSpan.FromMinutes(2)));

        var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/health")]
    [InlineData("/alive")]
    public async Task ServiceDefaultsHealthEndpointsNeedNoToken(string path)
    {
        using var api = new ApiFactory();
        using var client = api.CreateJsonClient();

        // They are mapped outside MapAuthenticatedControllers, so secure-by-default does not reach them. They must
        // stay open: Aspire's health check carries no token. /health also checks SQL, which these tests cannot
        // reach, so only "not a credential problem" is asserted.
        var response = await client.GetAsync(path, TestContext.Current.CancellationToken);

        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, ProductRoute);
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        return client.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>A token with <c>alg: none</c> and an empty signature: the oldest JWT trick there is.</summary>
    private static string UnsignedToken()
    {
        static string Segment(string json) =>
            Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json))
                .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = Segment("""{"alg":"none","typ":"JWT"}""");
        var expires = DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds();
        var notBefore = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = Segment(
            $$"""{"iss":"6311-gateway","aud":"6311-api","sub":"1","token_use":"user","role":"Admin","nbf":{{notBefore}},"exp":{{expires}}}""");

        return $"{header}.{payload}.";
    }
}
