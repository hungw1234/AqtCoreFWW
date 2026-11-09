using AqtCoreFW.Files.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Commands.CreateRetentionPolicy;

/// <summary>Creates a retention policy defining lifecycle rules for a document type.</summary>
public sealed record CreateRetentionPolicyCommand(
    string Name,
    string DocumentType,
    int RetainDays,
    int? ArchiveAfterDays,
    int? DestroyAfterDays,
    Guid TenantId,
    Guid CreatedBy) : IRequest<Result<RetentionPolicyDto>>;
