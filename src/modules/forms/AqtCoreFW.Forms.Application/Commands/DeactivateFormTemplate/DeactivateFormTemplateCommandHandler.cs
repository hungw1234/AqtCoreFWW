using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;

namespace AqtCoreFW.Forms.Application.Commands.DeactivateFormTemplate;

public sealed class DeactivateFormTemplateCommandHandler(IFormTemplateRepository repository)
    : IRequestHandler<DeactivateFormTemplateCommand, Result>
{
    public async Task<Result> Handle(DeactivateFormTemplateCommand cmd, CancellationToken ct)
    {
        // Use GetByPublicIdWithFieldsAsync (no status filter) — allows deactivating Draft or Active templates
        var template = await repository.GetByPublicIdWithFieldsAsync(cmd.PublicId, cmd.TenantId, ct);
        if (template is null)
            return Result.Fail(new NotFoundError($"Form template {cmd.PublicId} not found."));

        template.Deactivate(cmd.UpdatedBy);
        await repository.UpdateAsync(template, ct);
        return Result.Ok();
    }
}
