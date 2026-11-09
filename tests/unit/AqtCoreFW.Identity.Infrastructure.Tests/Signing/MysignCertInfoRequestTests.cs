using System.Text.Json;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using FluentAssertions;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// The Mysign cert-info endpoint rejects a request that omits <c>certificates</c> with HTTP 400
/// error 58100 ("invalid/wrong-format request"); the value must be "chain" to receive the cert chain.
/// This pins that the serialized request carries it (the field defaults so existing call sites need no change).
/// </summary>
public sealed class MysignCertInfoRequestTests
{
    [Fact]
    public void Serialized_IncludesCertificatesChain_ByDefault()
    {
        var req = new MysignCertInfoRequest(ClientId: "cid", ProfileId: "pid", UserId: "017091004878");

        var json = JsonSerializer.Serialize(req);

        json.Should().Contain("\"certificates\":\"chain\"",
            because: "the cert-info endpoint returns 400/58100 without the certificates field");
        req.Certificates.Should().Be("chain");
    }
}
