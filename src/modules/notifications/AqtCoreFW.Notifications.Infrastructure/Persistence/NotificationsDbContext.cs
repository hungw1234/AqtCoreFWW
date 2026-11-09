using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.Notifications.Domain.Entities;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Notifications.Infrastructure.Persistence;

/// <summary>
/// Notifications module DB context — owns the "notifications" schema.
/// Inherits ModuleDbContext: soft-delete + tenant filters, outbox table, audit interceptors.
/// </summary>
public sealed class NotificationsDbContext(DbContextOptions<NotificationsDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "notifications";

    public DbSet<Notification> Notifications { get; set; } = default!;
    public DbSet<NotificationTemplate> NotificationTemplates { get; set; } = default!;
    public DbSet<NotificationPreference> NotificationPreferences { get; set; } = default!;
    public DbSet<NotificationLog> NotificationLogs { get; set; } = default!;
}
