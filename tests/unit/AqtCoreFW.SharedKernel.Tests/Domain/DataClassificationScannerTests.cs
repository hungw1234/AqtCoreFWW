using System.Reflection;
using AqtCoreFW.Infrastructure.Compliance;
using AqtCoreFW.SharedKernel.Compliance;
using FluentAssertions;

namespace AqtCoreFW.SharedKernel.Tests.Domain;

/// <summary>
/// Unit tests for DataClassificationScanner.
/// Uses a local fixture assembly (this test assembly) containing known annotated types.
/// </summary>
public sealed class DataClassificationScannerTests
{
    private static readonly Assembly TestAssembly = typeof(DataClassificationScannerTests).Assembly;

    [Fact]
    public void ScanAssemblies_finds_restricted_properties()
    {
        var report = DataClassificationScanner.ScanAssemblies(TestAssembly);

        report.Entries.Should().Contain(e =>
            e.TypeFullName!.Contains(nameof(SamplePersonEntity)) &&
            e.PropertyName == nameof(SamplePersonEntity.NationalId) &&
            e.Level == DataClassificationLevel.Restricted);
    }

    [Fact]
    public void ScanAssemblies_finds_confidential_properties()
    {
        var report = DataClassificationScanner.ScanAssemblies(TestAssembly);

        report.Entries.Should().Contain(e =>
            e.TypeFullName!.Contains(nameof(SamplePersonEntity)) &&
            e.PropertyName == nameof(SamplePersonEntity.Email) &&
            e.Level == DataClassificationLevel.Confidential);
    }

    [Fact]
    public void ScanAssemblies_does_not_include_unclassified_properties()
    {
        var report = DataClassificationScanner.ScanAssemblies(TestAssembly);

        report.Entries.Should().NotContain(e =>
            e.TypeFullName!.Contains(nameof(SamplePersonEntity)) &&
            e.PropertyName == nameof(SamplePersonEntity.DisplayName));
    }

    [Fact]
    public void ScanAssemblies_report_counts_are_accurate()
    {
        var report = DataClassificationScanner.ScanAssemblies(TestAssembly);

        report.RestrictedCount.Should().Be(
            report.Entries.Count(e => e.Level == DataClassificationLevel.Restricted));

        report.ConfidentialCount.Should().Be(
            report.Entries.Count(e => e.Level == DataClassificationLevel.Confidential));

        report.TotalCount.Should().Be(report.Entries.Count);
    }

    [Fact]
    public void ScanAssemblies_sorts_restricted_before_confidential()
    {
        var report = DataClassificationScanner.ScanAssemblies(TestAssembly);

        // All restricted entries must appear before confidential entries
        var levels = report.Entries.Select(e => e.Level).ToList();
        var firstConfidential = levels.IndexOf(DataClassificationLevel.Confidential);
        var lastRestricted = levels.LastIndexOf(DataClassificationLevel.Restricted);

        if (firstConfidential >= 0 && lastRestricted >= 0)
        {
            lastRestricted.Should().BeLessThan(firstConfidential,
                "Restricted entries must come before Confidential entries in sorted output");
        }
    }

    [Fact]
    public void ScanAssemblies_with_minimum_level_filters_correctly()
    {
        var full = DataClassificationScanner.ScanAssemblies(TestAssembly);
        var filtered = DataClassificationScanner.ScanAssemblies(
            DataClassificationLevel.Confidential, TestAssembly);

        filtered.Entries.Should().OnlyContain(e =>
            e.Level >= DataClassificationLevel.Confidential);

        filtered.TotalCount.Should().BeLessThanOrEqualTo(full.TotalCount);
    }

    [Fact]
    public void ScanAssemblies_minimum_restricted_returns_only_restricted()
    {
        var report = DataClassificationScanner.ScanAssemblies(
            DataClassificationLevel.Restricted, TestAssembly);

        report.Entries.Should().OnlyContain(e =>
            e.Level == DataClassificationLevel.Restricted);
    }

    [Fact]
    public void ScanAssemblies_scannedAt_is_recent_utc()
    {
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        var report = DataClassificationScanner.ScanAssemblies(TestAssembly);
        var after = DateTimeOffset.UtcNow.AddSeconds(1);

        report.ScannedAt.Should().BeAfter(before).And.BeBefore(after);
    }

    [Fact]
    public void ScanAssemblies_returns_empty_for_assembly_with_no_annotations()
    {
        // Use an assembly that contains no DataClassification attributes
        // System.Runtime is safe bet — no business attributes
        var systemAssembly = typeof(object).Assembly;
        var report = DataClassificationScanner.ScanAssemblies(systemAssembly);

        report.TotalCount.Should().Be(0);
        report.Entries.Should().BeEmpty();
    }

    [Fact]
    public void ScanAssemblies_includes_assembly_name_in_entry()
    {
        var report = DataClassificationScanner.ScanAssemblies(TestAssembly);

        report.Entries.Should().AllSatisfy(e =>
            e.AssemblyName.Should().NotBeNullOrEmpty());
    }

    // ── Test fixtures — annotated entity types used by tests above ───────────

    private sealed class SamplePersonEntity
    {
        [DataClassification(DataClassificationLevel.Restricted)]
        public string NationalId { get; set; } = string.Empty;

        [DataClassification(DataClassificationLevel.Confidential)]
        public string Email { get; set; } = string.Empty;

        [DataClassification(DataClassificationLevel.Internal)]
        public string Department { get; set; } = string.Empty;

        // No attribute — should NOT appear in scan results
        public string DisplayName { get; set; } = string.Empty;
    }

    private sealed class SampleFileEntity
    {
        [DataClassification(DataClassificationLevel.Restricted)]
        public string EncryptionKey { get; set; } = string.Empty;

        [DataClassification(DataClassificationLevel.Confidential)]
        public string FilePath { get; set; } = string.Empty;
    }
}
