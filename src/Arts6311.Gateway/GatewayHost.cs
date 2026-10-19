namespace Arts6311.Gateway;

/// <summary>
/// Anchors <c>WebApplicationFactory&lt;T&gt;</c> to this assembly in tests. Never instantiated.
/// </summary>
/// <remarks>
/// <c>WebApplicationFactory</c> only uses its type argument to find the assembly holding the host builder, and
/// <c>Program</c> cannot serve: this project, <c>Arts6311.Api</c> and <c>Arts6311.AppHost</c> all use top-level
/// statements, so each has a <c>Program</c> in the global namespace and the test project references all three.
/// </remarks>
internal sealed class GatewayHost
{
    private GatewayHost()
    {
    }
}
