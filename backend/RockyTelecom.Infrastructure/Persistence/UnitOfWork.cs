using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RockyTelecom.Application.Common.Abstractions;

namespace RockyTelecom.Infrastructure.Persistence;

/// <summary>EF-backed transaction scope used by <c>TransactionBehaviour</c> for mutating commands.</summary>
internal sealed class EfTransactionScope : ITransactionScope
{
    private readonly IDbContextTransaction _transaction;
    private readonly AppDbContext _db;
    private bool _finished;

    public EfTransactionScope(AppDbContext db, IDbContextTransaction transaction)
    {
        _db = db;
        _transaction = transaction;
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_finished)
        {
            return;
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _transaction.CommitAsync(cancellationToken);
        _finished = true;
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_finished)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        _finished = true;
    }

    public async ValueTask DisposeAsync()
    {
        // A scope that was never committed must not leak an open transaction.
        if (!_finished)
        {
            try
            {
                await _transaction.RollbackAsync();
            }
            catch (Exception)
            {
                // Disposal must not mask the original failure.
            }
        }

        await _transaction.DisposeAsync();
    }
}

internal sealed class EfUnitOfWork : IUnitOfWork
{
    private readonly AppDbContext _db;

    public EfUnitOfWork(AppDbContext db) => _db = db;

    public async Task<ITransactionScope> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        // A plain transaction is safe because connection retries are disabled (D-22); a retrying
        // execution strategy cannot wrap a user-initiated transaction opened by the pipeline.
        var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        return new EfTransactionScope(_db, transaction);
    }
}
