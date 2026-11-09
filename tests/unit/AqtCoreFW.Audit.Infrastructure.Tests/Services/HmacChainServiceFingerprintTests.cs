using System.Reflection;
using AqtCoreFW.Audit.Domain.Entities;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.Audit.Infrastructure.Services;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Compliance;
using NSubstitute;

namespace AqtCoreFW.Audit.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for the HMAC v1 / v2 fingerprint switch.
/// v1 fingerprint = legacy pre-QĐ 742 layout.
/// v2 fingerprint = v1 fields + Category + Severity + SchemaVersion. DataSnapshot is
/// intentionally excluded so AuditPiiAnonymizer can scrub PII without breaking the chain
/// (RTBF / Law 91/2025 compatibility — see HmacChainService.BuildFingerprint xmldoc).
/// </summary>
public sealed class HmacChainServiceFingerprintTests
{
    private static readonly Guid FixedMessageId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset FixedOccurredAt = new(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);

    private static IGuidGenerator NewGen()
    {
        var gen = Substitute.For<IGuidGenerator>();
        // Always return FixedMessageId so two entries created in the same test share an identical
        // MessageId after the pin-override below, making fingerprint comparisons deterministic.
        gen.NewIdMonotonic().Returns(FixedMessageId);
        return gen;
    }

    // Deterministic factory — pins MessageId and OccurredAt so two entries that differ only in
    // the dimensions under test produce identical fingerprints in the equality assertions.
    private static AuditLogEntry NewEntry(
        AuditCategory category = AuditCategory.AppAccess,
        AuditSeverity severity = AuditSeverity.Low,
        int schemaVersion = 2,
        string? dataSnapshot = "snapshot-original")
    {
        var entry = AuditLogEntry.Create(
            NewGen(),
            tenantId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            userId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
            userName: "tester",
            action: "Create",
            moduleName: "Identity",
            resourceType: "User",
            resourceId: "abc-123",
            dataSnapshot: dataSnapshot,
            ipAddress: "127.0.0.1",
            correlationId: "corr-1",
            category: category,
            severity: severity,
            schemaVersion: schemaVersion);

        // Pin OccurredAt so fingerprint comparisons are deterministic across entries.
        // MessageId is already pinned via gen stub above.
        SetPrivateProperty(entry, nameof(AuditLogEntry.OccurredAt), FixedOccurredAt);
        return entry;
    }

    private static void SetPrivateProperty(object target, string name, object value) =>
        typeof(AuditLogEntry)
            .GetProperty(name, BindingFlags.Public | BindingFlags.Instance)!
            .SetValue(target, value);

    [Fact]
    public void V1_AndV2_FingerprintsDiffer_ForSameInputData()
    {
        var v1 = NewEntry(schemaVersion: 1);
        var v2 = NewEntry(schemaVersion: 2);

        var fpV1 = HmacChainService.BuildFingerprint(v1);
        var fpV2 = HmacChainService.BuildFingerprint(v2);

        fpV1.Should().NotBe(fpV2);
        fpV2.Should().StartWith("v2|", "v2 fingerprints carry an explicit version prefix");
    }

    [Fact]
    public void V2_Fingerprint_IncludesCategorySeverityAndSchemaVersion()
    {
        var entry = NewEntry(
            category: AuditCategory.AccountManagement,
            severity: AuditSeverity.High,
            schemaVersion: 2);

        var fp = HmacChainService.BuildFingerprint(entry);

        // Category=4 (AccountManagement), Severity=3 (High)
        fp.Should().Contain("|4|");
        fp.Should().Contain("|3|");
        fp.Should().EndWith("|2");
    }

    [Fact]
    public void V2_Fingerprint_DoesNotIncludeDataSnapshot_SoAnonymizerPreservesChain()
    {
        // Two entries identical except DataSnapshot — must produce identical v2 fingerprints
        // so AuditPiiAnonymizer.AnonymizeAsync (which mutates DataSnapshot) does not
        // invalidate the HMAC chain.
        var original   = NewEntry(dataSnapshot: "snapshot-with-pii");
        var anonymised = NewEntry(dataSnapshot: "[DA AN DANH]");

        var fpOriginal   = HmacChainService.BuildFingerprint(original);
        var fpAnonymised = HmacChainService.BuildFingerprint(anonymised);

        fpOriginal.Should().Be(fpAnonymised);
    }

    [Fact]
    public void V1_Fingerprint_DoesNotIncludeCategoryOrSeverity_LegacyChainCompat()
    {
        // SchemaVersion <= 1 (legacy rows backfilled by M1) — fingerprint MUST exclude
        // Category + Severity + SchemaVersion so existing HMAC sigs continue to verify
        // after the migration runs.
        var legacy = NewEntry(
            category: AuditCategory.Error,
            severity: AuditSeverity.Critical,
            schemaVersion: 1);

        var fp = HmacChainService.BuildFingerprint(legacy);

        fp.Should().NotStartWith("v2|");
        fp.Should().NotContain("|3|"); // Error category int = 3 — sentinel-style assert
    }

    [Fact]
    public void Fingerprint_IsDeterministic_ForIdenticalInputs()
    {
        var a = NewEntry();
        var b = NewEntry();

        HmacChainService.BuildFingerprint(a)
            .Should().Be(HmacChainService.BuildFingerprint(b));
    }
}
