using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Audit.Domain.Entities;

/// <summary>
/// Records every authentication attempt — for suspicious activity detection.
///
/// T3 plain-class contract (AMEND-A3):
///   - long InternalId: bigint identity PK (DB-assigned, never exposed externally)
///   - Guid MessageId: T3 AK (v7 monotonic), unique index
///   - DateTime CreatedAt: UTC, set at creation — NO UpdatedAt, NO IsDeleted, NO MarkDeleted
///   - Implements IMessageIdentified
///   - Manual IEntityTypeConfiguration (cannot inherit HotTableConfigurationBase)
/// </summary>
public sealed class LoginAttempt : IMessageIdentified
{
    private LoginAttempt() { }

    /// <summary>bigint identity PK — DB-assigned, never exposed externally.</summary>
    public long InternalId { get; private set; }

    /// <summary>T3 alternate key — v7 monotonic Guid for outbox dedup and event correlation.</summary>
    public Guid MessageId { get; private set; }

    /// <summary>UTC creation timestamp.</summary>
    public DateTime CreatedAt { get; private set; }

    [DataClassification(DataClassificationLevel.Internal)]
    /// <summary>UserId — stays Guid (FK exception list: references AspNetUsers.Id).</summary>
    public Guid? UserId { get; private set; }

    [DataClassification(DataClassificationLevel.Confidential)]
    public string Email { get; private set; } = string.Empty;

    [DataClassification(DataClassificationLevel.Confidential)]
    public string IpAddress { get; private set; } = string.Empty;

    [DataClassification(DataClassificationLevel.Confidential)]
    public string? UserAgent { get; private set; }

    public bool Success { get; private set; }
    public string? FailureReason { get; private set; }

    /// <summary>Domain event timestamp — distinct from CreatedAt (T3 PK timestamp).</summary>
    public DateTimeOffset AttemptedAt { get; private set; }

    public static LoginAttempt Record(
        IGuidGenerator gen,
        Guid? userId, string email, string ipAddress, string? userAgent,
        bool success, string? failureReason = null) =>
        new()
        {
            MessageId = gen.NewIdMonotonic(),
            CreatedAt = DateTime.UtcNow,
            AttemptedAt = DateTimeOffset.UtcNow,
            UserId = userId,
            Email = email,
            IpAddress = ipAddress,
            UserAgent = userAgent,
            Success = success,
            FailureReason = failureReason
        };
}
