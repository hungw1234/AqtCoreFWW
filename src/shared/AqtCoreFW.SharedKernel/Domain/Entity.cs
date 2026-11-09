namespace AqtCoreFW.SharedKernel.Domain;

/// <summary>
/// Base entity with bigint identity PK, soft delete, and audit timestamps.
/// Phase-2 Foundation Refactor (2026-05-03): Id is long (DB-assigned IDENTITY), CreatedAt is DateTime (datetime2(3) UTC convention).
/// Implements ISoftDeletable — EF SoftDeleteInterceptor and global query filter rely on this.
/// T2 entities additionally implement IPublicIdentified (Guid PublicId AK). T3 entities implement IMessageIdentified (Guid MessageId).
/// </summary>
public abstract class Entity : ISoftDeletable
{
    /// <summary>bigint identity, DB-assigned post-INSERT. 0 until SaveChanges.</summary>
    public long Id { get; protected set; }

    /// <summary>UTC creation timestamp (datetime2(3)). Convention: all timestamps UTC; offset applied at serialization boundary only.</summary>
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; private set; }

    public bool IsDeleted { get; private set; }

    /// <summary>Marks entity as soft-deleted. Called by SoftDeleteInterceptor on EF Delete.</summary>
    public void MarkDeleted() => IsDeleted = true;

    protected void MarkUpdated() => UpdatedAt = DateTime.UtcNow;

    /// <summary>Alias for MarkDeleted — use in domain logic for intent clarity.</summary>
    public void Delete() => MarkDeleted();
}
