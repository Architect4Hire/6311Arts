namespace Arts6311.Api;

/// <summary>
/// Anchors <c>WebApplicationFactory&lt;T&gt;</c> to this assembly in tests. Never instantiated.
/// </summary>
/// <remarks>
/// <c>WebApplicationFactory</c> only uses its type argument to find the assembly holding the host builder, and
/// <c>Program</c> cannot serve: both this project and <c>Arts6311.AppHost</c> use top-level statements, so each
/// has a <c>Program</c> in the global namespace, and the test project references both. A marker in a real
/// namespace is unambiguous and says why it exists.
/// </remarks>
internal sealed class ApiHost
{
    private ApiHost()
    {
    }
}
