using System.Text.Json;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// End-to-end proof that the FlexibleStringJsonConverter annotations on the real Mysign DTOs let
/// numeric status/error tokens parse instead of throwing — the failure the status-poll loop would hit
/// if Viettel returns these as bare numbers (as it did for login expires_in).
/// </summary>
public sealed class MysignNumericFieldParsingTests
{
    // Mirrors the per-client JsonOpts (case-insensitive). Property-level converters apply regardless.
    private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

    [Fact]
    public void SignHashResponse_NumericStatus_Parses()
    {
        var dto = JsonSerializer.Deserialize<MysignSignHashResponse>(
            "{\"status\":1,\"signatures\":[\"abc\"]}", Opts)!;

        dto.Status.Should().Be("1");
        MysignStatusMapper.Map(dto.Status).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void SignHashResponse_NumericStatusWaiting_MapsNonTerminal()
    {
        var dto = JsonSerializer.Deserialize<MysignSignHashResponse>("{\"status\":4000}", Opts)!;

        dto.Status.Should().Be("4000");
        MysignStatusMapper.Map(dto.Status).IsTerminal.Should().BeFalse();
    }

    [Fact]
    public void SignHashResponse_NumericError_Parses()
    {
        var dto = JsonSerializer.Deserialize<MysignSignHashResponse>("{\"error\":58100}", Opts)!;

        dto.Error.Should().Be("58100");
    }

    [Fact]
    public void SignHashResponse_StringStatus_StillParses()
    {
        var dto = JsonSerializer.Deserialize<MysignSignHashResponse>("{\"status\":\"4002\"}", Opts)!;

        dto.Status.Should().Be("4002");
    }

    [Fact]
    public void LoginResponse_NumericError_Parses()
    {
        var dto = JsonSerializer.Deserialize<MysignLoginResponse>("{\"error\":12345}", Opts)!;

        dto.Error.Should().Be("12345");
    }
}
