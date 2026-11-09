using AqtCoreFW.Ai.Infrastructure.Persistence;
using AqtCoreFW.AssetManagement.Infrastructure.Persistence;
using AqtCoreFW.Audit.Infrastructure.Persistence;
using AqtCoreFW.BusinessProcess.Infrastructure.Persistence;
using AqtCoreFW.Collaboration.Infrastructure.Persistence;
using AqtCoreFW.Contracts.Infrastructure.Persistence;
using AqtCoreFW.Files.Infrastructure.Persistence;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Infrastructure.Alerting;
using AqtCoreFW.Infrastructure.ApiKeys;
using AqtCoreFW.Infrastructure.Backup;
using AqtCoreFW.Infrastructure.Messaging;
using AqtCoreFW.Infrastructure.Webhooks;
using AqtCoreFW.Integration.Infrastructure.Persistence;
using AqtCoreFW.MasterData.Infrastructure.Persistence;
using AqtCoreFW.Notifications.Infrastructure.Persistence;
using AqtCoreFW.Organization.Infrastructure.Persistence;
using AqtCoreFW.Reporting.Infrastructure.Persistence;
using AqtCoreFW.Rules.Infrastructure.Persistence;
using AqtCoreFW.SystemParams.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.Persistence;

namespace AqtCoreFW.Api;

/// <summary>
/// The ordered set of DbContext types the Api host migrates at startup, declared as data
/// (not inline <c>MigrateAsync</c> calls) so the composition root iterates it AND the
/// architecture test can assert the ADR-010 invariant — AuthServer's startup-migrated set
/// MUST stay a subset of this set — without a brittle text-scan of Program.cs.
///
/// ORDER IS LOAD-BEARING: Program.cs migrates these in sequence. Preserve the original order
/// when editing (a reordering can break cross-context first-deploy assumptions). This list is
/// the single source of truth for "what Api migrates on boot".
/// </summary>
public static class StartupMigrationSets
{
    /// <summary>
    /// Migrated in this exact order by <c>Program.Main</c> when <c>migrationMode.RunMigrations</c>.
    /// </summary>
    public static readonly IReadOnlyList<Type> Contexts =
    [
        typeof(ApiKeyDbContext),
        typeof(IdentityDbContext),
        typeof(AiDbContext),
        typeof(AuditDbContext),
        typeof(FilesDbContext),
        typeof(FormsDbContext),
        typeof(MasterDataDbContext),
        typeof(NotificationsDbContext),
        typeof(OrgDbContext),
        typeof(SystemParamsDbContext),
        typeof(WorkflowDbContext),
        typeof(BusinessProcessDbContext),
        typeof(ReportingDbContext),
        typeof(WebhookDbContext),
        typeof(BackupDbContext),
        typeof(AlertingDbContext),
        typeof(RulesDbContext),
        typeof(CollaborationDbContext),
        typeof(IntegrationDbContext),
        typeof(AssetManagementDbContext),
        typeof(ContractsDbContext),
        typeof(MessagingDbContext),
    ];

    /// <summary>
    /// <see cref="Contexts"/> minus the DbContexts of deferred modules whose
    /// <c>Features:{Name}:Enabled</c> flag is false (see <see cref="DeferredModuleFlags"/>).
    /// Program.cs migrates THIS list; <see cref="Contexts"/> stays the complete declared set so
    /// the architecture guards (subset invariant, per-module wiring) keep asserting against the
    /// full universe regardless of runtime flags. Order of the survivors is preserved.
    /// </summary>
    public static IReadOnlyList<Type> EnabledContexts(IConfiguration configuration)
    {
        var disabled = DeferredModuleFlags.DisabledDbContexts(configuration);
        return disabled.Count == 0
            ? Contexts
            : Contexts.Where(t => !disabled.Contains(t)).ToList();
    }
}
