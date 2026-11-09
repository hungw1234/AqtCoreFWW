using FluentAssertions;
using AqtCoreFW.SharedKernel.Abstractions;

namespace AqtCoreFW.SharedKernel.Tests.Abstractions;

/// <summary>
/// Validates V7GuidGenerator emits correct version nibbles.
/// Phase-2 (2026-05-03): NewIdMonotonic = v7 (timestamp-prefix); NewIdRandom = v4 (random).
/// Per RED-TEAM-9: sub-millisecond ordering is undefined per RFC 9562 — only assert version + cross-ms timestamp ordering.
/// </summary>
public sealed class V7GuidGeneratorTests
{
    private static int VersionNibble(Guid g) => (g.ToByteArray()[7] >> 4) & 0x0F;

    [Fact]
    public void NewIdMonotonic_ReturnsGuidV7()
    {
        var gen = new V7GuidGenerator();

        var id = gen.NewIdMonotonic();

        VersionNibble(id).Should().Be(7);
    }

    [Fact]
    public void NewIdRandom_ReturnsGuidV4()
    {
        var gen = new V7GuidGenerator();

        var id = gen.NewIdRandom();

        VersionNibble(id).Should().Be(4);
    }

    [Fact]
    public void NewIdMonotonic_TimestampPrefixIncreasesAcrossMsBoundary()
    {
        var gen = new V7GuidGenerator();

        var first = gen.NewIdMonotonic();
        Thread.Sleep(5); // span ms boundary
        var second = gen.NewIdMonotonic();

        // Compare first 6 bytes (48-bit unix_ts_ms prefix). second.timestamp >= first.timestamp.
        var firstBytes = first.ToByteArray();
        var secondBytes = second.ToByteArray();

        // Note: Guid.ToByteArray() returns mixed-endian (Data1/2/3 little-endian on Windows).
        // For v7 the first 6 bytes of the SPEC layout map to bytes [3,2,1,0,5,4] in ToByteArray() output.
        long ExtractTimestampMs(byte[] b) =>
            ((long)b[3] << 40) | ((long)b[2] << 32) | ((long)b[1] << 24) | ((long)b[0] << 16) | ((long)b[5] << 8) | b[4];

        ExtractTimestampMs(secondBytes).Should().BeGreaterThan(ExtractTimestampMs(firstBytes));
    }

    [Fact]
    public void NewIdRandom_TwoCallsProduceDistinct()
    {
        var gen = new V7GuidGenerator();

        var a = gen.NewIdRandom();
        var b = gen.NewIdRandom();

        a.Should().NotBe(b);
    }
}
