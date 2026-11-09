using AqtCoreFW.Files.Application.Jobs;
using AqtCoreFW.Files.Application.Options;
using AqtCoreFW.Files.Domain.Entities;
using AqtCoreFW.Files.Domain.Repositories;
using AqtCoreFW.Files.Domain.Services;
using AqtCoreFW.Files.Infrastructure.Clients;
using AqtCoreFW.Files.Infrastructure.Jobs;
using AqtCoreFW.Files.Infrastructure.Options;
using AqtCoreFW.Files.Infrastructure.Persistence;
using AqtCoreFW.Files.Infrastructure.Security;
using AqtCoreFW.Files.Infrastructure.Services;
using AqtCoreFW.Files.Infrastructure.Storage;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Minio;
using nClam;

namespace AqtCoreFW.Files.Infrastructure;

public static class FilesInfrastructureRegistration
{
    public static IServiceCollection AddFilesInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // EF Core — "files" schema
        services.AddDbContext<FilesDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                NonAuditConnectionString.Get(configuration),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "files"));

            options.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>());
        });

        // Options
        services.Configure<MinioOptions>(configuration.GetSection(MinioOptions.SectionName));
        services.Configure<ClamAvOptions>(configuration.GetSection(ClamAvOptions.SectionName));
        services.Configure<FilesOptions>(configuration.GetSection(FilesOptions.SectionName));

        // MinIO client (singleton — thread-safe, connection-pooled)
        services.AddSingleton<IMinioClient>(sp =>
        {
            var opts = configuration.GetSection(MinioOptions.SectionName).Get<MinioOptions>()
                       ?? new MinioOptions();
            return new MinioClient()
                .WithEndpoint(opts.Endpoint)
                .WithCredentials(opts.AccessKey, opts.SecretKey)
                .WithSSL(opts.UseSSL)
                .Build();
        });

        // Storage + security services
        services.AddScoped<IFileStorageService, MinioFileStorageService>();
        services.AddScoped<IVirusScanner, ClamAvVirusScanner>();
        services.AddScoped<IFileSecurityService, FileSecurityService>();
        // NĐ68 signature VERIFICATION seam (IDigitalSignatureService + stub) removed 2026-07-23:
        // zero callers repo-wide, and its stub returned IsValid=true — a fake pass-through if
        // ever wired. Real signing lives in Identity (USB/Mysign/PAdES providers). Recover via
        // `git log -S IDigitalSignatureService` if NĐ68 verification becomes a real requirement.

        // Repository
        services.AddScoped<IFileRepository, FileRepository>();

        // Hangfire scan job
        services.AddScoped<IClamAvScanJob, ClamAvScanJob>();

        // M08: Document lifecycle generic repositories.
        // Phase-4b B3b: T1/T3 entities use long TId (bigint identity PK).
        // GenericFilesRepository<T> → GenericRepository<T, long> to match Entity.Id (long).
        services.AddScoped<IRepository<FileVersion, long>, GenericFilesRepository<FileVersion>>();
        services.AddScoped<IRepository<DocumentTemplate, long>, GenericFilesRepository<DocumentTemplate>>();
        services.AddScoped<IRepository<RetentionPolicy, long>, GenericFilesRepository<RetentionPolicy>>();
        services.AddScoped<IRepository<RecordLifecycle, long>, GenericFilesRepository<RecordLifecycle>>();

        // M08: Scriban template engine + enforcement job
        services.AddScoped<ITemplateEngine, ScribanTemplateEngine>();
        services.AddTransient<RetentionPolicyEnforcementJob>();

        // F-04: Use Clients/InProcessFilesModuleClient (repository-based, domain-aligned)
        services.AddScoped<IFilesModuleClient, InProcessFilesModuleClient>();

        // F-03: ClamAV startup health validation — logs CRITICAL if unreachable in production
        services.AddHostedService<ClamAvStartupHealthCheck>();

        return services;
    }
}

/// <summary>
/// F-03: Startup hosted service that validates ClamAV reachability — logs a CRITICAL warning if clamd
/// is unreachable. Warning only — does not block startup — to avoid cascading failures on transient
/// network issues. Skipped in the Benchmark profile, which deliberately runs without a clamd daemon
/// (scanning is also feature-flag-off there, so no uploads are scanned regardless).
/// </summary>
internal sealed class ClamAvStartupHealthCheck(
    IOptions<ClamAvOptions> options,
    IHostEnvironment hostEnvironment,
    ILogger<ClamAvStartupHealthCheck> logger) : IHostedService
{
    private const string BenchmarkEnvironment = "Benchmark";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var opts = options.Value;

        // Benchmark load-test profile provisions no clamd — skip the probe so it doesn't emit a
        // misleading CRITICAL. Scanning is feature-flag-off there anyway (nothing is scanned).
        if (hostEnvironment.IsEnvironment(BenchmarkEnvironment))
        {
            logger.LogInformation("ClamAV startup health check skipped (Benchmark profile — no scanner provisioned).");
            return;
        }

        try
        {
            var client = new ClamClient(opts.Host, opts.Port);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));

            var pingResult = await client.PingAsync(cts.Token);
            if (pingResult)
            {
                logger.LogInformation("ClamAV health check passed. Host={Host}:{Port}", opts.Host, opts.Port);
            }
            else
            {
                logger.LogCritical(
                    "SECURITY WARNING: ClamAV is unreachable at {Host}:{Port}. " +
                    "Virus scanning will FAIL for all uploaded files. Investigate immediately.",
                    opts.Host, opts.Port);
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex,
                "SECURITY WARNING: ClamAV startup health check failed. Host={Host}:{Port}. " +
                "Virus scanning may be unavailable. Investigate immediately.",
                opts.Host, opts.Port);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
