namespace Arts6311.Domain.Modules.Operations.Managers.ServiceModels;

/// <param name="PingId">The id the handler records the ping under.</param>
/// <param name="CorrelationId">The request's correlation id: its <c>"N"</c> form is the trace id to look up (ADR 0019).</param>
public sealed record PingRequestedServiceModel(Guid PingId, Guid CorrelationId);
