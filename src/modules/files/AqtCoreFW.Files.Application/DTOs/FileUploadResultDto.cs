namespace AqtCoreFW.Files.Application.DTOs;

/// <summary>
/// Upload response — returns 202 Accepted with fileId and status polling URL.
/// Client polls GET /api/v1/files/{id}/metadata or subscribes SignalR file.status.changed.
/// </summary>
public sealed record FileUploadResultDto(
    Guid FileId,
    string StatusUrl,
    string Message = "File uploaded. Virus scan in progress.");
