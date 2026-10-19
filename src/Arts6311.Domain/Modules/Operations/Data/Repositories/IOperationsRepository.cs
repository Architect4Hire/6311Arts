namespace Arts6311.Domain.Modules.Operations.Data.Repositories;

internal interface IOperationsRepository
{
    Task<bool> PingHandledExistsAsync(Guid pingId, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
