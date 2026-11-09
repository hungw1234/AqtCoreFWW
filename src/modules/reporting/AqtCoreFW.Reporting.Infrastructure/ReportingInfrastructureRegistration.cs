using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Reporting.Application.Abstractions;
using AqtCoreFW.Reporting.Application.Jobs;
using AqtCoreFW.Reporting.Domain.Repositories;
using AqtCoreFW.Reporting.Infrastructure.Export;
using AqtCoreFW.Reporting.Infrastructure.Jobs;
using AqtCoreFW.Reporting.Infrastructure.Persistence;
using AqtCoreFW.Reporting.Infrastructure.Seeding;
using AqtCoreFW.Reporting.Infrastructure.Services;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Reporting.Infrastructure;

public static class ReportingInfrastructureRegistration
{
    public static IServiceCollection AddReportingInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<ReportingDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                NonAuditConnectionString.Get(configuration),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "reporting"));

            options.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>());
        });

        // Repositories
        services.AddScoped<IReportDefinitionRepository, ReportDefinitionRepository>();
        services.AddScoped<IReportExecutionRepository, ReportExecutionRepository>();
        services.AddScoped<IQueryCatalogRepository, QueryCatalogRepository>();
        services.AddScoped<IReportWidgetRepository, ReportWidgetRepository>();
        services.AddScoped<IDashboardLayoutRepository, DashboardLayoutRepository>();

        // Export generators
        services.AddSingleton<IReportExcelGenerator, ReportExcelGenerator>();
        services.AddSingleton<IReportPdfGenerator, ReportPdfGenerator>();
        services.AddSingleton<ICompliancePdfGenerator, CompliancePdfGenerator>();

        // Hangfire jobs + storage
        services.AddScoped<IExecuteReportJob, ExecuteReportJob>();
        services.AddScoped<IReportStorageService, ReportStorageService>();
        // E-06: Cleanup job registered as scoped — Hangfire resolves per execution
        services.AddScoped<ReportCleanupJob>();

        // Tenant-agnostic demo reporting seeder (IHostedService, conditional on config). Seeds one report
        // definition + 1 widget + 1 dashboard per TenantSeedProfiles entry plus one global query-catalog
        // entry. Default OFF (Development appsettings + the reset-all script enable it). Self-contained — no
        // cross-module deps; the report definition's tenant scope is a string slug, the widget/dashboard are
        // Guid-tenant-scoped, and the catalog entry is system-wide.
        services.Configure<DemoReportingSeederOptions>(
            configuration.GetSection(DemoReportingSeederOptions.SectionName));
        var demoReportingOpts = configuration
            .GetSection(DemoReportingSeederOptions.SectionName)
            .Get<DemoReportingSeederOptions>();
        if (demoReportingOpts?.Enabled == true)
        {
            services.AddHostedService<DemoReportingSeeder>();
        }

        return services;
    }

    /// <summary>
    /// Registers Hangfire recurring jobs for the Reporting module.
    /// Called from the API host startup after Hangfire server is configured.
    /// E-06: ReportCleanupJob runs nightly at 02:00 UTC.
    /// </summary>
    public static void AddReportingRecurringJobs()
    {
        RecurringJob.AddOrUpdate<ReportCleanupJob>(
            recurringJobId: "reporting.report-cleanup",
            methodCall: j => j.ExecuteAsync(CancellationToken.None),
            cronExpression: "0 2 * * *"); // nightly at 02:00 UTC
    }
}
