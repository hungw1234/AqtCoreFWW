using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Status of a two-admin MFA emergency disable request (QĐ742 1.6).
/// </summary>
public enum EmergencyDisableStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Expired = 3
}

/// <summary>
/// 2-admin approval flow for emergency MFA disable (QĐ742 1.6).
/// Requester submits → second admin approves → target user's TotpEnrolledAt + TotpSecret cleared.
/// Approver MUST be different from Requester (enforced in ApproveEmergencyDisableCommandHandler).
/// Requests expire after 24 h if not actioned.
/// NOTE: No INotificationService available in phase-03 — approval notification is a Critical
///       audit log entry only. Email notification deferred to phase-07b (IEmailSender).
/// </summary>
public class MfaEmergencyDisableRequest : AuditableEntity
{
    /// <summary>User whose MFA will be disabled upon approval.</summary>
    public Guid TargetUserId { get; private set; }

    /// <summary>Admin who submitted the request.</summary>
    public Guid RequestedBy { get; private set; }

    /// <summary>Business justification — stored for audit trail.</summary>
    public string Reason { get; private set; } = string.Empty;

    public EmergencyDisableStatus Status { get; private set; } = EmergencyDisableStatus.Pending;

    /// <summary>Admin who approved or rejected (null while Pending).</summary>
    public Guid? ActionedBy { get; private set; }

    /// <summary>UTC timestamp of approval or rejection.</summary>
    public DateTime? ActionedAt { get; private set; }

    /// <summary>UTC expiry — requests auto-expire 24 h after creation if not actioned.</summary>
    public DateTime ExpiresAt { get; private set; }

    private MfaEmergencyDisableRequest() { }

    /// <summary>Factory — use in RequestEmergencyDisableCommandHandler.</summary>
    public static MfaEmergencyDisableRequest Create(
        Guid targetUserId,
        Guid requestedBy,
        string reason)
    {
        if (targetUserId == Guid.Empty)
            throw new ArgumentException("TargetUserId must not be empty.", nameof(targetUserId));
        if (requestedBy == Guid.Empty)
            throw new ArgumentException("RequestedBy must not be empty.", nameof(requestedBy));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason must not be empty.", nameof(reason));

        var req = new MfaEmergencyDisableRequest
        {
            TargetUserId = targetUserId,
            RequestedBy = requestedBy,
            Reason = reason,
            Status = EmergencyDisableStatus.Pending,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
        };
        req.SetAuditCreate(requestedBy);
        return req;
    }

    /// <summary>
    /// Approves the request and clears MFA on the target user.
    /// Caller must verify approver != requester before calling.
    /// </summary>
    public void Approve(Guid approvedBy)
    {
        EnsurePending();
        Status = EmergencyDisableStatus.Approved;
        ActionedBy = approvedBy;
        ActionedAt = DateTime.UtcNow;
        SetAuditUpdate(approvedBy);
    }

    /// <summary>Rejects the request without disabling MFA.</summary>
    public void Reject(Guid rejectedBy)
    {
        EnsurePending();
        Status = EmergencyDisableStatus.Rejected;
        ActionedBy = rejectedBy;
        ActionedAt = DateTime.UtcNow;
        SetAuditUpdate(rejectedBy);
    }

    private void EnsurePending()
    {
        if (Status != EmergencyDisableStatus.Pending)
            throw new InvalidOperationException(
                $"Request {Id} is already in status {Status} — cannot action.");
        if (DateTime.UtcNow > ExpiresAt)
            throw new InvalidOperationException(
                $"Request {Id} has expired and cannot be actioned.");
    }
}
