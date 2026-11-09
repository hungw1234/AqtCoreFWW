using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Audit.Domain.Tests.Entities;

/// <summary>
/// Unit tests for SecurityIncident entity (T3 plain-class: long InternalId PK + Guid MessageId AK).
/// QĐ742 mandated tracking.
/// TC-AUD-D010: SecurityIncident entity lifecycle (Open → Investigating → Resolved).
/// </summary>
public sealed class SecurityIncidentTests
{
    private static readonly Guid TenantId    = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ReportedBy  = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateTimeOffset OccurredAt = new(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);

    private static IGuidGenerator NewGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return gen;
    }

    private static SecurityIncident BuildIncident(
        AuditSeverity severity = AuditSeverity.High,
        string title = "Unauthorized Access",
        string description = "Detected unauthorized access attempt") =>
        SecurityIncident.Report(NewGen(), TenantId, title, severity, description, ReportedBy, OccurredAt);

    // --- Report factory ---

    [Fact]
    public void Report_SetsAllRequiredFields()
    {
        var incident = BuildIncident();

        incident.TenantId.Should().Be(TenantId);
        incident.Title.Should().Be("Unauthorized Access");
        incident.Severity.Should().Be(AuditSeverity.High);
        incident.Description.Should().Be("Detected unauthorized access attempt");
        incident.ReportedBy.Should().Be(ReportedBy);
        incident.OccurredAt.Should().Be(OccurredAt);
    }

    [Fact]
    public void Report_StatusIsOpenByDefault()
    {
        var incident = BuildIncident();

        incident.Status.Should().Be(IncidentStatus.Open);
    }

    [Fact]
    public void Report_ResolvedAt_IsNullInitially()
    {
        var incident = BuildIncident();

        incident.ResolvedAt.Should().BeNull();
    }

    [Fact]
    public void Report_Mitigations_IsNullInitially()
    {
        var incident = BuildIncident();

        incident.Mitigations.Should().BeNull();
    }

    [Fact]
    public void Report_GeneratesNonEmptyMessageId()
    {
        var incident = BuildIncident();

        // T3: identity key is MessageId (Guid v7), not InternalId (DB-assigned long)
        incident.MessageId.Should().NotBeEmpty();
    }

    [Fact]
    public void Report_CreatedAt_IsApproximatelyUtcNow()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var incident = BuildIncident();

        // CreatedAt is DateTime (UTC) on T3 entities
        incident.CreatedAt.Should().BeAfter(before);
        incident.CreatedAt.Should().BeOnOrBefore(DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public void Report_WithNullTenantId_TenantIdIsNull()
    {
        var incident = SecurityIncident.Report(
            NewGen(),
            null, "System Incident", AuditSeverity.Critical,
            "Cross-tenant breach", ReportedBy, OccurredAt);

        incident.TenantId.Should().BeNull();
    }

    // --- TC-AUD-D010: Lifecycle Open → Investigating → Resolved ---

    [Fact]
    public void UpdateStatus_OpenToInvestigating_TransitionsStatus()
    {
        var incident = BuildIncident();

        incident.UpdateStatus(IncidentStatus.Investigating, "SOC team assigned");

        incident.Status.Should().Be(IncidentStatus.Investigating);
    }

    [Fact]
    public void UpdateStatus_WithMitigationNote_AppendsMitigationNote()
    {
        var incident = BuildIncident();

        incident.UpdateStatus(IncidentStatus.Investigating, "Blocked offending IP 1.2.3.4");

        incident.Mitigations.Should().NotBeNullOrEmpty();
        incident.Mitigations.Should().Contain("Blocked offending IP 1.2.3.4");
    }

    [Fact]
    public void UpdateStatus_MultipleMitigationNotes_AppendsBoth()
    {
        var incident = BuildIncident();

        incident.UpdateStatus(IncidentStatus.Investigating, "First mitigation");
        incident.UpdateStatus(IncidentStatus.Investigating, "Second mitigation");

        incident.Mitigations.Should().Contain("First mitigation");
        incident.Mitigations.Should().Contain("Second mitigation");
    }

    [Fact]
    public void UpdateStatus_WithNullMitigationNote_DoesNotAppend()
    {
        var incident = BuildIncident();

        incident.UpdateStatus(IncidentStatus.Investigating, null);

        incident.Mitigations.Should().BeNull();
    }

    [Fact]
    public void UpdateStatus_WithEmptyMitigationNote_DoesNotAppend()
    {
        var incident = BuildIncident();

        incident.UpdateStatus(IncidentStatus.Investigating, string.Empty);

        incident.Mitigations.Should().BeNull();
    }

    [Fact]
    public void UpdateStatus_ToResolved_SetsResolvedAt()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var incident = BuildIncident();
        incident.UpdateStatus(IncidentStatus.Investigating, "investigating");

        incident.UpdateStatus(IncidentStatus.Resolved, "Root cause found and patched");

        incident.ResolvedAt.Should().NotBeNull();
        incident.ResolvedAt.Should().BeAfter(before);
        incident.Status.Should().Be(IncidentStatus.Resolved);
    }

    [Fact]
    public void UpdateStatus_ToClosed_SetsResolvedAt()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var incident = BuildIncident();

        incident.UpdateStatus(IncidentStatus.Closed, "Formally closed after review");

        incident.ResolvedAt.Should().NotBeNull();
        incident.ResolvedAt.Should().BeAfter(before);
        incident.Status.Should().Be(IncidentStatus.Closed);
    }

    [Fact]
    public void UpdateStatus_ToOpen_DoesNotSetResolvedAt()
    {
        var incident = BuildIncident();

        incident.UpdateStatus(IncidentStatus.Open, null);

        incident.ResolvedAt.Should().BeNull();
    }

    [Fact]
    public void UpdateStatus_ToInvestigating_DoesNotSetResolvedAt()
    {
        var incident = BuildIncident();

        incident.UpdateStatus(IncidentStatus.Investigating, "Assigning team");

        incident.ResolvedAt.Should().BeNull();
    }

    // --- Severity levels ---

    [Fact]
    public void Report_CriticalSeverity_Stored()
    {
        var incident = BuildIncident(severity: AuditSeverity.Critical);

        incident.Severity.Should().Be(AuditSeverity.Critical);
    }

    [Fact]
    public void Report_LowSeverity_Stored()
    {
        var incident = BuildIncident(severity: AuditSeverity.Low);

        incident.Severity.Should().Be(AuditSeverity.Low);
    }
}
