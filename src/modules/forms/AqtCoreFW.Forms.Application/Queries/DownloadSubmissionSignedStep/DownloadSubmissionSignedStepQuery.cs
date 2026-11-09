using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.DownloadSubmissionSignedStep;

/// <summary>
/// Downloads the signed PDF for a specific signing step of a form submission.
/// The storage key is resolved server-side from (tenantId, submissionPublicId, sequenceNumber) —
/// the client never passes a bucket or storage key.
/// </summary>
public sealed record DownloadSubmissionSignedStepQuery(
    Guid SubmissionPublicId,
    Guid TenantId,
    int SequenceNumber)
    : IQuery<DownloadSubmissionSignedStepResult>;

/// <summary>Carries the PDF stream and metadata for the controller to build the file response.</summary>
public sealed record DownloadSubmissionSignedStepResult(
    Stream FileStream,
    string FileName,
    string ContentType);
