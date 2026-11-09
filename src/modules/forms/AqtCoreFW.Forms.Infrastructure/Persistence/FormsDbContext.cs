using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Forms.Infrastructure.Persistence;

/// <summary>
/// Forms module DB context — owns the "forms" schema.
/// Inherits ModuleDbContext: soft-delete + tenant filters, outbox table, audit interceptors.
/// </summary>
public sealed class FormsDbContext(DbContextOptions<FormsDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "forms";

    public DbSet<FormTemplate> FormTemplates { get; set; } = default!;
    public DbSet<FormTemplateVersion> FormTemplateVersions { get; set; } = default!;
    public DbSet<FormTemplateSignerRole> FormTemplateSignerRoles { get; set; } = default!;
    public DbSet<FormSubmission> FormSubmissions { get; set; } = default!;
    public DbSet<FormSubmissionSignerEvidence> FormSubmissionSignerEvidences { get; set; } = default!;
    public DbSet<FormField> FormFields { get; set; } = default!;
    public DbSet<FormFieldOption> FormFieldOptions { get; set; } = default!;

    // P6-DEFER-1 Phase 03 — orphan-link retry attempts.
    public DbSet<FormSubmissionWorkflowLinkPendingAttempt> FormSubmissionWorkflowLinkPendingAttempts { get; set; } = default!;

    // M03: View metadata
    public DbSet<ViewDefinition> ViewDefinitions { get; set; } = default!;
    public DbSet<ViewColumn> ViewColumns { get; set; } = default!;
    public DbSet<ViewFilter> ViewFilters { get; set; } = default!;
}
