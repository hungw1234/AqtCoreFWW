using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.UpdateFormTemplate;

public sealed class UpdateFormTemplateCommandHandler(IFormTemplateRepository repository)
    : IRequestHandler<UpdateFormTemplateCommand, Result>
{
    public async Task<Result> Handle(UpdateFormTemplateCommand cmd, CancellationToken ct)
    {
        var template = await repository.GetByPublicIdAsync(cmd.PublicId, cmd.TenantId, ct);
        if (template is null)
            return Result.Fail(new NotFoundError($"Form template {cmd.PublicId} not found."));

        template.UpdateInfo(cmd.Name, cmd.NameVi, cmd.UpdatedBy);

        if (cmd.RequiresConsent.HasValue)
            template.SetConsentConfig(cmd.RequiresConsent.Value, cmd.ConsentText, cmd.UpdatedBy);

        // Only update workflow when explicitly requested (avoids accidental clearing on name-only updates)
        if (cmd.UpdateWorkflow)
            template.SetApprovalWorkflow(cmd.ApprovalWorkflowDefinitionId, cmd.UpdatedBy);

        await repository.UpdateAsync(template, ct);
        return Result.Ok();
    }
}
