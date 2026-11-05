using MediatR;

namespace RockyTelecom.Application.Common.Messaging;

/// <summary>
/// Marker for reads. Queries never open a transaction and never mutate state.
/// </summary>
public interface IQuery<out TResponse> : IRequest<TResponse> { }

/// <summary>
/// Marker for writes. The <c>TransactionBehaviour</c> wraps these in one EF transaction and
/// rolls back on failure (ARCHITECTURE §3).
/// </summary>
public interface ICommand<out TResponse> : IRequest<TResponse> { }

/// <summary>Command with no payload; returns <see cref="Unit"/>.</summary>
public interface ICommand : ICommand<Unit> { }
