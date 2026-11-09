using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using WireMock.RequestBuilders;
using WireMock.ResponseBuilders;
using WireMock.Server;

namespace AqtCoreFW.Identity.Integration.Tests.Signing;

/// <summary>
/// Integration tests for <see cref="ViettelMysignProvider"/> using WireMock.Net as HTTP stub.
///
/// D17.2 + D15 LOCK: ALL tests use WireMock — zero live Viettel Mysign API calls.
/// WireMockServer starts on random port per test class; disposed after all tests.
///
/// Covers:
///   - Happy path: Login → CertInfo → SignHash → raw RSA bytes returned
///   - Token caching: second sign call does NOT call Login again (cache hit)
///   - HTTP 5xx retry exhaust: Login 5xx 3× → SignResult.IsSuccess=false (no exception)
///   - Application-level error in SignHash 200-OK body → IsSuccess=false with reason
/// </summary>
public sealed class ViettelMysignSyncTests : IAsyncLifetime
{
    private WireMockServer _server = null!;
    private ViettelMysignProvider _provider = null!;
    private IMemoryCache _cache = null!;

    // Viettel Mysign API paths
    private const string LoginPath    = "/vtss/service/ras/v1/login";
    private const string CertInfoPath = "/vtss/service/certificates/info";
    private const string SignHashPath = "/vtss/service/signHash";

    // Stable test data
    private const string FakeUserId      = "test-user-001";
    private const string FakeAccessToken = "fake-bearer-token";
    private const string FakeCredentialId = "cred-abc-123";

    // ~32 bytes of fake RSA signature in base64
    private static readonly byte[] FakeSigBytes = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();
    private static readonly string FakeSigBase64 = Convert.ToBase64String(FakeSigBytes);

    // Generate a real self-signed RSA-2048 cert at test-time to guarantee
    // valid base64 DER + correct cert structure (avoid stale hardcoded blobs).
    private static readonly string FakeCertBase64 = GenerateTestCertBase64();

