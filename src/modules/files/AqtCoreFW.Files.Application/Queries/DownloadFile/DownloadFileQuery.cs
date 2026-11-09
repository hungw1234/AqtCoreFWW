using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Files.Application.Queries.DownloadFile;

/// <summary>Download file stream from storage. Returns stream + content metadata for response headers.</summary>
public sealed record DownloadFileQuery(Guid FileId, Guid TenantId, Guid RequestedBy) : IQuery<DownloadFileResult>;

// No SizeBytes: the response Content-Length is taken by ASP.NET from the seekable stream's actual
// Length (FilesController streams via File(stream, …, enableRangeProcessing: true)), so a separate
// metadata size would be an unread field that could only drift from the real bytes.
public sealed record DownloadFileResult(
    Stream FileStream,
    string OriginalFileName,
    string ContentType);
