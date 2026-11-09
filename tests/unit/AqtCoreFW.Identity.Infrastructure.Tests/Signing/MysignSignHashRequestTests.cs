using System.Text.Json;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// The Mysign signHash body is a distinct schema from login/cert-info (SDK v1.9 §1.2.3): PascalCase
/// <c>credentialID</c>, camelCase <c>hashAlgo</c>/<c>signAlgo</c>, and a mandatory
/// <c>client_secret</c>/<c>numSignatures</c>/<c>documents</c> set. Sending the login-style snake_case
/// shape (or omitting the mandatory fields) yields HTTP 400 error 58100. These pin the wire shape.
/// </summary>
public sealed class MysignSignHashRequestTests
{
    private static MysignSignHashRequest BuildRequest() => new(
        CredentialId: "017091004878_9710405_20260603200344",
        ClientId: "cid",
        ClientSecret: "csecret",
        NumSignatures: 1,
        Documents: [new MysignSignDocument(1, "U21va2VQcm9iZQ==")],
        Hash: ["aGFzaA=="],
        HashAlgo: "2.16.840.1.101.3.4.2.1",
        SignAlgo: "1.2.840.113549.1.1.1",
        Async: 0);

    [Fact]
    public void Serialized_UsesSpecFieldNames()
    {
        var json = JsonSerializer.Serialize(BuildRequest());

        json.Should().Contain("\"credentialID\":", because: "Viettel keys the cert id PascalCase");
        json.Should().Contain("\"hashAlgo\":", because: "spec uses camelCase hashAlgo, not hash_algorithm");
        json.Should().Contain("\"signAlgo\":", because: "spec uses camelCase signAlgo, not sign_algorithm");
        json.Should().NotContain("hash_algorithm");
        json.Should().NotContain("sign_algorithm");
        json.Should().NotContain("credential_id");
        // profile_id / user_id are NOT part of the signHash schema and must not be emitted.
        json.Should().NotContain("profile_id");
        json.Should().NotContain("user_id");
    }

    [Fact]
    public void Serialized_CarriesMandatoryFields()
    {
        var json = JsonSerializer.Serialize(BuildRequest());

        json.Should().Contain("\"client_secret\":\"csecret\"",
            because: "signHash requires client_secret in the body");
        json.Should().Contain("\"numSignatures\":1");
        json.Should().Contain("\"hash\":[\"aGFzaA==\"]",
            because: "hash must be a JSON array; a bare string 500s with a Jackson error");
    }

    [Fact]
    public void Serialized_DocumentsCarryIdAndName()
    {
        var json = JsonSerializer.Serialize(BuildRequest());

        json.Should().Contain("\"documents\":[", because: "documents is mandatory");
        json.Should().Contain("\"document_id\":1");
        json.Should().Contain("\"document_name\":\"U21va2VQcm9iZQ==\"");
    }
}
