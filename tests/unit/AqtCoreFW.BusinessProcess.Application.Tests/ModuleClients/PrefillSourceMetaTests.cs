using AqtCoreFW.BusinessProcess.Application.ModuleClients;
using FluentAssertions;

namespace AqtCoreFW.BusinessProcess.Application.Tests.ModuleClients;

/// <summary>
/// Unit tests for the PrefillSource envelope parser. Covers happy path + every silent-fail branch
/// (the resolver relies on TryParse never throwing — partial-fail tolerance per D4).
/// </summary>
public sealed class PrefillSourceMetaTests
{
    [Fact]
    public void TryParse_ValidEnvelope_ReturnsMeta()
    {
        var sourceId = Guid.NewGuid();
        var json = $"{{\"sourceFormTemplateId\":\"{sourceId}\",\"sourceFieldKey\":\"firstName\"}}";

        var meta = PrefillSourceMeta.TryParse(json);

        meta.Should().NotBeNull();
        meta!.SourceFormTemplatePublicId.Should().Be(sourceId);
        meta.SourceFieldKey.Should().Be("firstName");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_NullOrWhitespace_ReturnsNull(string? json)
    {
        PrefillSourceMeta.TryParse(json).Should().BeNull();
    }

    [Fact]
    public void TryParse_MalformedJson_ReturnsNull()
    {
        PrefillSourceMeta.TryParse("not a json {").Should().BeNull();
    }

    [Fact]
    public void TryParse_EmptyGuidSource_ReturnsNull()
    {
        var json = $"{{\"sourceFormTemplateId\":\"{Guid.Empty}\",\"sourceFieldKey\":\"x\"}}";
        PrefillSourceMeta.TryParse(json).Should().BeNull();
    }

    [Fact]
    public void TryParse_MissingSourceFieldKey_ReturnsNull()
    {
        var json = $"{{\"sourceFormTemplateId\":\"{Guid.NewGuid()}\"}}";
        PrefillSourceMeta.TryParse(json).Should().BeNull();
    }

    [Fact]
    public void TryParse_ExtraKeys_IgnoredForwardCompatible()
    {
        var sourceId = Guid.NewGuid();
        var json = $"{{\"sourceFormTemplateId\":\"{sourceId}\",\"sourceFieldKey\":\"k\",\"futureKey\":\"ignored\"}}";

        var meta = PrefillSourceMeta.TryParse(json);

        meta.Should().NotBeNull();
        meta!.SourceFieldKey.Should().Be("k");
    }
}
