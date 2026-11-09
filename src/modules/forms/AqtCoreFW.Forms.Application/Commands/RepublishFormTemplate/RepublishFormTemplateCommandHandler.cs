using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Exceptions;
using AqtCoreFW.Forms.Domain.Repositories;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Forms.Application.Commands.RepublishFormTemplate;

/// <summary>
/// Re-publishes an already-Active template, emitting a fresh FormTemplateVersion snapshot.
/// Mirrors PublishFormTemplateCommandHandler atomicity (AddVersionAsync stages, UpdateAsync
/// triggers the single SaveChanges that flushes both). Materialization queue is NOT
/// re-triggered — Active Table templates already have their backing storage; DDL diff/apply
/// is out of scope (post-MVP).
/// </summary>
public sealed class RepublishFormTemplateCommandHandler(
    IFormTemplateRepository repository,
    ILogger<RepublishFormTemplateCommandHandler> logger)
    : IRequestHandler<RepublishFormTemplateCommand, Result>
{
    public async Task<Result> Handle(
        RepublishFormTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var template = await repository.GetByPublicIdWithFieldsAsync(
            request.TemplatePublicId, request.TenantId, cancellationToken);

        if (template is null)
            return Result.Fail(FormErrors.TemplateNotFound(request.TemplatePublicId));

        if (template.TenantId != request.TenantId)
            return Result.Fail(FormErrors.AccessDenied());

        try
        {
            template.Republish(request.UpdatedBy);
        }
        catch (InvalidFormSchemaException ex)
        {
            return Result.Fail(new SharedKernel.Errors.ValidationError(ex.Message, "Status"));
        }

        // BP MVP P1-FU: factory MUST read template.Version AFTER Republish() bumps it,
        // so VersionNumber on the snapshot is the freshly-bumped value (guarantees uniqueness
        // on the (FormTemplateId, VersionNumber) index even across rapid sequential calls).
        var snapshot = FormTemplateVersionFactory.Snapshot(template, request.UpdatedBy);
        await repository.AddVersionAsync(snapshot, cancellationToken);

        await repository.UpdateAsync(template, cancellationToken);

        logger.LogInformation(
            "FormTemplate {TemplateId} republished as v{Version} (tenant={TenantId}); snapshot SchemaJson={SchemaBytes}B",
            template.PublicId, template.Version, template.TenantId, snapshot.SchemaJson.Length);

        return Result.Ok();
    }
}
