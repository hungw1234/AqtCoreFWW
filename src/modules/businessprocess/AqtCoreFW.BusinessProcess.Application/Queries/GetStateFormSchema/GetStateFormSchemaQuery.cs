using AqtCoreFW.BusinessProcess.Application.DTOs;
using FluentResults;
using MediatR;

namespace AqtCoreFW.BusinessProcess.Application.Queries.GetStateFormSchema;

/// <summary>
/// P8-FU1: resolve the FormTemplate snapshot bound to a (BP, state-form-map) pair so the FE
/// can render fields for the state form panel. The map's PublicId is the AK exposed to FE
/// via BpStateFormMapDto.Id; BP PublicId is required for tenant/scope guard.
///
/// Returns NotFound when: BP missing, cross-tenant, state-form-map missing, or the pinned
/// FormTemplate snapshot is missing (data-integrity). Same shape as
/// <see cref="BusinessProcessIntakeFormDto"/> — reused to avoid duplicate DTO.
/// </summary>
public sealed record GetStateFormSchemaQuery(
    Guid BusinessProcessPublicId,
    Guid StateFormMapPublicId,
    Guid TenantId)
    : IRequest<Result<BusinessProcessIntakeFormDto>>;
