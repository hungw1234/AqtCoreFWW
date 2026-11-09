using AqtCoreFW.Identity.Application.Signing;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Application.Tests.Signing;

/// <summary>
/// <see cref="SignaturePlacement.TryParse"/> — the first reader of the long-dormant
/// <c>SigningRequest.Placement</c> JSON. Pins forward-compatibility (M6: unknown fields ignored so a newer FE
/// never breaks an older server) and the fail-soft contract (blank/malformed/degenerate → invisible sign).
/// </summary>
public sealed class SignaturePlacementTests
{
    [Fact]
    public void TryParse_FullPayload_BindsEveryField()
    {
        var pid = Guid.NewGuid();
        string json = $$"""
            {"page":2,"x":72.5,"y":120,"w":200,"h":100,"profilePublicId":"{{pid}}","visualType":"full"}
            """;

        SignaturePlacement.TryParse(json, out var p).Should().BeTrue();
        p!.Page.Should().Be(2);
        p.X.Should().Be(72.5f);
        p.Y.Should().Be(120f);
        p.W.Should().Be(200f);
        p.H.Should().Be(100f);
        p.ProfilePublicId.Should().Be(pid);
        p.VisualType.Should().Be("full");
    }

    [Fact]
    public void TryParse_UnknownFields_AreIgnored_ForwardCompatible()
    {
        // A future FE adds keys the current server does not know — it must still parse (M6), not reject.
        string json = """{"x":10,"y":10,"w":50,"h":30,"rotation":90,"opacity":0.5,"future":"x"}""";

        SignaturePlacement.TryParse(json, out var p).Should().BeTrue();
        p!.W.Should().Be(50f);
        p.ProfilePublicId.Should().BeNull("omitted → null, not an error");
        p.VisualType.Should().BeNull();
    }

    [Fact]
    public void TryParse_CaseInsensitiveKeys_Bind()
    {
        SignaturePlacement.TryParse("""{"X":1,"Y":2,"W":3,"H":4}""", out var p).Should().BeTrue();
        p!.W.Should().Be(3f);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-json")]
    [InlineData("{")]
    public void TryParse_BlankOrMalformed_ReturnsFalse(string? json)
    {
        SignaturePlacement.TryParse(json, out var p).Should().BeFalse();
        p.Should().BeNull();
    }

    [Theory]
    [InlineData("""{"x":0,"y":0,"w":0,"h":100}""")]   // zero width
    [InlineData("""{"x":0,"y":0,"w":100,"h":0}""")]   // zero height
    [InlineData("""{"x":0,"y":0,"w":-5,"h":100}""")]  // negative width
    public void TryParse_NonPositiveRect_ReturnsFalse(string json)
    {
        SignaturePlacement.TryParse(json, out var p).Should().BeFalse(
            "a degenerate rect means no usable stamp — sign invisibly rather than emit a broken widget");
        p.Should().BeNull();
    }
}