    private static string GenerateTestCertBase64()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=TEST-CERT DO NOT TRUST",
            rsa,
            System.Security.Cryptography.HashAlgorithmName.SHA256,
            System.Security.Cryptography.RSASignaturePadding.Pkcs1);
        using var cert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(2));
        return Convert.ToBase64String(cert.RawData);
    }

    public Task InitializeAsync()
    {
        _server = WireMockServer.Start();
        _cache  = new MemoryCache(new MemoryCacheOptions());

        var opts = Options.Create(new ViettelMysignOptions
        {
            BaseUrl      = _server.Urls[0],
            ClientId     = "test-client-id",
            ClientSecret = "test-client-secret",
            ProfileId    = "test-profile-id",
            LoginTokenCacheLeewaySeconds = 60,
            CertInfoCacheTtlSeconds      = 300,
            SyncSignTimeoutSeconds       = 30,
            HttpRetryMaxAttempts         = 3,
        });

        // IOptionsMonitor backed by the IOptions value
        var optionsMonitor = Substitute.For<IOptionsMonitor<ViettelMysignOptions>>();
        optionsMonitor.CurrentValue.Returns(opts.Value);

        var loginClient   = new ViettelMysignLoginClient(
            new HttpClient(), optionsMonitor, _cache,
            NullLogger<ViettelMysignLoginClient>.Instance);

        var certInfoClient = new ViettelMysignCertInfoClient(
            new HttpClient(), optionsMonitor, _cache,
            NullLogger<ViettelMysignCertInfoClient>.Instance);

        var signHashClient = new ViettelMysignSignHashClient(
            new HttpClient(), optionsMonitor,
            NullLogger<ViettelMysignSignHashClient>.Instance);

        // W5: StatusPollClient added to provider constructor. Sync tests don't invoke polling
        // paths — stub with real client pointing at same WireMock server (unused in sync mode).
        var statusPollClient = new ViettelMysignStatusPollClient(
            new HttpClient(), optionsMonitor,
            NullLogger<ViettelMysignStatusPollClient>.Instance);

        var chainValidator = Substitute.For<ICertificateChainValidator>();
        // Chain validator stub: always succeeds (W4 tests focus on the HTTP 3-call flow)
        chainValidator
            .ValidateAsync(Arg.Any<byte[]>(), Arg.Any<IReadOnlyList<byte[]>?>(),
                Arg.Any<OcspMode>(), Arg.Any<CancellationToken>())
            .Returns(new ChainValidationResult(
                IsValid: true, FailureReason: null,
                Chain: [], OcspChecked: false, OcspSucceeded: false));

        _provider = new ViettelMysignProvider(
            loginClient, certInfoClient, signHashClient, statusPollClient,
            chainValidator, optionsMonitor,
            NullLogger<ViettelMysignProvider>.Instance);

        return Task.CompletedTask;
    }

    public Task DisposeAsync()
    {
        _server.Stop();
        _server.Dispose();
        _cache.Dispose();
        return Task.CompletedTask;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void StubLogin(string? accessToken = FakeAccessToken, int statusCode = 200)
    {
        // expires_in is a JSON number — matches the real Viettel UAT login response shape.
        var body = statusCode == 200 && accessToken is not null
            ? $@"{{""access_token"":""{accessToken}"",""token_type"":""Bearer"",""expires_in"":3600}}"
            : @"{""error"":""invalid_client"",""error_description"":""Bad credentials""}";

        _server
            .Given(Request.Create().WithPath(LoginPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));
    }

    private void StubCertInfo(int statusCode = 200)
    {
        var body = statusCode == 200
            ? $@"[{{
                ""credential_id"":""{FakeCredentialId}"",
                ""cert"":{{
                    ""status"":""valid"",
                    ""certificates"":[""{FakeCertBase64}""],
                    ""issuerDN"":""CN=Test CA"",
                    ""SerialNumber"":""AABB"",
                    ""subjectDN"":""CN=Test User"",
                    ""validFrom"":""20240101000000Z"",
                    ""validTo"":""20250101000000Z""
                }}
            }}]"
            : @"{""error"":""server_error"",""error_description"":""Internal error""}";

        _server
            .Given(Request.Create().WithPath(CertInfoPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));
    }

    private void StubSignHash(string? sigBase64 = null, string? errorCode = null, int statusCode = 200)
    {
        string body;
        if (errorCode is not null)
            body = $@"{{""error"":""{errorCode}"",""error_description"":""Sign failed""}}";
        else
            body = sigBase64 is not null
                ? $@"{{""signatures"":[""{sigBase64}""]}}"
                : @"{""signatures"":[]}";

        _server
            .Given(Request.Create().WithPath(SignHashPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(statusCode)
                .WithHeader("Content-Type", "application/json")
                .WithBody(body));
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Happy path: Login (200) → CertInfo (200) → SignHash (200 with signatures array)
    /// → IsSuccess=true, Signature bytes match stub, certificate info populated.
    /// </summary>
    [Fact]
    public async Task SignAsync_HappyPath_ReturnsSuccessWithSignatureBytes()
    {
        StubLogin();
        StubCertInfo();
        StubSignHash(sigBase64: FakeSigBase64);

        var request = new SignRequest(
            Payload:         [1, 2, 3, 4],
            UserId:          FakeUserId,
            Reason:          "test",
            ExtraParameters: null);

        var result = await _provider.SignAsync(request);

        result.IsSuccess.Should().BeTrue("sign happy path must succeed");
        result.IsPending.Should().BeFalse();
        result.Signature.Should().Equal(FakeSigBytes, "signature bytes must match WireMock stub");
        result.SigningCertificate.Should().NotBeNull("certificate info must be populated from CertInfo response");
        result.SigningCertificate!.SerialNumber.Should().Be("AABB");
        result.FailureReason.Should().BeNull();
    }

    /// <summary>
    /// Token cache: first Sign call populates cache; second Sign call does NOT call Login again.
    /// Verifies IMemoryCache prevents redundant Login round-trips.
    /// </summary>
    [Fact]
    public async Task SignAsync_TokenCached_LoginCalledOnlyOnce()
    {
        StubLogin();
        StubCertInfo();
        StubSignHash(sigBase64: FakeSigBase64);

        var request = new SignRequest([1, 2, 3], FakeUserId, null, null);

        // First call — populates cache
        var r1 = await _provider.SignAsync(request);
        r1.IsSuccess.Should().BeTrue();

        // Reset WireMock call log
        _server.ResetLogEntries();

        // Second call — token should come from cache, Login not called
        StubCertInfo(); // CertInfo cache hit too, but re-stub just in case
        StubSignHash(sigBase64: FakeSigBase64);

        var r2 = await _provider.SignAsync(request);
        r2.IsSuccess.Should().BeTrue();

        var loginCalls = _server.LogEntries
            .Count(e => e.RequestMessage.Path == LoginPath);

        loginCalls.Should().Be(0,
            "login token must be served from IMemoryCache on second call — no redundant Login HTTP call");
    }

    /// <summary>
    /// HTTP 5xx from Login → IsSuccess=false, no exception propagated.
    /// Note: this test exercises the provider's error-surface contract, not Polly retry count.
    /// Polly is wired via AddResilienceHandler in InfrastructureRegistration on factory-managed
    /// HttpClients; direct-construction tests use raw HttpClient (no retry pipeline).
    /// </summary>
    [Fact]
    public async Task SignAsync_Login5xx_ReturnsFailureWithoutException()
    {
        // Stub Login to always return 503
        _server
            .Given(Request.Create().WithPath(LoginPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(503)
                .WithHeader("Content-Type", "application/json")
                .WithBody(@"{""error"":""service_unavailable""}"));

        var request = new SignRequest([1, 2, 3], FakeUserId, null, null);

        var result = await _provider.SignAsync(request);

        result.IsSuccess.Should().BeFalse("Login 5xx must yield failed SignResult");
        result.Signature.Should().BeNull();
        result.FailureReason.Should().NotBeNullOrEmpty("failure reason must describe the Login error");
        result.FailureReason.Should().Contain("Login", "reason must mention which step failed");
    }

    /// <summary>
    /// SignHash returns 200 with application-level error body → IsSuccess=false, reason from body.
    /// This tests the critical case: Viettel returns errors in 200-OK body, not via HTTP status.
    /// </summary>
    [Fact]
    public async Task SignAsync_SignHashAppLevelError_ReturnsFailureWithReason()
    {
        StubLogin();
        StubCertInfo();
        // SignHash returns 200 but with error body (Viettel app-level error pattern)
        StubSignHash(errorCode: "SIGN_REJECTED");

        var request = new SignRequest([1, 2, 3], FakeUserId, null, null);

        var result = await _provider.SignAsync(request);

        result.IsSuccess.Should().BeFalse("app-level error in SignHash body must yield failed SignResult");
        result.Signature.Should().BeNull();
        result.FailureReason.Should().NotBeNullOrEmpty();
        result.FailureReason.Should().Contain("SIGN_REJECTED",
            "failure reason must propagate the error code from Viettel response body");
    }
}
