namespace Arts6311.Domain.Modules.Operations.Managers;

/// <summary>The audit vocabulary this module writes: the same <c>area.event</c> names as the ping's message.</summary>
internal static class OperationsAudit
{
    public const string PingResourceType = "system.ping";
    public const string PingRequested = "system.ping-requested";
    public const string PingHandled = "system.ping-handled";
}
