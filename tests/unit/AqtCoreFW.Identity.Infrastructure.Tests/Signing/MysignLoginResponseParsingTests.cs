using System.Text.Json;
using System.Text.Json.Serialization;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Deserialization contract for the Viettel Mysign login response. The real UAT server returns
/// <c>expires_in</c> as a JSON NUMBER (e.g. 3393); an earlier DTO assumed a string and threw
/// "The JSON value could not be converted ... Path: $.expires_in" at runtime. These tests lock the
/// shape: expires_in binds from a number, and (defensively) from a stringified number too.
///
/// The options mirror ViettelMysignLoginClient.JsonOpts — same three settings the client deserializes with.
/// </summary>
public sealed class MysignLoginResponseParsingTests
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    [Fact]
    public void Deserialize_ExpiresInAsNumber_Binds()
    {
        // Exact shape observed from the real Viettel UAT login response.
        const string json = """{"access_token":"abc.def.ghi","expires_in":3393}""";

        var resp = JsonSerializer.Deserialize<MysignLoginResponse>(json, JsonOpts)!;

        resp.AccessToken.Should().Be("abc.def.ghi");
        resp.ExpiresIn.Should().Be(3393);
    }

    [Fact]
    public void Deserialize_ExpiresInAsString_BindsDefensively()
    {
        // Defensive: a stringified number must also bind (AllowReadingFromString).
        const string json = """{"access_token":"abc.def.ghi","expires_in":"3600"}""";

        var resp = JsonSerializer.Deserialize<MysignLoginResponse>(json, JsonOpts)!;

        resp.ExpiresIn.Should().Be(3600);
    }

    [Fact]
    public void Deserialize_ExpiresInAbsent_IsNull()
    {
        const string json = """{"access_token":"abc.def.ghi"}""";

        var resp = JsonSerializer.Deserialize<MysignLoginResponse>(json, JsonOpts)!;

        resp.ExpiresIn.Should().BeNull();
        resp.AccessToken.Should().Be("abc.def.ghi");
    }
}
