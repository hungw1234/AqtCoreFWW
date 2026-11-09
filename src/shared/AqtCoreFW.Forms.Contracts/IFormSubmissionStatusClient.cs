namespace AqtCoreFW.SharedKernel.Contracts.Clients;

/// <summary>
/// Cross-module interface for updating form submission status from Workflow module.
/// Monolith: InProcessFormSubmissionStatusClient (repository call).
/// Microservice: gRPC client when Forms module extracted.
/// </summary>
public interface IFormSubmissionStatusClient
{
    Task ApproveSubmissionAsync(Guid submissionId, Guid reviewedBy, string comment, CancellationToken ct = default);
    Task RejectSubmissionAsync(Guid submissionId, Guid reviewedBy, string comment, CancellationToken ct = default);
}
