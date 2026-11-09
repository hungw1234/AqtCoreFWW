using AqtCoreFW.BusinessProcess.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.BusinessProcess.Application.Queries.GetBusinessProcessById;

/// <summary>
/// Get BusinessProcessDefinition by external PublicId — returns root + StateForms + TransitionForms.
/// TenantId is filled from controller's ResolveTenantId(); cross-tenant access denied unless SystemAdmin.
/// </summary>
public sealed record GetBusinessProcessByIdQuery(
    Guid BusinessProcessPublicId,
    Guid TenantId)
    : IRequest<Result<BusinessProcessDetailDto>>;
