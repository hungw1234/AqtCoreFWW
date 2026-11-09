using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Notifications.Application.Providers;
using AqtCoreFW.Notifications.Application.Services;
using AqtCoreFW.Notifications.Domain.Repositories;
using AqtCoreFW.Notifications.Infrastructure.Email;
using AqtCoreFW.Notifications.Infrastructure.Persistence;
using AqtCoreFW.Notifications.Infrastructure.Rendering;
using AqtCoreFW.Notifications.Infrastructure.Sms;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Notifications.Infrastructure;

public static class NotificationsInfrastructureRegistration
{
    public static IServiceCollection AddNotificationsInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Database
        services.AddDbContext<NotificationsDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                NonAuditConnectionString.Get(configuration),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "notifications"));

            options.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>());
        });

        // Repositories
        services.AddScoped<INotificationRepository, NotificationRepository>();
        services.AddScoped<INotificationTemplateRepository, NotificationTemplateRepository>();
        services.AddScoped<INotificationLogRepository, NotificationLogRepository>();

        // Email (MailKit SMTP)
        services.Configure<SmtpOptions>(configuration.GetSection(SmtpOptions.SectionName));
        services.AddScoped<IEmailSender, MailKitEmailSender>();

        // Address-based alert email seam consumed by shared infrastructure alerting.
        services.AddScoped<IAlertEmailSender, AlertEmailSenderAdapter>();

        // SMS (HTTP webhook)
        services.Configure<SmsWebhookOptions>(configuration.GetSection(SmsWebhookOptions.SectionName));
        services.AddHttpClient("SmsWebhook", (sp, client) =>
        {
            var opts = configuration.GetSection(SmsWebhookOptions.SectionName).Get<SmsWebhookOptions>();
            if (opts is not null && !string.IsNullOrEmpty(opts.BearerToken))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", opts.BearerToken);
        });
        services.AddScoped<ISmsProvider, WebhookSmsProvider>();

        // Scriban template renderer
        services.AddScoped<ITemplateRenderer, ScribanTemplateRenderer>();

        // SignalR (Redis backplane added in Program.cs where Redis config is available)
        services.AddSignalR();

        // Seed default notification templates (idempotent — GOV-branded HTML email + InApp + SMS)
        services.AddHostedService<NotificationTemplateSeeder>();

        return services;
    }
}
