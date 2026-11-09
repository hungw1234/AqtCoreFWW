using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;

namespace AqtCoreFW.Forms.Application.Queries.GetFormTemplates;

/// <summary>Returns paged list of active form templates for a tenant.</summary>
public sealed record GetFormTemplatesQuery(
    Guid TenantId,
    string? SearchTerm = null,
    int Page = 1,
    int PageSize = 20) : IQuery<PagedResult<FormTemplateDto>>;
