using AqtCoreFW.Rules.Domain.Entities;
using FluentAssertions;

namespace AqtCoreFW.Rules.Domain.Tests.Entities;

public sealed class RuleVersionEntityTests
{
    // T1 FK: RuleSet.Id is long (internal PK). Use a constant to represent a parent FK.
    private const long RuleSetId = 1L;
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Create_ValidParams_SetsAllProperties()
    {
        var version = RuleVersion.Create(RuleSetId, 2, """{"rules":[]}""", UserId);
        version.RuleSetId.Should().Be(RuleSetId);
        version.VersionNumber.Should().Be(2);
        version.Snapshot.Should().Be("""{"rules":[]}""");
        version.ActivatedAt.Should().BeNull();
    }

    [Fact]
    public void MarkActivated_SetsActivatedAt()
    {
        var version = RuleVersion.Create(RuleSetId, 1, "{}", UserId);
        version.MarkActivated();
        version.ActivatedAt.Should().NotBeNull();
        version.ActivatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void MarkActivated_CalledTwice_OverwritesTimestamp()
    {
        var version = RuleVersion.Create(RuleSetId, 1, "{}", UserId);
        version.MarkActivated();
        version.MarkActivated();
        version.ActivatedAt.Should().NotBeNull();
        // Second call should succeed without error
    }
}
