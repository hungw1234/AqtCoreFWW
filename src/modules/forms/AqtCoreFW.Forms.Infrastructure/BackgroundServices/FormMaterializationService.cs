using AqtCoreFW.Forms.Application.Commands.PublishFormTemplate;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace AqtCoreFW.Forms.Infrastructure.BackgroundServices;

/// <summary>
/// Background service that processes Table-mode form materialization requests.
/// Flow: PublishFormTemplateCommandHandler enqueues templateId → this service dequeues →
///       creates Submissions_{id:N} table + v_{code} view → marks template Active.
/// Uses System.Threading.Channels for in-process, low-latency queuing.
/// On failure: logs error; template remains in Materializing status for manual recovery.
/// </summary>
public sealed class FormMaterializationService(
    Channel<long> channel,
    IServiceScopeFactory scopeFactory,
    ILogger<FormMaterializationService> logger)
    : BackgroundService, IMaterializationQueue
{
    // System user ID used for audit when background service transitions status
    private static readonly Guid SystemUserId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    /// <summary>Enqueue a template ID (long, T1 PK) for DDL materialization (called from command handler).</summary>
    public async Task EnqueueAsync(long templateId, CancellationToken cancellationToken = default)
    {
        await channel.Writer.WriteAsync(templateId, cancellationToken);
        logger.LogInformation("Enqueued template {TemplateId} for DDL materialization.", templateId);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("FormMaterializationService started.");

        // On startup: re-enqueue any templates stuck in Materializing (process restart recovery)
        await RecoverMaterializingTemplatesAsync(stoppingToken);

        await foreach (var templateId in channel.Reader.ReadAllAsync(stoppingToken))
        {
            await MaterializeAsync(templateId, stoppingToken);
        }

        logger.LogInformation("FormMaterializationService stopped.");
    }

    private async Task MaterializeAsync(long templateId, CancellationToken stoppingToken)
    {
        logger.LogInformation("Materializing table-mode form template {TemplateId}.", templateId);

        using var scope = scopeFactory.CreateScope();
        var templateRepo = scope.ServiceProvider.GetRequiredService<IFormTemplateRepository>();
        var migrator = scope.ServiceProvider.GetRequiredService<IFormTableMigrator>();

        try
        {
            // Load template with fields (no tenant filter needed — system operation)
            var templates = await templateRepo.GetMaterializingAsync(stoppingToken);
            var template = templates.FirstOrDefault(t => t.Id == templateId);

            if (template is null)
            {
                logger.LogWarning("Template {TemplateId} not found or no longer Materializing.", templateId);
                return;
            }

            var activeFields = template.Fields.Where(f => f.IsActive).ToList();

            // Step 1: Create dynamic parent table
            await migrator.CreateTableAsync(templateId, activeFields, stoppingToken);

            // Step 2: Create or replace parent BI view
            await migrator.CreateOrReplaceViewAsync(template.Code, templateId, activeFields, stoppingToken);

            // Step 3: Create child tables + views for each TableField
            var tableFields = activeFields.Where(f => f.Type == FormFieldType.TableField).ToList();
            foreach (var tableField in tableFields)
            {
                var childFields = activeFields.Where(f => f.ParentFieldId == tableField.Id).ToList();
                await migrator.CreateChildTableAsync(templateId, tableField, childFields, stoppingToken);
                await migrator.CreateOrReplaceChildViewAsync(
                    template.Code, templateId, tableField, childFields, stoppingToken);
            }

            // Step 4: Mark template Active
            template.MarkActive(SystemUserId);
            await templateRepo.UpdateAsync(template, stoppingToken);

            logger.LogInformation(
                "Template {TemplateId} materialized successfully. Status → Active.", templateId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to materialize template {TemplateId}. Template remains in Materializing status.",
                templateId);
            // Template stays in Materializing — admin can retry via re-publish or manual recovery
        }
    }

    /// <summary>Re-enqueue templates stuck in Materializing after process restart.</summary>
    private async Task RecoverMaterializingTemplatesAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var templateRepo = scope.ServiceProvider.GetRequiredService<IFormTemplateRepository>();

        var stuck = await templateRepo.GetMaterializingAsync(stoppingToken);
        if (stuck.Count == 0) return;

        logger.LogInformation(
            "Recovering {Count} Materializing templates from previous session.", stuck.Count);

        foreach (var template in stuck)
            await channel.Writer.WriteAsync(template.Id, stoppingToken);
    }
}
