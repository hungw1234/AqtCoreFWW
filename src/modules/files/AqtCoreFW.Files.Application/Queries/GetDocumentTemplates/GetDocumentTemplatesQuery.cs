using AqtCoreFW.Files.Application.DTOs;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Files.Application.Queries.GetDocumentTemplates;

/// <summary>Returns document templates for a tenant with pagination and optional search.</summary>
public sealed record GetDocumentTemplatesQuery(
    Guid TenantId,
    DocumentTemplateStatus? Status = null,
    string? SearchTerm = null,
    int Page = 1,
    int PageSize = 20) : IRequest<Result<PagedResult<DocumentTemplateDto>>>;
