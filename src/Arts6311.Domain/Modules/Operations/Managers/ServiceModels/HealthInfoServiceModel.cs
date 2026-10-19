namespace Arts6311.Domain.Modules.Operations.Managers.ServiceModels;

/// <summary>
/// What <c>GET /api/v1/site/health-info</c> returns.
/// </summary>
/// <remarks>
/// The route is anonymous and reachable from the public internet through the gateway, so this says nothing about
/// the host, the build or the environment. Adding a version, a machine name or an environment name here would make
/// an unauthenticated fingerprinting surface out of a liveness echo. The readiness detail that *is* useful to
/// operators stays on <c>/health</c>, which the gateway does not expose.
/// </remarks>
/// <param name="Status">Always <c>ok</c>: reaching the route at all is the signal.</param>
/// <param name="ServerTimeUtc">
/// The server's current instant, so a client can tell its own clock has drifted before it misreads a class time.
/// </param>
public sealed record HealthInfoServiceModel(string Status, DateTimeOffset ServerTimeUtc);
