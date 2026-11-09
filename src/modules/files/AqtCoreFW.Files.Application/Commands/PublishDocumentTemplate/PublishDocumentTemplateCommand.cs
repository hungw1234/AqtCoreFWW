using AqtCoreFW.Files.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Commands.PublishDocumentTemplate;

/// <summary>
/// Publishes a DocumentTemplate (Draft → Active).
/// Phase-4b B3b: TemplateId is long (T1 entity — bigint identity PK, no PublicId AK).
/// </summary>
public sealed record PublishDocumentTemplateCommand(
    long TemplateId,
    Guid PublishedBy,
    Guid TenantId) : IRequest<Result<DocumentTemplateDto>>;
