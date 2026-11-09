namespace AqtCoreFW.Audit.Domain.Enums;

/// <summary>Reviewer decision on an AI-generated output — M15 AI Governance.</summary>
public enum ReviewDecision
{
    Pending = 0,
    Approved = 1,
    Flagged = 2,
    Blocked = 3
}
