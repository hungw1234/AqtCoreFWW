using Microsoft.EntityFrameworkCore;

namespace AqtCoreFW.Infrastructure.Messaging;

/// <summary>
/// Dedicated DbContext for cross-cutting messaging concerns:
/// dead letters (messaging.dead_letters) and the consumer idempotency ledger
/// (messaging.processed_messages).
/// Separate from ModuleDbContext — no tenant filter needed here.
/// Registered as Scoped by AddMessageBus().
/// </summary>
public sealed class MessagingDbContext(DbContextOptions<MessagingDbContext> options)
    : DbContext(options)
{
    public DbSet<MessageDeadLetter> DeadLetters { get; set; } = default!;

    public DbSet<ProcessedMessage> ProcessedMessages { get; set; } = default!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("messaging");

        modelBuilder.Entity<MessageDeadLetter>(e =>
        {
            e.ToTable("dead_letters");
            e.HasKey(x => x.Id);
            e.Property(x => x.MessageType).HasMaxLength(500).IsRequired();
            e.Property(x => x.Payload).HasColumnType("nvarchar(max)").IsRequired();
            e.Property(x => x.FailureReason).HasMaxLength(2000).IsRequired();
            e.Property(x => x.OriginalQueue).HasMaxLength(200).IsRequired();
            e.Property(x => x.QuarantineReason).HasMaxLength(1000);
            e.Property(x => x.Status).HasConversion<int>();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.ReceivedAt);
        });

        modelBuilder.Entity<ProcessedMessage>(e =>
        {
            e.ToTable("processed_messages");
            e.HasKey(x => x.Id);
            e.Property(x => x.ConsumerName).HasMaxLength(200).IsRequired();
            e.Property(x => x.DedupKey).HasMaxLength(200).IsRequired();
            // The claim primitive: a duplicate (consumer, key) INSERT hits this unique index → the
            // 2601/2627 that ProcessedMessageStore reads as "already processed".
            e.HasIndex(x => new { x.ConsumerName, x.DedupKey }).IsUnique();
            // Retention cutoff column for the bounded-growth cleanup sweep.
            e.HasIndex(x => x.ProcessedAtUtc);
        });
    }
}
