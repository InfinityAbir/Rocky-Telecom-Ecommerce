namespace RockyTelecom.Application.Common.Abstractions;

/// <summary>Ambient transaction started by <c>TransactionBehaviour</c> around mutating commands.</summary>
public interface ITransactionScope : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}

/// <summary>Transaction boundary abstraction so Application stays free of EF Core types.</summary>
public interface IUnitOfWork
{
    Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default);
}
