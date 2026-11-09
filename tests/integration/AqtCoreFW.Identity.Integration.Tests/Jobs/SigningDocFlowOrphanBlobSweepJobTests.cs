using AqtCoreFW.Api.Jobs;
using AqtCoreFW.Files.Domain.Services;
using FluentAssertions;

namespace AqtCoreFW.Identity.Integration.Tests.Jobs;

/// <summary>
/// Pure-function coverage of the orphan-selection rule in <see cref="SigningDocFlowOrphanBlobSweepJob"/>.
/// No fixture — the decision logic (age cutoff + reference check) is the part with branches worth pinning;
/// the surrounding storage/DB orchestration is exercised by the live doc-flow integration tests.
/// </summary>
public sealed class SigningDocFlowOrphanBlobSweepJobTests
{
    private static readonly DateTimeOffset Now = new(2026, 06, 16, 12, 00, 00, TimeSpan.Zero);
    private static readonly DateTimeOffset Cutoff = Now.AddHours(-24);

    private static StorageObjectInfo Obj(string key, DateTimeOffset? lastModified)
        => new(key, lastModified, Size: 1024);

    [Fact]
    public void SelectOrphanKeys_OldUnreferencedBlob_IsReclaimed()
    {
        var objects = new[] { Obj("t1/d1/orphan.signed.pdf", Cutoff.AddHours(-1)) };
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        var orphans = SigningDocFlowOrphanBlobSweepJob.SelectOrphanKeys(objects, referenced, Cutoff);

        orphans.Should().ContainSingle().Which.Should().Be("t1/d1/orphan.signed.pdf");
    }

    [Fact]
    public void SelectOrphanKeys_ReferencedBlob_IsKept()
    {
        const string key = "t1/d1/completed.signed.pdf";
        var objects = new[] { Obj(key, Cutoff.AddHours(-1)) };
        var referenced = new HashSet<string>([key], StringComparer.Ordinal);

        var orphans = SigningDocFlowOrphanBlobSweepJob.SelectOrphanKeys(objects, referenced, Cutoff);

        orphans.Should().BeEmpty("a blob whose key a record references must never be reclaimed");
    }

    [Fact]
    public void SelectOrphanKeys_RecentBlobWithinGrace_IsKept()
    {
        // Newer than the cutoff — could be an in-flight upload whose Completed commit has not landed.
        var objects = new[] { Obj("t1/d1/inflight.signed.pdf", Cutoff.AddMinutes(5)) };
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        var orphans = SigningDocFlowOrphanBlobSweepJob.SelectOrphanKeys(objects, referenced, Cutoff);

        orphans.Should().BeEmpty("a blob inside the grace window may still be mid-flight");
    }

    [Fact]
    public void SelectOrphanKeys_NullTimestamp_IsKept()
    {
        var objects = new[] { Obj("t1/d1/no-timestamp.signed.pdf", lastModified: null) };
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        var orphans = SigningDocFlowOrphanBlobSweepJob.SelectOrphanKeys(objects, referenced, Cutoff);

        orphans.Should().BeEmpty("a blob whose age is unknown must not be reclaimed");
    }

    [Fact]
    public void SelectOrphanKeys_MixedSet_ReclaimsOnlyOldUnreferenced()
    {
        const string referencedKey = "t1/d1/keep.signed.pdf";
        var objects = new[]
        {
            Obj("t1/d1/orphan-old.signed.pdf", Cutoff.AddHours(-2)),  // reclaim
            Obj(referencedKey,                  Cutoff.AddHours(-2)),  // keep — referenced
            Obj("t1/d1/orphan-new.signed.pdf", Cutoff.AddHours(2)),   // keep — within grace
        };
        var referenced = new HashSet<string>([referencedKey], StringComparer.Ordinal);

        var orphans = SigningDocFlowOrphanBlobSweepJob.SelectOrphanKeys(objects, referenced, Cutoff);

        orphans.Should().ContainSingle().Which.Should().Be("t1/d1/orphan-old.signed.pdf");
    }
}
