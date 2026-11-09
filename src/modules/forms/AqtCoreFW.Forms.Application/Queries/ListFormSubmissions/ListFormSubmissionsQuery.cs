using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Application.Pagination;

namespace AqtCoreFW.Forms.Application.Queries.ListFormSubmissions;

/// <summary>
/// Lists submissions filtered by templatePublicId and/or approval status.
/// TemplatePublicId = Guid? (FormTemplate.PublicId T2 AK — external addressing).
/// FieldFilters: JSON-encoded array of [{fieldKey, value}] for submission data filtering.
/// At least one filter is expected; returns all tenant submissions if neither provided.
/// </summary>
public sealed record ListFormSubmissionsQuery(
    Guid TenantId,
    Guid? TemplatePublicId,
    string? Status = null,
    string? SearchTerm = null,
    int Page = 1,
    int PageSize = 20,
    string? FieldFiltersJson = null) : IQuery<PagedResult<FormSubmissionListItemDto>>;

/// <summary>Single field-value filter for submission search.</summary>
public sealed record SubmissionFieldFilter(string FieldKey, string Value);
