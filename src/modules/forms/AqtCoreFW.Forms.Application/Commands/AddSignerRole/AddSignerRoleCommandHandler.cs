using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Exceptions;
using AqtCoreFW.Forms.Domain.Repositories;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.AddSignerRole;

public sealed class AddSignerRoleCommandHandler(
    IFormTemplateRepository templateRepository)
    : IRequestHandler<AddSignerRoleCommand, Result<FormTemplateSignerRoleDto>>
{
    public async Task<Result<FormTemplateSignerRoleDto>> Handle(
        AddSignerRoleCommand request,
        CancellationToken cancellationToken)
    {
        // Load template with signer roles for domain duplicate-key validation
        var template = await templateRepository.GetByPublicIdWithSignerRolesAsync(
            request.TemplatePublicId, request.TenantId, cancellationToken);

        if (template is null)
            return Result.Fail(FormErrors.TemplateNotFound(request.TemplatePublicId));

        Domain.Entities.FormTemplateSignerRole role;
        try
        {
            role = template.AddSignerRole(
                request.RoleKey,
                request.RoleLabelVi,
                request.DisplayOrder,
                request.IsRequired,
                request.PlacementJson,
                request.UpdatedBy);
        }
        catch (InvalidFormSchemaException ex)
        {
            return Result.Fail(new SharedKernel.Errors.ValidationError(ex.Message, "RoleKey"));
        }

        await templateRepository.UpdateAsync(template, cancellationToken);

        return Result.Ok(new FormTemplateSignerRoleDto(
            role.Id,
            role.RoleKey,
            role.RoleLabelVi,
            role.DisplayOrder,
            role.IsRequired,
            role.PlacementJson));
    }
}
