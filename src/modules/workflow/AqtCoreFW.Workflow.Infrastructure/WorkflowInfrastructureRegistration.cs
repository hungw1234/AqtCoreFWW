using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Application.Workflow;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using AqtCoreFW.Workflow.Application.Repositories;
using AqtCoreFW.Workflow.Application.Services;
using AqtCoreFW.Workflow.Application.Validation;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Jobs;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Workflow.Infrastructure.QueryHandlers;
using AqtCoreFW.Workflow.Infrastructure.Services;
using AqtCoreFW.Workflow.Infrastructure.Validation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Infrastructure;

/// <summary>
/// Registers all Workflow Infrastructure layer services:
/// DbContext, repositories, and Hangfire SLA breach recurring job.
/// Called from host Program.cs: services.AddWorkflowInfrastructure(config)
/// </summary>
public static class WorkflowInfrastructureRegistration
{
    public static IServiceCollection AddWorkflowInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register MediatR handlers from Infrastructure assembly (EF-dependent query handlers
        // such as ListWorkflowInstancesQueryHandler cannot live in Application layer).
        // Follows the Organization module pattern (OrganizationInfrastructureRegistration.cs:43).
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(ListWorkflowInstancesQueryHandler).Assembly));

        services.AddDbContext<WorkflowDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                NonAuditConnectionString.Get(configuration),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "workflow"));

            options.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>());
        });

        services.AddScoped<IWorkflowDefinitionRepository, WorkflowDefinitionRepository>();
        services.AddScoped<IWorkflowInstanceRepository, WorkflowInstanceRepository>();

        // M05: WorkflowTask repository + escalation rule generic repository
        services.AddScoped<IWorkflowTaskRepository, WorkflowTaskRepository>();
        services.AddScoped<IRepository<EscalationRule, long>, Infrastructure.Persistence.EscalationRuleRepository>();

        // M06: Notification config generic repository
        services.AddScoped<IRepository<WorkflowNotificationConfig, long>, Infrastructure.Persistence.WorkflowNotificationConfigRepository>();

        // Phase 3.5: Assignment rule repository
        services.AddScoped<IRepository<WorkflowAssignmentRule, long>, Infrastructure.Persistence.WorkflowAssignmentRuleRepository>();

        // Phase 5: Parallel branching service + repository
        services.AddScoped<IParallelBranchService, ParallelBranchService>();
        services.AddScoped<IParallelBranchRepository, ParallelBranchRepository>();

        // Phase 7C: Transition attachment repository
        services.AddScoped<ITransitionAttachmentRepository, TransitionAttachmentRepository>();

        // T5-08: Delegation repositories
        services.AddScoped<IDelegationRuleRepository, Infrastructure.Persistence.DelegationRuleRepository>();
        services.AddScoped<ITaskAssignmentRepository, Infrastructure.Persistence.TaskAssignmentRepository>();

        // Phase 7E: Dynamic workflow resolver (used by Cases + other modules via SharedKernel interface)
        services.AddScoped<IWorkflowResolver, DynamicWorkflowResolver>();

        // Cross-module: workflow instance creation (called by Forms module)
        services.AddScoped<SharedKernel.Contracts.Clients.IWorkflowModuleClient,
            Clients.InProcessWorkflowModuleClient>();

        // T5-10: Distributed lock for version migration (sp_getapplock wrapper)
        services.AddScoped<ISourceDefinitionLockService, SourceDefinitionLockService>();

        // T5-10: Version migration repository + Hangfire enqueuer abstraction
        services.AddScoped<IWorkflowVersionMigrationRepository, WorkflowVersionMigrationRepository>();
        services.AddScoped<IWorkflowJobEnqueuer, WorkflowJobEnqueuer>();

        // T5-10: Hangfire job state provider (IMonitoringApi abstraction for query handlers)
        services.AddScoped<IHangfireJobStateProvider, HangfireJobStateProvider>();

        // T5-10: Migration executor (Hangfire-resolved via DI)
        services.AddTransient<IWorkflowMigrationExecutor, WorkflowMigrationExecutor>();

        // Register job classes (Hangfire resolves via DI)
        // NOTE: RecurringJob.AddOrUpdate is called post-build in HangfireRegistration.RegisterRecurringJobs()
        services.AddTransient<SlaBreachCheckerJob>();
        services.AddTransient<EscalationCheckJob>();
        services.AddTransient<BranchTimeoutCheckerJob>();
        services.AddTransient<BranchJoinReconcilerJob>();
        services.AddTransient<DelegationExpiryJob>();

        // Phase-02: Entity status enum registry for workflow state validation
        // Singleton — built at startup from all IEntityStatusEnumContributor registrations.
        // Default "Generic" contributor ensures the validator has at least one known type
        // post-Cases removal — consumer modules add their own contributors as needed.
        services.AddSingleton<AqtCoreFW.Workflow.Contracts.Validation.IEntityStatusEnumContributor, Validation.GenericEntityStatusEnumContributor>();
        services.AddSingleton<IEntityStatusEnumRegistry, EntityStatusEnumRegistry>();

        // Phase-02: Lightweight lookup for WorkflowDefinition.EntityType (used by SaveGraph validator)
        services.AddScoped<IWorkflowDefinitionEntityTypeLookup, WorkflowDefinitionEntityTypeLookup>();

        // Tenant-agnostic demo workflow seeder (IHostedService, conditional on config). Seeds one
        // generic 6-state definition per TenantSeedProfiles entry in Draft, each transition carrying a
        // ByRole assignment keyed by per-tenant role code. Default OFF (Development appsettings + the
        // reset-all script enable it). No tenant-existence guard — a definition's TenantId is an
        // unenforced stamp, so a missing tenant yields a harmless never-routed definition, not an orphan.
        services.Configure<Seeding.DemoWorkflowSeederOptions>(
            configuration.GetSection(Seeding.DemoWorkflowSeederOptions.SectionName));
        var demoWorkflowOpts = configuration
            .GetSection(Seeding.DemoWorkflowSeederOptions.SectionName)
            .Get<Seeding.DemoWorkflowSeederOptions>();
        if (demoWorkflowOpts?.Enabled == true)
        {
            services.AddHostedService<Seeding.DemoWorkflowSeeder>();
        }

        return services;
    }
}
