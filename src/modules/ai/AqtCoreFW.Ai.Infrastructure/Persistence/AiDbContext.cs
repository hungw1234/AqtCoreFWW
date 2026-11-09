using AqtCoreFW.Ai.Domain.Entities;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Ai.Infrastructure.Persistence;

/// <summary>
/// AI module DB context — owns the "ai" schema.
/// Inherits ModuleDbContext: soft-delete + tenant filters, outbox table, audit interceptors.
/// </summary>
public sealed class AiDbContext(DbContextOptions<AiDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "ai";

    public DbSet<AiSession> AiSessions { get; set; } = default!;
    public DbSet<AiChatMessage> AiChatMessages { get; set; } = default!;
    public DbSet<VectorDocument> VectorDocuments { get; set; } = default!;
    public DbSet<AiModelProfile> AiModelProfiles { get; set; } = default!;
    public DbSet<AiPromptTemplate> AiPromptTemplates { get; set; } = default!;
}
