using AqtCoreFW.Files.Domain.Entities;

namespace AqtCoreFW.Files.Domain.Tests.Entities;

/// <summary>
/// Unit tests for FileId strongly-typed value object.
/// Verifies identity generation, wrapping, string conversion, and equality.
/// </summary>
public sealed class FileIdTests
{
    [Fact]
    public void New_GeneratesNonEmptyGuid()
    {
        var id = FileId.New();

        id.Value.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public void New_EachCallGeneratesDifferentGuid()
    {
        var id1 = FileId.New();
        var id2 = FileId.New();

        id1.Should().NotBe(id2);
    }

    [Fact]
    public void From_WrapsGivenGuid()
    {
        var guid = Guid.NewGuid();

        var id = FileId.From(guid);

        id.Value.Should().Be(guid);
    }

    [Fact]
    public void ToString_ReturnsGuidString()
    {
        var guid = Guid.NewGuid();
        var id = FileId.From(guid);

        id.ToString().Should().Be(guid.ToString());
    }

    [Fact]
    public void Equality_TwoIdsFromSameGuid_AreEqual()
    {
        var guid = Guid.NewGuid();

        var id1 = FileId.From(guid);
        var id2 = FileId.From(guid);

        id1.Should().Be(id2);
    }

    [Fact]
    public void Equality_TwoIdsFromDifferentGuids_AreNotEqual()
    {
        var id1 = FileId.New();
        var id2 = FileId.New();

        id1.Should().NotBe(id2);
    }
}
