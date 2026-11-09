using System.Text.Json;
using AqtCoreFW.Forms.Application.Services;
using FluentAssertions;

namespace AqtCoreFW.Forms.Application.Tests.Services;

/// <summary>
/// Unit tests for <see cref="JsonValueNormalizer"/>.
/// Pins the JsonElement → native CLR conversion contract introduced in P6-FU2 to close the
/// JsonElement-vs-CLR asymmetry between Json-mode and Table-mode form submission storage.
/// Each fact pins one <see cref="JsonValueKind"/> branch + the pass-through path for non-JsonElement input.
/// </summary>
public sealed class JsonValueNormalizerTests
{
    // ─── Scalar kinds ─────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_StringElement_ReturnsNativeString()
    {
        var element = JsonDocument.Parse("\"Nguyen Van A\"").RootElement;
        var result = JsonValueNormalizer.Normalize(element);
        result.Should().BeOfType<string>().And.Be("Nguyen Van A");
    }

    [Fact]
    public void Normalize_IntegerElement_ReturnsLong()
    {
        var element = JsonDocument.Parse("42").RootElement;
        var result = JsonValueNormalizer.Normalize(element);
        result.Should().BeOfType<long>().And.Be(42L);
    }

    [Fact]
    public void Normalize_DecimalElement_ReturnsDecimal_NotDouble()
    {
        // Decimal preferred over double — preserves financial-data precision and matches the
        // FormDataPreprocessor write-path so read/write round-trip is lossless.
        var element = JsonDocument.Parse("3.14").RootElement;
        var result = JsonValueNormalizer.Normalize(element);
        result.Should().BeOfType<decimal>().And.Be(3.14m);
    }

    [Fact]
    public void Normalize_NumberOutOfInt64Range_ReturnsDecimal()
    {
        // Just above long.MaxValue (9223372036854775807). Falls through TryGetInt64 → TryGetDecimal.
        var element = JsonDocument.Parse("9223372036854775808").RootElement;
        var result = JsonValueNormalizer.Normalize(element);
        result.Should().BeOfType<decimal>().And.Be(9223372036854775808m);
    }

    [Fact]
    public void Normalize_TrueElement_ReturnsBool()
    {
        var element = JsonDocument.Parse("true").RootElement;
        var result = JsonValueNormalizer.Normalize(element);
        result.Should().BeOfType<bool>().And.Be(true);
    }

    [Fact]
    public void Normalize_FalseElement_ReturnsBool()
    {
        var element = JsonDocument.Parse("false").RootElement;
        var result = JsonValueNormalizer.Normalize(element);
        result.Should().BeOfType<bool>().And.Be(false);
    }

    [Fact]
    public void Normalize_NullElement_ReturnsNull()
    {
        var element = JsonDocument.Parse("null").RootElement;
        var result = JsonValueNormalizer.Normalize(element);
        result.Should().BeNull();
    }

    // ─── Composite kinds ──────────────────────────────────────────────────────

    [Fact]
    public void Normalize_ArrayElement_ReturnsListOfNormalizedItems()
    {
        var element = JsonDocument.Parse("""["A", 1, true]""").RootElement;
        var result = JsonValueNormalizer.Normalize(element);
        result.Should().BeAssignableTo<List<object?>>();
        var list = (List<object?>)result!;
        list.Should().HaveCount(3);
        list[0].Should().BeOfType<string>().And.Be("A");
        list[1].Should().BeOfType<long>().And.Be(1L);
        list[2].Should().BeOfType<bool>().And.Be(true);
    }

    [Fact]
    public void Normalize_NestedObjectElement_ReturnsDictRecursively()
    {
        var element = JsonDocument.Parse("""{"name":"X","age":5,"meta":{"flag":false}}""").RootElement;
        var result = JsonValueNormalizer.Normalize(element);
        result.Should().BeAssignableTo<Dictionary<string, object?>>();
        var dict = (Dictionary<string, object?>)result!;
        dict["name"].Should().Be("X");
        dict["age"].Should().Be(5L);
        dict["meta"].Should().BeAssignableTo<Dictionary<string, object?>>();
        ((Dictionary<string, object?>)dict["meta"]!)["flag"].Should().Be(false);
    }

    // ─── Pass-through ─────────────────────────────────────────────────────────

    [Fact]
    public void Normalize_NativeClrValue_PassesThroughUnchanged()
    {
        JsonValueNormalizer.Normalize("text").Should().Be("text");
        JsonValueNormalizer.Normalize(42).Should().Be(42);
        JsonValueNormalizer.Normalize(null).Should().BeNull();
        JsonValueNormalizer.Normalize(true).Should().Be(true);
    }

    // ─── Dict-level API ───────────────────────────────────────────────────────

    [Fact]
    public void NormalizeDict_MixedSource_AllValuesNormalized()
    {
        var doc = JsonDocument.Parse("""{"name":"A","age":30,"active":true,"tags":["x"]}""");
        var source = new Dictionary<string, object?>();
        foreach (var prop in doc.RootElement.EnumerateObject())
            source[prop.Name] = prop.Value;

        var result = JsonValueNormalizer.NormalizeDict(source);

        result["name"].Should().BeOfType<string>().And.Be("A");
        result["age"].Should().BeOfType<long>().And.Be(30L);
        result["active"].Should().BeOfType<bool>().And.Be(true);
        result["tags"].Should().BeAssignableTo<List<object?>>();
    }

    [Fact]
    public void NormalizeDict_EmptySource_ReturnsEmpty()
    {
        var result = JsonValueNormalizer.NormalizeDict(new Dictionary<string, object?>());
        result.Should().BeEmpty();
    }
}
