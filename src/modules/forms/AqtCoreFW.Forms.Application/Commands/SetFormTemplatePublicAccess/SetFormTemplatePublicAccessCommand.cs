using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.SetFormTemplatePublicAccess;

/// <summary>
/// Opt a form template into (or out of) anonymous public reachability.
/// Drives the FormTemplates RLS public-read carve-out (IsPublic=1 rows are
/// admitted to anonymous by-Code resolution under the public_forms_bypass flag).
/// PublicId = FormTemplate.PublicId (Guid AK — external addressing).
/// Server-controlled: IsPublic is set from the authenticated admin action, never
/// from anonymous/client-supplied template data.
/// </summary>
public sealed record SetFormTemplatePublicAccessCommand(
    Guid PublicId,
    Guid TenantId,
    bool IsPublic,
    Guid UpdatedBy) : IRequest<Result>;
