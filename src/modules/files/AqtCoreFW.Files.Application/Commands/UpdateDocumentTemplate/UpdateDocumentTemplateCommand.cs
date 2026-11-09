using AqtCoreFW.Files.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Commands.UpdateDocumentTemplate;

/// <summary>
/// Updates name, description, and template content.
/// Phase-4b B3b: TemplateId is long (T1 entity — bigint identity PK, no PublicId AK).
/// </summary>
public sealed record UpdateDocumentTemplateCommand(
    long TemplateId,
    string Name,
    string? Description,
    string TemplateContent,
    Guid ModifiedBy,
    Guid TenantId) : IRequest<Result<DocumentTemplateDto>>;
