using AqtCoreFW.Files.Application.Queries.DownloadFile;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Files.Application.Queries.DownloadSignedStep;

/// <summary>
/// Downloads the signed PDF for a specific signing attempt of an uploaded file.
/// The storage key is resolved server-side — the client never passes a bucket or key.
///
/// <para>Addressing: when <see cref="RequestId"/> is set the exact signing attempt with that
/// SigningRequest PublicId is resolved (so each independent sign of the same file — which all share
/// sequence 1 — is retrievable). When it is null the legacy (tenantId, fileId, sequenceNumber)
/// resolution is used (latest Completed at that sequence).</para>
/// </summary>
public sealed record DownloadSignedStepQuery(
    Guid FileId,
    Guid TenantId,
    int SequenceNumber,
    Guid? RequestId = null)
    : IQuery<DownloadFileResult>;
