using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Infrastructure.Alerting;

/// <summary>
/// Defines a metric-based alert rule evaluated every minute by AlertEvaluationJob.
/// Condition operators: GT (greater than), LT (less than), EQ (equal).
/// NotifyChannel: Email | Webhook | Log.
/// ConsecutiveBreaches resets to 0 when condition is no longer met.
/// </summary>
// Phase-2 Foundation Refactor (2026-05-03): rebased to non-generic AuditableEntity (long Id, DB-assigned IDENTITY).
// AlertRule is T1 per Phase-1 entity-tier-mapping (internal infra; no API exposure → no PublicId).
// Existing migration creates Id as uniqueidentifier; Phase-6 will drop+regen all migrations to bigint.
public sealed class AlertRule : AuditableEntity
{
    public string Name { get; private set; } = default!;
    public string MetricName { get; private set; } = default!;     // e.g. "http_request_duration_seconds"
    public string Condition { get; private set; } = default!;      // GT | LT | EQ
    public double Threshold { get; private set; }
    public int WindowMinutes { get; private set; } = 5;
    public string NotifyChannel { get; private set; } = default!;  // Email | Webhook | Log
    public string? NotifyTarget { get; private set; }              // email address or webhook URL
    public bool Enabled { get; private set; } = true;
    public DateTime? LastTriggeredAt { get; private set; }
    public int ConsecutiveBreaches { get; private set; }

    // EF Core constructor
    private AlertRule() { }

    /// <summary>Factory — validates required fields and sets audit create.</summary>
    public static AlertRule Create(
        string name,
        string metricName,
        string condition,
        double threshold,
        int windowMinutes,
        string notifyChannel,
        string? notifyTarget,
        Guid createdBy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(metricName);
        ArgumentException.ThrowIfNullOrWhiteSpace(notifyChannel);

        var rule = new AlertRule
        {
            // Id assigned by DB on save (bigint IDENTITY)
            Name            = name.Trim(),
            MetricName      = metricName.Trim(),
            Condition       = condition.ToUpperInvariant(),
            Threshold       = threshold,
            WindowMinutes   = Math.Max(1, windowMinutes),
            NotifyChannel   = notifyChannel,
            NotifyTarget    = notifyTarget?.Trim(),
            Enabled         = true,
            ConsecutiveBreaches = 0
        };
        rule.SetAuditCreate(createdBy);
        return rule;
    }

    public void Update(
        string name,
        string metricName,
        string condition,
        double threshold,
        int windowMinutes,
        string notifyChannel,
        string? notifyTarget,
        Guid modifiedBy)
    {
        Name          = name.Trim();
        MetricName    = metricName.Trim();
        Condition     = condition.ToUpperInvariant();
        Threshold     = threshold;
        WindowMinutes = Math.Max(1, windowMinutes);
        NotifyChannel = notifyChannel;
        NotifyTarget  = notifyTarget?.Trim();
        SetAuditUpdate(modifiedBy);
    }

    public void Enable(Guid modifiedBy) { Enabled = true; SetAuditUpdate(modifiedBy); }

    public void Disable(Guid modifiedBy) { Enabled = false; SetAuditUpdate(modifiedBy); }

    /// <summary>Called by AlertEvaluationJob when the condition is met.</summary>
    public void RecordBreach()
    {
        ConsecutiveBreaches++;
        LastTriggeredAt = DateTime.UtcNow;
        MarkUpdated();
    }

    /// <summary>Called by AlertEvaluationJob when the condition is no longer met.</summary>
    public void ClearBreach() { ConsecutiveBreaches = 0; MarkUpdated(); }

    /// <summary>Evaluates whether the supplied metric value breaches this rule's condition.</summary>
    public bool IsBreached(double metricValue) => Condition switch
    {
        "GT" => metricValue > Threshold,
        "LT" => metricValue < Threshold,
        "EQ" => Math.Abs(metricValue - Threshold) < 1e-9,
        _    => false
    };
}
