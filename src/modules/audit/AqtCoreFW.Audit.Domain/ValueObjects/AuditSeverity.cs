namespace AqtCoreFW.Audit.Domain.ValueObjects;

/// <summary>Security incident severity — QĐ742 classification.</summary>
public enum AuditSeverity { Low = 1, Medium = 2, High = 3, Critical = 4 }

/// <summary>Security incident status.</summary>
public enum IncidentStatus { Open, Investigating, Resolved, Closed }

/// <summary>RTBF request status.</summary>
public enum RtbfStatus { Pending, Processing, Completed, Rejected, PartiallyCompleted }
