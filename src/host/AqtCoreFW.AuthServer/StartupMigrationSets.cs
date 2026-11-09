using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Infrastructure.Messaging;

namespace AqtCoreFW.AuthServer;

/// <summary>
/// The ordered set of DbContext types the AuthServer host migrates at startup.
///
/// This is a STRICT SUBSET of <see cref="AqtCoreFW.Api.StartupMigrationSets"/> by design
/// (ADR-010): AuthServer is never the sole schema owner of any context, so its runtime DB
/// login can stay least-privileged (no db_ddladmin for Audit/SystemParams/etc.). The Helm
/// db-migrate Job (Api image) owns the full schema in K8s; this in-app block exists only for
/// dev / single-container topologies, or a standalone AuthServer release.
///
/// The architecture test <c>MigrateSetSubsetGuardTests</c> enforces the subset relationship —
/// adding a context here that Api does NOT migrate is a defect (AuthServer would become the
/// sole migrator for a schema whose login is intentionally least-privileged).
///
/// ORDER IS LOAD-BEARING: Program.cs migrates these in sequence.
/// MessagingDbContext is required because AuthServer publishes AuditLogAppendedIntegrationEvent
/// which can dead-letter on retry exhaustion → the dead_letters table must exist on this DB.
/// </summary>
public static class StartupMigrationSets
{
    public static readonly IReadOnlyList<Type> Contexts =
    [
        typeof(IdentityDbContext),
        typeof(MessagingDbContext),
    ];
}
