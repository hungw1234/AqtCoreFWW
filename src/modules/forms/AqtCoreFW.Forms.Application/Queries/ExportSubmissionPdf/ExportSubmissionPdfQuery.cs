using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.ExportSubmissionPdf;

/// <summary>Generates a PDF byte array for a single submission. Returns bytes for file download.</summary>
public sealed record ExportSubmissionPdfQuery(Guid SubmissionId, Guid TenantId)
    : IQuery<byte[]>;
