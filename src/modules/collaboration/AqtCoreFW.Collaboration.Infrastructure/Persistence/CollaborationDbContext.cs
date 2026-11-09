using AqtCoreFW.Collaboration.Domain.Entities;
using AqtCoreFW.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Domain;
using Microsoft.EntityFrameworkCore;
using DomainThread = AqtCoreFW.Collaboration.Domain.Entities.Thread;

namespace AqtCoreFW.Collaboration.Infrastructure.Persistence;

/// <summary>
/// Collaboration module DB context — owns the "collab" schema.
/// Inherits ModuleDbContext: soft-delete + tenant filters, outbox table, audit interceptors.
/// </summary>
public sealed class CollaborationDbContext(DbContextOptions<CollaborationDbContext> options, ITenantContext tenantContext)
    : ModuleDbContext(options, tenantContext)
{
    protected override string SchemaName => "collab";

    public DbSet<Conversation> Conversations { get; set; } = default!;
    public DbSet<ConversationMember> ConversationMembers { get; set; } = default!;
    public DbSet<Message> Messages { get; set; } = default!;
    public DbSet<DomainThread> Threads { get; set; } = default!;
    public DbSet<MessageReadState> MessageReadStates { get; set; } = default!;
}
