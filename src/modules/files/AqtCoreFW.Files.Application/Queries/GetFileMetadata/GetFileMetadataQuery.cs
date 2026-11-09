using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Files.Application.Queries.GetFileMetadata;

/// <summary>Get file metadata including current virus scan status (used for polling after upload).</summary>
public sealed record GetFileMetadataQuery(Guid FileId, Guid TenantId) : IQuery<FileMetadataDto>;
