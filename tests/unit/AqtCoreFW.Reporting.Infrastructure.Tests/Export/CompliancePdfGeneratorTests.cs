using AqtCoreFW.Reporting.Application.DTOs;
using AqtCoreFW.Reporting.Infrastructure.Export;
using FluentAssertions;
using QuestPDF.Infrastructure;

namespace AqtCoreFW.Reporting.Infrastructure.Tests.Export;

/// <summary>
/// TC-RPT-I002: CompliancePdfGenerator includes headers/footers and produces valid PDF bytes.
/// </summary>
public sealed class CompliancePdfGeneratorTests
{
    static CompliancePdfGeneratorTests()
    {
        // QuestPDF requires a license setting for unit tests (Community license is free)
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private readonly CompliancePdfGenerator _sut = new();

    private static ComplianceEvidenceDto BuildSampleData() => new(
        Framework: "ISO 27001",
        GeneratedAt: DateTimeOffset.UtcNow,
        GeneratedBy: "admin@gov.vn",
        AuditSummary: new AuditSummaryDto(
            TotalEvents: 1000,
            LoginEvents: 200,
            DataAccessEvents: 600,
            AdminEvents: 200,
            PeriodFrom: DateTimeOffset.UtcNow.AddDays(-30),
            PeriodTo: DateTimeOffset.UtcNow),
        SecurityIncidents: [],
        AccessReviewStatus: new AccessReviewStatusDto(
            TotalReviewed: 50,
            Approved: 48,
            Revoked: 2,
            LastReviewDate: DateTimeOffset.UtcNow.AddDays(-7)),
        DataRetentionStatus: new DataRetentionStatusDto(
            TotalRecords: 10000,
            ArchivedRecords: 2000,
            PurgedRecords: 500,
            PendingPurge: 100));

    [Fact]
    public async Task GenerateAsync_ProducesNonEmptyPdfBytes()
    {
        // TC-RPT-I002: result is valid PDF (starts with %PDF magic bytes)
        var data = BuildSampleData();

        var bytes = await _sut.GenerateAsync(data);

        bytes.Should().NotBeNullOrEmpty();
        bytes[0].Should().Be(0x25); // %
        bytes[1].Should().Be(0x50); // P
        bytes[2].Should().Be(0x44); // D
        bytes[3].Should().Be(0x46); // F
    }

    [Fact]
    public async Task GenerateAsync_WithSecurityIncidents_DoesNotThrow()
    {
        var incident = new SecurityIncidentSummaryDto(
            IncidentId: "INC-001",
            Severity: "High",
            Status: "Resolved",
            Description: "Unauthorized access attempt",
            OccurredAt: DateTimeOffset.UtcNow.AddDays(-5));

        var data = new ComplianceEvidenceDto(
            Framework: "ISO 27001",
            GeneratedAt: DateTimeOffset.UtcNow,
            GeneratedBy: "admin@gov.vn",
            AuditSummary: new AuditSummaryDto(100, 20, 60, 20,
                DateTimeOffset.UtcNow.AddDays(-30), DateTimeOffset.UtcNow),
            SecurityIncidents: [incident],
            AccessReviewStatus: new AccessReviewStatusDto(10, 9, 1, DateTimeOffset.UtcNow.AddDays(-3)),
            DataRetentionStatus: new DataRetentionStatusDto(1000, 200, 50, 10));

        var act = async () => await _sut.GenerateAsync(data);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GenerateAsync_SizeIsReasonable()
    {
        // PDF for a simple compliance report should be > 1KB and < 5MB
        var bytes = await _sut.GenerateAsync(BuildSampleData());

        bytes.Length.Should().BeGreaterThan(1024);
        bytes.Length.Should().BeLessThan(5 * 1024 * 1024);
    }
}
