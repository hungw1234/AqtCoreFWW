using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Workflow.Domain.Entities;

/// <summary>
/// A time-bounded rule delegating workflow tasks from one user (delegator) to another (delegate).
/// Constraint: max 1 active rule per delegator per tenant (enforced at Application layer).
/// TenantId scopes all queries — no cross-tenant delegation allowed.
/// T1 entity: non-generic AuditableEntity (long Id DB-assigned).
///   All FKs stay Guid (DelegatorUserId, DelegateUserId, TenantId — FK exception list).
/// </summary>
public sealed class DelegationRule : AuditableEntity, ITenantScoped
{
    /// <summary>User who is delegating their tasks.</summary>
    public Guid DelegatorUserId { get; private set; }

    /// <summary>User who receives delegated tasks.</summary>
    public Guid DelegateUserId { get; private set; }

    /// <summary>Delegation window start (inclusive).</summary>
    public DateTimeOffset ValidFrom { get; private set; }

    /// <summary>Delegation window end (inclusive). Must be after ValidFrom.</summary>
    public DateTimeOffset ValidUntil { get; private set; }

    /// <summary>Soft toggle — allows disabling without deleting the rule.</summary>
    public bool IsActive { get; private set; }

    public Guid TenantId { get; private set; }

    private DelegationRule() { } // EF Core

    /// <summary>
    /// Creates and validates a new DelegationRule.
    /// Guards: non-empty Guids, delegator != delegate, ValidFrom &lt; ValidUntil.
    /// </summary>
    public static DelegationRule Create(
        Guid delegatorUserId,
        Guid delegateUserId,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        Guid tenantId,
        Guid createdBy)
    {
        if (delegatorUserId == Guid.Empty)
            throw new ArgumentException("DelegatorUserId must not be empty.", nameof(delegatorUserId));

        if (delegateUserId == Guid.Empty)
            throw new ArgumentException("DelegateUserId must not be empty.", nameof(delegateUserId));

        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId must not be empty.", nameof(tenantId));

        if (delegatorUserId == delegateUserId)
            throw new ArgumentException("Delegator and delegate must be different users.");

        if (validFrom >= validUntil)
            throw new ArgumentException("ValidFrom must be before ValidUntil.", nameof(validFrom));

        var rule = new DelegationRule
        {
            DelegatorUserId = delegatorUserId,
            DelegateUserId = delegateUserId,
            ValidFrom = validFrom,
            ValidUntil = validUntil,
            IsActive = true,
            TenantId = tenantId
        };
        rule.SetAuditCreate(createdBy);
        return rule;
    }

    /// <summary>Checks whether the rule is currently active within the given point in time.</summary>
    public bool IsEffectiveAt(DateTimeOffset at) =>
        IsActive && ValidFrom <= at && at <= ValidUntil;

    /// <summary>Deactivates the rule (soft toggle). Idempotent.</summary>
    public void Deactivate(Guid modifiedBy)
    {
        IsActive = false;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>Reactivates the rule. Idempotent.</summary>
    public void Activate(Guid modifiedBy)
    {
        IsActive = true;
        SetAuditUpdate(modifiedBy);
    }

    /// <summary>Updates the delegation window. ValidFrom must remain before ValidUntil.</summary>
    public void UpdateWindow(
        Guid delegateUserId,
        DateTimeOffset validFrom,
        DateTimeOffset validUntil,
        Guid modifiedBy)
    {
        if (delegateUserId == Guid.Empty)
            throw new ArgumentException("DelegateUserId must not be empty.", nameof(delegateUserId));

        if (delegateUserId == DelegatorUserId)
            throw new ArgumentException("Delegator and delegate must be different users.");

        if (validFrom >= validUntil)
            throw new ArgumentException("ValidFrom must be before ValidUntil.", nameof(validFrom));

        DelegateUserId = delegateUserId;
        ValidFrom = validFrom;
        ValidUntil = validUntil;
        SetAuditUpdate(modifiedBy);
    }
}
