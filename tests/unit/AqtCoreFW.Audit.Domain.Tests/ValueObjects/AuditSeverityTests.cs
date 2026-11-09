using AqtCoreFW.Audit.Domain.ValueObjects;
using FluentAssertions;

namespace AqtCoreFW.Audit.Domain.Tests.ValueObjects;

/// <summary>
/// Unit tests for AuditSeverity and IncidentStatus enums (QĐ742 classification).
/// TC-AUD-D013: AuditSeverity ordering (Low=1 &lt; Medium=2 &lt; High=3 &lt; Critical=4).
/// </summary>
public sealed class AuditSeverityTests
{
    // --- TC-AUD-D013: Severity ordering ---

    [Fact]
    public void AuditSeverity_Low_HasValue1()
    {
        ((int)AuditSeverity.Low).Should().Be(1);
    }

    [Fact]
    public void AuditSeverity_Medium_HasValue2()
    {
        ((int)AuditSeverity.Medium).Should().Be(2);
    }

    [Fact]
    public void AuditSeverity_High_HasValue3()
    {
        ((int)AuditSeverity.High).Should().Be(3);
    }

    [Fact]
    public void AuditSeverity_Critical_HasValue4()
    {
        ((int)AuditSeverity.Critical).Should().Be(4);
    }

    [Fact]
    public void AuditSeverity_LowIsLessThanMedium()
    {
        ((int)AuditSeverity.Low).Should().BeLessThan((int)AuditSeverity.Medium);
    }

    [Fact]
    public void AuditSeverity_MediumIsLessThanHigh()
    {
        ((int)AuditSeverity.Medium).Should().BeLessThan((int)AuditSeverity.High);
    }

    [Fact]
    public void AuditSeverity_HighIsLessThanCritical()
    {
        ((int)AuditSeverity.High).Should().BeLessThan((int)AuditSeverity.Critical);
    }

    [Fact]
    public void AuditSeverity_AllValues_SortableByIntValue()
    {
        var severities = new[] { AuditSeverity.Critical, AuditSeverity.Low, AuditSeverity.High, AuditSeverity.Medium };

        var sorted = severities.OrderBy(s => (int)s).ToArray();

        sorted.Should().ContainInOrder(
            AuditSeverity.Low,
            AuditSeverity.Medium,
            AuditSeverity.High,
            AuditSeverity.Critical);
    }

    [Theory]
    [InlineData(AuditSeverity.Low,      1)]
    [InlineData(AuditSeverity.Medium,   2)]
    [InlineData(AuditSeverity.High,     3)]
    [InlineData(AuditSeverity.Critical, 4)]
    public void AuditSeverity_EachLevel_HasExpectedIntValue(AuditSeverity severity, int expected)
    {
        ((int)severity).Should().Be(expected);
    }

    [Fact]
    public void AuditSeverity_CriticalIsGreaterThanAllOthers()
    {
        var allExceptCritical = new[] { AuditSeverity.Low, AuditSeverity.Medium, AuditSeverity.High };

        foreach (var s in allExceptCritical)
            ((int)AuditSeverity.Critical).Should().BeGreaterThan((int)s);
    }

    // --- IncidentStatus enum ---

    [Fact]
    public void IncidentStatus_DefinesOpenStatus()
    {
        Enum.IsDefined(typeof(IncidentStatus), IncidentStatus.Open).Should().BeTrue();
    }

    [Fact]
    public void IncidentStatus_DefinesInvestigatingStatus()
    {
        Enum.IsDefined(typeof(IncidentStatus), IncidentStatus.Investigating).Should().BeTrue();
    }

    [Fact]
    public void IncidentStatus_DefinesResolvedStatus()
    {
        Enum.IsDefined(typeof(IncidentStatus), IncidentStatus.Resolved).Should().BeTrue();
    }

    [Fact]
    public void IncidentStatus_DefinesClosedStatus()
    {
        Enum.IsDefined(typeof(IncidentStatus), IncidentStatus.Closed).Should().BeTrue();
    }

    // --- RtbfStatus enum ---

    [Fact]
    public void RtbfStatus_DefinesAllExpectedValues()
    {
        var expected = new[]
        {
            RtbfStatus.Pending,
            RtbfStatus.Processing,
            RtbfStatus.Completed,
            RtbfStatus.Rejected,
            RtbfStatus.PartiallyCompleted
        };

        foreach (var status in expected)
            Enum.IsDefined(typeof(RtbfStatus), status).Should().BeTrue();
    }
}
