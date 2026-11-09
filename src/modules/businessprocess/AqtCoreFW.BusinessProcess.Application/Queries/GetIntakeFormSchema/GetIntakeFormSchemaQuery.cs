using AqtCoreFW.BusinessProcess.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.BusinessProcess.Application.Queries.GetIntakeFormSchema;

/// <summary>
/// Resolve the intake-form pinned snapshot for a BusinessProcessDefinition.
/// Returns null when the BP has no intake form pinned (IntakeFormTemplateId IS NULL by GOV_BP_010);
/// controller maps null → 204 No Content. Missing BP / cross-tenant returns NotFound.
/// </summary>
public sealed record GetIntakeFormSchemaQuery(
    Guid BusinessProcessPublicId,
    Guid TenantId)
    : IRequest<Result<BusinessProcessIntakeFormDto?>>;
