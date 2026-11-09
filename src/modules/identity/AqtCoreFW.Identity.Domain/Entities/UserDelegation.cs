using System.Text.Json;
using AqtCoreFW.Identity.Domain.Events;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.SharedKernel.Domain.Events;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Delegation lifecycle states.
/// </summary>
public enum DelegationStatus
{
    PendingApproval = 1,
    Active = 2,
    Expired = 3,
    Revoked = 4
}

/// <summary>
/// Full role delegation: User A delegates all (or a scoped subset of) roles to User B for a time window.
/// DelegationClaimsTransformer injects delegator's roles into delegate's JWT claims.
/// T2: long PK (Id, DB-assigned) + Guid AK (PublicId, set at create via IGuidGenerator v7).
///
/// Enhancement (Phase E):
///   - DelegatedRoleIds: optional JSON array of Guid — restrict which roles are delegated.
///     Null = inject all delegator roles (backward-compat).
///   - ScopeJson: optional JSON scope override.
///   - Status: lifecycle enum (PendingApproval → Active → Expired/Revoked).
///   - RequiresApproval: if true, delegation stays PendingApproval until Admin approves.
///   - ApprovedBy / ApprovedAtUtc: approval audit trail.
/// </summary>
public sealed class UserDelegation : AuditableEntity, IPublicIdentified
{
    private UserDelegation() { }

    /// <summary>Public-facing alternate key (Guid v7 monotonic). Used in API URLs and DTOs.</summary>
    public Guid PublicId { get; private set; }

    public Guid DelegatorId { get; private set; }
    public Guid DelegateId { get; private set; }
    public DateTime ValidFrom { get; private set; }
    public DateTime ValidTo { get; private set; }
    public string? Reason { get; private set; }

    // --- Legacy revoke flag (kept for backward compat; Status = Revoked is the canonical signal) ---
    public bool IsRevoked { get; private set; }
    public Guid? RevokedBy { get; private set; }

    // --- Phase E additions ---

    /// <summary>
    /// JSON array of role Guid strings to restrict delegation scope.
    /// Null = delegate all delegator roles (backward-compat behavior).
    /// </summary>
    public string? DelegatedRoleIds { get; private set; }

    /// <summary>JSON-serialized scope override object (reserved for future fine-grained scope).</summary>
    public string? ScopeJson { get; private set; }

    /// <summary>Delegation lifecycle status.</summary>
    public DelegationStatus Status { get; private set; } = DelegationStatus.Active;

    /// <summary>When true, delegation waits for Admin approval before becoming Active.</summary>
    public bool RequiresApproval { get; private set; }

    /// <summary>ID of Admin who approved the delegation.</summary>
    public Guid? ApprovedBy { get; private set; }

    /// <summary>UTC timestamp when the delegation was approved.</summary>
    public DateTime? ApprovedAtUtc { get; private set; }

    // --- Domain events ---
    private readonly List<IDomainEvent> _domainEvents = [];
    public IReadOnlyList<IDomainEvent> DomainEvents => _domainEvents.AsReadOnly();
    public void ClearDomainEvents() => _domainEvents.Clear();

    // --- Navigation ---
    public ApplicationUser Delegator { get; private set; } = null!;
    public ApplicationUser Delegate { get; private set; } = null!;

    /// <summary>
    /// Factory — creates a new delegation. Raises DelegationGrantedEvent.
    /// </summary>
    public static UserDelegation Create(
        IGuidGenerator gen,
        Guid delegatorId,
        Guid delegateId,
        DateTime validFrom,
        DateTime validTo,
        string? reason,
        string? delegatedRoleIds,
        string? scopeJson,
        bool requiresApproval,
        Guid actorId)
    {
        var delegation = new UserDelegation
        {
            PublicId = gen.NewIdMonotonic(),
            DelegatorId = delegatorId,
            DelegateId = delegateId,
            ValidFrom = validFrom,
            ValidTo = validTo,
            Reason = reason?.Trim(),
            DelegatedRoleIds = delegatedRoleIds,
            ScopeJson = scopeJson,
            RequiresApproval = requiresApproval,
            Status = requiresApproval ? DelegationStatus.PendingApproval : DelegationStatus.Active,
            IsRevoked = false,
        };
        delegation.SetAuditCreate(actorId);
        delegation._domainEvents.Add(new DelegationGrantedEvent(
            delegation.PublicId, delegatorId, delegateId, actorId, gen.NewIdRandom()));
        return delegation;
    }

    /// <summary>
    /// Revokes the delegation. Raises DelegationRevokedEvent.
    /// </summary>
    public void Revoke(Guid actorId, IGuidGenerator gen)
    {
        if (Status == DelegationStatus.Revoked) return;

        IsRevoked = true;
        RevokedBy = actorId;
        Status = DelegationStatus.Revoked;
        SetAuditUpdate(actorId);
        _domainEvents.Add(new DelegationRevokedEvent(
            PublicId, DelegatorId, DelegateId, actorId, gen.NewIdRandom()));
    }

    /// <summary>Approves a pending delegation.</summary>
    public void Approve(Guid approvedBy)
    {
        if (Status != DelegationStatus.PendingApproval) return;

        Status = DelegationStatus.Active;
        ApprovedBy = approvedBy;
        ApprovedAtUtc = DateTime.UtcNow;
        SetAuditUpdate(approvedBy);
    }

    /// <summary>Marks the delegation as expired (called by DelegationExpiryJob).</summary>
    public void Expire()
    {
        if (Status == DelegationStatus.Expired) return;
        Status = DelegationStatus.Expired;
    }

    /// <summary>
    /// Returns the list of role IDs scoped to this delegation,
    /// or null if all delegator roles should be injected (backward compat).
    /// </summary>
    public IReadOnlyList<Guid>? GetDelegatedRoleIds()
    {
        if (string.IsNullOrWhiteSpace(DelegatedRoleIds))
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<Guid>>(DelegatedRoleIds);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns true when the delegation is currently effective:
    /// Status == Active, not revoked, and within the valid time window.
    /// </summary>
    public bool IsEffective(DateTime now) =>
        Status == DelegationStatus.Active
        && !IsRevoked
        && ValidFrom <= now
        && ValidTo >= now;
}
