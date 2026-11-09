using System.Text.Json;
using System.Text.Json.Serialization;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Viettel ignores its spec's "all strings" claim (login expires_in arrived as a number). A string-typed
/// field receiving a bare number makes System.Text.Json throw. FlexibleStringJsonConverter must accept
/// both token kinds and return the string form, so status/error parsing never breaks the poll loop.
/// </summary>
public sealed class FlexibleStringJsonConverterTests
{
    private sealed record Wrapper(
        [property: JsonPropertyName("v"), JsonConverter(typeof(FlexibleStringJsonConverter))] string? Value);

    private static string? Parse(string json) =>
        JsonSerializer.Deserialize<Wrapper>(json)!.Value;

    [Theory]
    [InlineData("{\"v\":1}", "1")]
    [InlineData("{\"v\":4000}", "4000")]
    [InlineData("{\"v\":58100}", "58100")]
    [InlineData("{\"v\":9999999999}", "9999999999")] // beyond int32, must not overflow
    [InlineData("{\"v\":1.5}", "1.5")]               // fractional → raw literal preserved
    public void Read_NumberToken_ReturnsStringForm(string json, string expected)
    {
        Parse(json).Should().Be(expected);
    }

    [Fact]
    public void Read_NumberBeyondDecimalRange_DoesNotThrow_ReturnsRawLiteral()
    {
        // GetInt64/GetDecimal would throw a non-JsonException here, escaping the clients' catch and
        // breaking the poll loop. Raw-literal reading must return the digits verbatim instead.
        var huge = new string('9', 40);

        Parse($"{{\"v\":{huge}}}").Should().Be(huge);
    }

    [Theory]
    [InlineData("{\"v\":\"4000\"}", "4000")]
    [InlineData("{\"v\":\"invalid_request\"}", "invalid_request")]
    public void Read_StringToken_PassesThrough(string json, string expected)
    {
        Parse(json).Should().Be(expected);
    }

    [Fact]
    public void Read_NullToken_ReturnsNull()
    {
        Parse("{\"v\":null}").Should().BeNull();
    }

    [Fact]
    public void Read_UnexpectedToken_Throws()
    {
        var act = () => Parse("{\"v\":{\"nested\":1}}");
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Write_RoundTrips_AsString()
    {
        var json = JsonSerializer.Serialize(new Wrapper("4000"));
        json.Should().Contain("\"v\":\"4000\"");
    }
}
