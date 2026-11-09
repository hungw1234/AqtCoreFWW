using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Exceptions;
using AqtCoreFW.Forms.Domain.Repositories;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Forms.Application.Commands.PublishFormTemplate;

/// <summary>
/// Publishes a Draft template.
/// Json mode: status moves directly Draft → Active.
/// Table mode: status moves Draft → Materializing; FormMaterializationService completes DDL async.
/// </summary>
public sealed class PublishFormTemplateCommandHandler(
    IFormTemplateRepository repository,
    IMaterializationQueue materializationQueue,
    ILogger<PublishFormTemplateCommandHandler> logger)
    : IRequestHandler<PublishFormTemplateCommand, Result>
{
    public async Task<Result> Handle(
        PublishFormTemplateCommand request,
        CancellationToken cancellationToken)
    {
        var template = await repository.GetByPublicIdWithFieldsAsync(
            request.TemplatePublicId, request.TenantId, cancellationToken);

        if (template is null)
            return Result.Fail(FormErrors.TemplateNotFound(request.TemplatePublicId));

        if (template.TenantId != request.TenantId)
            return Result.Fail(FormErrors.AccessDenied());

        // BP MVP P1: publish + capture immutable schema snapshot atomically with the status flip.
        // PublishAndSnapshot pairs the two so the status-Active-without-snapshot drift cannot recur
        // (see FormTemplateVersionFactory.PublishAndSnapshot). AddVersionAsync only stages the
        // entity; UpdateAsync triggers the single SaveChanges that flushes both inserts in one txn.
        FormTemplateVersion snapshot;
        try
        {
            snapshot = FormTemplateVersionFactory.PublishAndSnapshot(template, request.UpdatedBy);
        }
        catch (InvalidFormSchemaException ex)
        {
            return Result.Fail(new SharedKernel.Errors.ValidationError(ex.Message, "Status"));
        }

        await repository.AddVersionAsync(snapshot, cancellationToken);

        await repository.UpdateAsync(template, cancellationToken);

        logger.LogInformation(
            "FormTemplate {TemplateId} v{Version} published (tenant={TenantId}, status={Status}); snapshot SchemaJson={SchemaBytes}B",
            template.PublicId, template.Version, template.TenantId, template.Status,
            snapshot.SchemaJson.Length);

        // Table mode: enqueue background DDL job after saving Materializing status
        if (template.Status == FormTemplateStatus.Materializing)
            await materializationQueue.EnqueueAsync(template.Id, cancellationToken);

        return Result.Ok();
    }
}

/// <summary>
/// In-process queue for background DDL materialization requests.
/// Implemented by FormMaterializationService in Infrastructure.
/// templateId = FormTemplate.Id (long, T1 internal PK).
/// </summary>
public interface IMaterializationQueue
{
    Task EnqueueAsync(long templateId, CancellationToken cancellationToken = default);
}
