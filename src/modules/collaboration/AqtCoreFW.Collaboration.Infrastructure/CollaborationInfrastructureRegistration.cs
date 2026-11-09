using AqtCoreFW.Collaboration.Application.Services;
using AqtCoreFW.Collaboration.Domain.Repositories;
using MediatR;
using AqtCoreFW.Collaboration.Infrastructure.Clients;
using AqtCoreFW.Collaboration.Infrastructure.Persistence;
using AqtCoreFW.Collaboration.Infrastructure.Repositories;
using AqtCoreFW.Collaboration.Infrastructure.Services;
using AqtCoreFW.Infrastructure.Configuration;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Collaboration.Infrastructure;

public static class CollaborationInfrastructureRegistration
{
    public static IServiceCollection AddCollaborationInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // MediatR handlers in Infrastructure assembly (SignalR event handlers)
        services.AddMediatR(cfg =>
            cfg.RegisterServicesFromAssembly(typeof(CollaborationInfrastructureRegistration).Assembly));

        // DbContext — "collab" schema, separate migrations history table
        services.AddDbContext<CollaborationDbContext>((sp, options) =>
        {
            options.UseSqlServer(
                NonAuditConnectionString.Get(configuration),
                sql => sql.MigrationsHistoryTable("__EFMigrationsHistory", "collab"));

            options.AddInterceptors(
                sp.GetRequiredService<SlowQueryInterceptor>(),
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<OutboxInterceptor>(),
                sp.GetRequiredService<AuditableEntityInterceptor>(),
                sp.GetRequiredService<TenantSessionContextInterceptor>());
        });

        // Repositories
        services.AddScoped<IConversationRepository, ConversationRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IMessageReadStateRepository, MessageReadStateRepository>();

        // Presence cache — IMemoryCache is registered once by the host (shared
        // InfrastructureRegistration); no module-level re-registration needed.
        services.AddScoped<IPresenceCacheService, PresenceCacheService>();

        // Cross-module client — in-process for monolith
        services.AddScoped<IChatModuleClient, InProcessChatModuleClient>();

        // SignalR is registered by NotificationsInfrastructure (host level) — do not duplicate here.
        // ChatHub is mapped in Program.cs via app.MapHub<ChatHub>("/hubs/chat").

        return services;
    }
}
