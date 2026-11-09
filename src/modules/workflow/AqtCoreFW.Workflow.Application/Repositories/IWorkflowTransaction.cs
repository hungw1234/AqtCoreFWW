namespace AqtCoreFW.Workflow.Application.Repositories;

/// <summary>
/// Thin abstraction over a database transaction opened by <see cref="IWorkflowInstanceRepository.BeginTransactionAsync"/>.
/// Keeps EF Core (<c>IDbContextTransaction</c>) out of the Application layer (Application→Infrastructure boundary).
/// Slice 6: used by <c>ExecuteTransitionCommandHandler</c> to own commit/rollback when no ambient transaction
/// is active (<see cref="IWorkflowInstanceRepository.HasAmbientTransaction"/> == false).
/// When ETR is invoked inside an ambient <c>TransactionScope</c> (BP runtime path), it does NOT commit here —
/// see <c>ExecuteTransitionCommandHandler.Handle</c> remarks for the full cooperative-rollback contract.
/// </summary>
/// <seealso cref="Commands.ExecuteTransition.ExecuteTransitionCommandHandler"/>
public interface IWorkflowTransaction : IAsyncDisposable, IDisposable
{
    /// <summary>Commits the transaction.</summary>
    Task CommitAsync(CancellationToken ct = default);

    /// <summary>Rolls back the transaction.</summary>
    Task RollbackAsync(CancellationToken ct = default);
}
