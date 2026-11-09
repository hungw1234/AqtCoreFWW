using AqtCoreFW.Forms.Application.Commands.PublishFormTemplate;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.Forms.Infrastructure.BackgroundServices;
using AqtCoreFW.Forms.Infrastructure.Jobs;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Forms.Infrastructure.Services;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Rtbf;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Channels;

namespace AqtCoreFW.Forms.Infrastructure;

public static class FormsInfrastructureRegistration
{
    public static IServiceCollection AddFormsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // EF Core DbContext — forms schema
        services.AddDbContext<FormsDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                NonAuditConnectionString.Get(configuration),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "forms"));

            options.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>());
        });

        // Repositories
        services.AddScoped<IFormTemplateRepository, FormTemplateRepository>();
        services.AddScoped<IFormSubmissionRepository, FormSubmissionRepository>();
        services.AddScoped<IFormFieldRepository, FormFieldRepository>();
        services.AddScoped<IViewDefinitionRepository, ViewDefinitionRepository>();
        services.AddScoped<IFormSubmissionWorkflowLinkPendingAttemptRepository,
                           FormSubmissionWorkflowLinkPendingAttemptRepository>();

        // Phase 04: orphan-workflow-link retry job — options bind + DI registration. The job itself
        // only depends on IServiceScopeFactory/IOptions/ILogger (all container-resolvable) and opens
        // its OWN child DI scope per claimed row (see job doc) — Transient mirrors the sibling
        // Workflow recurring jobs (SlaBreachCheckerJob/EscalationCheckJob) that don't need a shared
        // scoped DbContext across their own construction.
        services.Configure<FormWorkflowLinkRetryOptions>(
            configuration.GetSection(FormWorkflowLinkRetryOptions.SectionName));
        services.AddTransient<FormWorkflowLinkRetryJob>();

        // Validation service
        services.AddScoped<IFormValidationService, FormValidationService>();

        // Dual-mode storage implementations (registered as concrete types for factory resolution)
        services.AddScoped<JsonFormDataStorage>();
        services.AddScoped<TableFormDataStorage>();
        services.AddScoped<IFormDataStorageFactory, FormDataStorageFactory>();

        // DDL migrator for Table-mode forms
        services.AddScoped<IFormTableMigrator, FormTableMigrator>();

        // Phase 3 — Child table row read/write for TableField in Table mode
        services.AddScoped<ITableFieldDataRepository, TableFieldDataRepository>();

        // Phase 2 — Data source resolvers
        services.AddScoped<EnumDataSourceResolver>();
        services.AddScoped<InternalDataSourceResolver>();
        services.AddScoped<ExternalDataSourceResolver>();
        services.AddScoped<IDataSourceResolverFactory, DataSourceResolverFactory>();

        // H2 — Vault-backed API key resolver for AUTHENTICATED external data sources. IVaultClient is an
        // OPTIONAL ctor dependency (registered only on the Api host via AddVaultKeyResolver); on a host or
        // test context without Vault, the resolver is built with a null client and every authed-external
        // lookup fails closed (empty options). The static external path never touches it.
        services.AddScoped<ExternalDataSourceAuthKeyResolver>();

        // Phase 2 — Formula evaluation (NCalcSync)
        services.AddScoped<IFormulaEvaluationService, FormulaEvaluationService>();

        // HTTP client for ExternalDataSourceResolver — 10s timeout, no base address (per-field ApiUrl)
        // M-01: AllowAutoRedirect=false prevents redirect-to-internal SSRF bypass
        services.AddHttpClient("FormsExternalDataSource", c =>
        {
            c.Timeout = TimeSpan.FromSeconds(10);
        })
        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
        {
            AllowAutoRedirect = false
        });

        // M-04: Bounded channel with backpressure (was unbounded — overflow risk)
        services.AddSingleton(Channel.CreateBounded<long>(new BoundedChannelOptions(100)
        {
            SingleReader = true,  // Only FormMaterializationService reads
            SingleWriter = false, // Multiple command handlers can enqueue
            FullMode = BoundedChannelFullMode.Wait
        }));

        // Background materialization service — implements both BackgroundService + IMaterializationQueue
        services.AddSingleton<FormMaterializationService>();
        services.AddHostedService(sp => sp.GetRequiredService<FormMaterializationService>());
        services.AddSingleton<IMaterializationQueue>(
            sp => sp.GetRequiredService<FormMaterializationService>());

        // Cross-module: form submission status updates (called by Workflow module)
        services.AddScoped<SharedKernel.Contracts.Clients.IFormSubmissionStatusClient,
            Clients.InProcessFormSubmissionStatusClient>();

        // Cross-module: BP-Run scoped form submission ops (called by BusinessProcess module — P3/P4/P6)
        services.AddScoped<SharedKernel.Contracts.Clients.IFormSubmissionRunClient,
            Clients.InProcessFormSubmissionRunClient>();

        // Cross-module: FormTemplate snapshot lookup by internal FK (called by BusinessProcess — BP MVP P4 Slice 2B GetIntakeFormSchema)
        services.AddScoped<SharedKernel.Contracts.Clients.IFormTemplateQueryClient,
            Clients.InProcessFormTemplateQueryClient>();

        // PDF generation — QuestPDF, community license set in FormSubmissionPdfGenerator static ctor
        services.AddScoped<IFormSubmissionPdfGenerator, FormSubmissionPdfGenerator>();

        // Shared PDF composition service (load + merge + generate) — consumed by export query and sign command
        services.AddScoped<ISubmissionPdfComposer, SubmissionPdfComposer>();

        // RTBF PII anonymizer
        services.AddScoped<IModulePiiAnonymizer, FormsPiiAnonymizer>();

        // Tenant-agnostic demo form-template seeder (IHostedService, conditional on config). Seeds one
        // published intake template per TenantSeedProfiles entry with a diverse field set (incl. a
        // Select-with-options). Default OFF (Development appsettings + the reset-all script enable it).
        // No tenant-existence guard — a template's TenantId is an unenforced stamp, so a missing tenant
        // yields a harmless never-used template, not an orphan.
        services.Configure<Seeding.DemoFormTemplateSeederOptions>(
            configuration.GetSection(Seeding.DemoFormTemplateSeederOptions.SectionName));
        var demoFormOpts = configuration
            .GetSection(Seeding.DemoFormTemplateSeederOptions.SectionName)
            .Get<Seeding.DemoFormTemplateSeederOptions>();
        if (demoFormOpts?.Enabled == true)
        {
            services.AddHostedService<Seeding.DemoFormTemplateSeeder>();
        }

        return services;
    }
}
