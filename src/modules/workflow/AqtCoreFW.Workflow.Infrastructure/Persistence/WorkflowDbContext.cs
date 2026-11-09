using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Workflow.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Workflow.Infrastructure.Persistence;

/// <summary>
/// Workflow module DB context — owns the "workflow" schema.
/// Inherits ModuleDbContext: soft-delete + tenant filters, outbox table, audit interceptors.
/// SaveChangesAsync() is called directly on DbContext within handlers that need atomic saves.
/// </summary>
public sealed class WorkflowDbContext(DbContextOptions<WorkflowDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "workflow";

    public DbSet<WorkflowDefinition> WorkflowDefinitions { get; set; } = default!;
    public DbSet<WorkflowState> WorkflowStates { get; set; } = default!;
    public DbSet<WorkflowTransition> WorkflowTransitions { get; set; } = default!;
    public DbSet<WorkflowInstance> WorkflowInstances { get; set; } = default!;
    public DbSet<WorkflowInstanceHistory> WorkflowInstanceHistories { get; set; } = default!;

    // M05: Workflow Task entities
    public DbSet<WorkflowTask> WorkflowTasks { get; set; } = default!;
    public DbSet<TaskAssignment> TaskAssignments { get; set; } = default!;
    public DbSet<EscalationRule> EscalationRules { get; set; } = default!;

    // M06: Notification integration
    public DbSet<WorkflowNotificationConfig> WorkflowNotificationConfigs { get; set; } = default!;

    // Phase 3.5: Tenant-workflow assignment configuration
    public DbSet<WorkflowAssignmentRule> WorkflowAssignmentRules { get; set; } = default!;

    // Plan 260519-0421 v2 (P1): per-transition handler-assignment rule (config; no RLS)
    // + tenant-scoped runtime candidate read-model (RLS-policied).
    public DbSet<WorkflowTransitionAssignment> WorkflowTransitionAssignments { get; set; } = default!;
    public DbSet<WorkflowTransitionAssignmentClause> WorkflowTransitionAssignmentClauses { get; set; } = default!;
    public DbSet<WorkflowTaskCandidateIndex> WorkflowTaskCandidateIndex { get; set; } = default!;

    // Phase 5: Parallel branching — definition layer
    public DbSet<WorkflowParallelBranch> ParallelBranches => Set<WorkflowParallelBranch>();
    public DbSet<WorkflowBranchChild> BranchChildren => Set<WorkflowBranchChild>();
    public DbSet<WorkflowBranchResolution> BranchResolutions => Set<WorkflowBranchResolution>();

    // Per-branch-child handler-assignment rule (config; no RLS) — parallel-fork analogue of
    // WorkflowTransitionAssignment. Feeds the fork fan-out → candidate-index (pooled/claimable child task).
    public DbSet<WorkflowBranchChildAssignment> BranchChildAssignments => Set<WorkflowBranchChildAssignment>();
    public DbSet<WorkflowBranchChildAssignmentClause> BranchChildAssignmentClauses => Set<WorkflowBranchChildAssignmentClause>();

    // Phase 5: Parallel branching — runtime layer
    public DbSet<WorkflowInstanceBranchStatus> InstanceBranchStatuses => Set<WorkflowInstanceBranchStatus>();
    public DbSet<WorkflowBranchChildStatus> BranchChildStatuses => Set<WorkflowBranchChildStatus>();

    // Phase 7C: Transition attachments — files linked to a history entry
    public DbSet<WorkflowTransitionAttachment> TransitionAttachments => Set<WorkflowTransitionAttachment>();

    // T5-08: Workflow delegation rules — time-bounded user-to-user task delegation
    public DbSet<DelegationRule> DelegationRules { get; set; } = default!;

    // T5-10: Version migration — parent + per-instance guard table
    public DbSet<WorkflowVersionMigration> VersionMigrations { get; set; } = default!;
    public DbSet<WorkflowVersionMigrationItem> VersionMigrationItems { get; set; } = default!;
}
