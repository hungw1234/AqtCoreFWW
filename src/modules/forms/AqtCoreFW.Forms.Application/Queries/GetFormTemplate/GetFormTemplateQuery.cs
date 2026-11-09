using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.GetFormTemplate;

/// <summary>
/// Returns a single form template schema by public Guid AK (all non-deleted statuses).
/// TemplatePublicId = FormTemplate.PublicId (T2 external addressing key).
/// </summary>
public sealed record GetFormTemplateQuery(Guid TemplatePublicId, Guid TenantId) : IQuery<FormTemplateDto>;
