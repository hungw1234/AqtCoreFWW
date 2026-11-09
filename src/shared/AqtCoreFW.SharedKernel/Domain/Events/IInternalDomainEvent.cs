namespace AqtCoreFW.SharedKernel.Domain.Events;

/// <summary>
/// Internal domain event — dispatched via MediatR in the SAME transaction.
/// Use for: side effects within module boundary (e.g. update read projections).
/// </summary>
public interface IInternalDomainEvent : IDomainEvent { }
