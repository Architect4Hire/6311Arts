using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;

namespace Arts6311.Gateway.Edge;

internal static class RateLimitExtensions
{
    /// <summary>The named policies from <c>gateway.md</c>. A YARP route references one by name.</summary>
    public static class Policies
    {
        public const string BffLogin = "bff-login";
        public const string Forms = "forms";
        public const string Checkout = "checkout";
        public const string Webhooks = "webhooks";
        public const string Api = "api";

        /// <summary>Every policy the gateway defines, for the test that keeps this list and gateway.md in step.</summary>
        public static readonly string[] All = [BffLogin, Forms, Checkout, Webhooks, Api];
    }

    /// <summary>
    /// Registers the named policies and the generous global limit (<c>gateway.md</c> "Rate limits").
    /// </summary>
    /// <remarks>
    /// <para>
    /// Takes the already-bound <paramref name="options"/> rather than reading them from DI, so a policy a route
    /// names but configuration never defines stops the host at startup instead of failing at request time, on that
    /// one route, long after deployment.
    /// </para>
    /// <para>
    /// All five are defined now even though <c>bff-login</c>, <c>forms</c> and <c>checkout</c> have no route until
    /// 1.10, 2.12 and 5.14: the prompts that add those routes then only reference a name, and the limits stay in
    /// one place rather than arriving piecemeal.
    /// </para>
    /// <para>
    /// Partitioned by client IP. That is the best key available before there are sessions; it over-counts callers
    /// sharing one NAT, which is why the limits are generous rather than tight. A 429 carries <c>Retry-After</c>.
    /// </para>
    /// </remarks>
    public static IServiceCollection AddArtsRateLimiter(this IServiceCollection services, GatewayOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        foreach (var name in Policies.All)
        {
            if (!options.RateLimits.ContainsKey(name))
            {
                throw new InvalidOperationException(
                    $"Rate-limit policy '{name}' is not configured under {GatewayOptions.SectionName}:RateLimits.");
            }
        }

        if (options.GlobalRateLimit.PermitLimit <= 0)
        {
            throw new InvalidOperationException(
                $"{GatewayOptions.SectionName}:GlobalRateLimit:PermitLimit must be greater than zero.");
        }

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.OnRejected = async (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)retryAfter.TotalSeconds).ToString(CultureInfo.InvariantCulture);
                }

                await GatewayProblem.WriteAsync(context.HttpContext, StatusCodes.Status429TooManyRequests);
            };

            foreach (var (name, policy) in options.RateLimits)
            {
                limiter.AddPolicy(name, httpContext => Partition(httpContext, name, policy));
            }

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(
                httpContext => Partition(httpContext, "global", options.GlobalRateLimit));
        });

        return services;
    }

    private static RateLimitPartition<string> Partition(
        HttpContext httpContext, string name, RateLimitPolicyOptions policy) =>
        RateLimitPartition.GetFixedWindowLimiter(
            $"{name}:{PartitionKey(httpContext.Connection.RemoteIpAddress)}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = policy.PermitLimit,
                Window = policy.Window,
                QueueLimit = policy.QueueLimit,
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            });

    /// <summary>
    /// The caller's identity for limiting purposes: the IPv4 address, or an IPv6 address truncated to its /64
    /// prefix.
    /// </summary>
    /// <remarks>
    /// A full IPv6 address would give one attacker a limit per address, and a residential allocation is typically a
    /// /64 — so the whole budget could be evaded by changing the low 64 bits. Truncating groups them, at the cost
    /// of grouping genuine users behind one /64 together, which is the same trade as a shared IPv4 NAT.
    /// </remarks>
    private static string PartitionKey(IPAddress? address)
    {
        if (address is null)
        {
            // No peer address: one shared partition rather than an unbounded set of them.
            return "unknown";
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 || address.IsIPv4MappedToIPv6)
        {
            return address.ToString();
        }

        Span<byte> bytes = stackalloc byte[16];
        address.TryWriteBytes(bytes, out _);
        bytes[8..].Clear();

        return new IPAddress(bytes).ToString() + "/64";
    }
}
