using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.DeactivateFormTemplate;

/// <summary>
/// Deactivate (soft-delete) a form template — no longer accepts submissions.
/// PublicId = FormTemplate.PublicId (Guid AK — external addressing).
/// </summary>
public sealed record DeactivateFormTemplateCommand(
    Guid PublicId,
    Guid TenantId,
    Guid UpdatedBy) : IRequest<Result>;
