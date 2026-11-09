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
/// Integration tests for Viettel Mysign async-poll mode (W5).
/// Uses WireMock.Net — zero live Viettel API calls.
///
/// Test cases:
///   1. InlinePoll happy path: state machine 4000 → 6000 → 1+signatures → success.
///   2. InlinePoll timeout: always returns 4000, exhausts MaxAttempts → failure.
///   3. InlinePoll user reject: 4000 → 4000 → 4002 → IsSuccess=false "User rejected".
///   4. DeferredPoll: SignAsync returns IsPending=true immediately; ConfirmAsync resolves success.
///   5. Cancellation: poll loop cancelled → OperationCanceledException propagates (not swallowed).
///
/// PollIntervalMilliseconds overridden to 50ms in all tests for fast wall-clock execution.
/// </summary>
public sealed class ViettelMysignAsyncPollTests : IAsyncLifetime
{
    private WireMockServer _server = null!;
    private ViettelMysignProvider _provider = null!;
    private IMemoryCache _cache = null!;

    // API paths
    private const string LoginPath    = "/vtss/service/ras/v1/login";
    private const string CertInfoPath = "/vtss/service/certificates/info";
    private const string SignHashPath = "/vtss/service/signHash";
    private const string StatusPath   = "/vtss/service/requests/status";

    // Stable test data
    private const string FakeUserId       = "async-test-user";
    private const string FakeAccessToken  = "fake-async-bearer";
    private const string FakeCredentialId = "cred-async-456";
    private const string FakeTxId         = "tx-async-uuid-1234";

    private static readonly byte[] FakeSigBytes  = Enumerable.Range(0, 32).Select(i => (byte)(i + 10)).ToArray();
    private static readonly string FakeSigBase64 = Convert.ToBase64String(FakeSigBytes);

    private static readonly string FakeCertBase64 = GenerateTestCertBase64();

    private static string GenerateTestCertBase64()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(2048);
        var req = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=ASYNC-TEST DO NOT TRUST",
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

        // PollIntervalMilliseconds=50 keeps test wall-clock time low.
        // PollMaxAttempts=10 allows timeout test to finish quickly.
        var opts = new ViettelMysignOptions
        {
            BaseUrl      = _server.Urls[0],
            ClientId     = "test-client",
            ClientSecret = "test-secret",
            ProfileId    = "test-profile",
            LoginTokenCacheLeewaySeconds = 60,
            CertInfoCacheTtlSeconds      = 300,
            SyncSignTimeoutSeconds       = 30,
            HttpRetryMaxAttempts         = 3,
            PollIntervalMilliseconds     = 50,
            PollMaxAttempts              = 10,
            DefaultMode                  = MysignSignMode.Sync, // overridden per test via ExtraParameters
        };

        var optionsMonitor = Substitute.For<IOptionsMonitor<ViettelMysignOptions>>();
        optionsMonitor.CurrentValue.Returns(opts);

        var loginClient = new ViettelMysignLoginClient(
            new HttpClient(), optionsMonitor, _cache,
            NullLogger<ViettelMysignLoginClient>.Instance);

        var certInfoClient = new ViettelMysignCertInfoClient(
            new HttpClient(), optionsMonitor, _cache,
            NullLogger<ViettelMysignCertInfoClient>.Instance);

        var signHashClient = new ViettelMysignSignHashClient(
            new HttpClient(), optionsMonitor,
            NullLogger<ViettelMysignSignHashClient>.Instance);

        var statusPollClient = new ViettelMysignStatusPollClient(
            new HttpClient(), optionsMonitor,
            NullLogger<ViettelMysignStatusPollClient>.Instance);

        var chainValidator = Substitute.For<ICertificateChainValidator>();
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

    // ── Stub helpers ──────────────────────────────────────────────────────────

    private void StubLogin()
    {
        _server
            .Given(Request.Create().WithPath(LoginPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                // expires_in is a JSON number — matches the real Viettel UAT login response shape.
                .WithBody($@"{{""access_token"":""{FakeAccessToken}"",""token_type"":""Bearer"",""expires_in"":3600}}"));
    }

    private void StubCertInfo()
    {
        _server
            .Given(Request.Create().WithPath(CertInfoPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($@"[{{
                    ""credential_id"":""{FakeCredentialId}"",
                    ""cert"":{{
                        ""status"":""valid"",
                        ""certificates"":[""{FakeCertBase64}""],
                        ""issuerDN"":""CN=Test CA"",
                        ""SerialNumber"":""CC01"",
                        ""subjectDN"":""CN=Async Test User"",
                        ""validFrom"":""20240101000000Z"",
                        ""validTo"":""20260101000000Z""
                    }}
                }}]"));
    }

    /// <summary>Stubs signHash async=2 to return transactionId immediately.</summary>
    private void StubSignHashAsync2()
    {
        _server
            .Given(Request.Create().WithPath(SignHashPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($@"{{""transactionId"":""{FakeTxId}""}}"));
    }

    /// <summary>Stubs /requests/status with WireMock scenario state machine for happy path.</summary>
    // Counter for happy-path poll responses (4000 → 6000 → 1+signatures)
    private int _happyPollCount;

    private void StubStatusPollStateMachine_HappyPath()
    {
        _happyPollCount = 0;
        // Use callback to vary response by call count (WireMock.Net scenario state machine
        // behaves inconsistently across versions — counter-based approach is more reliable).
        _server
            .Given(Request.Create().WithPath(StatusPath).UsingPost())
            .RespondWith(Response.Create()
                .WithCallback(_ =>
                {
                    var attempt = System.Threading.Interlocked.Increment(ref _happyPollCount);
                    var body = attempt switch
                    {
                        1 => @"{""status"":""4000""}",
                        2 => @"{""status"":""6000""}",
                        _ => $@"{{""status"":""1"",""signatures"":[""{FakeSigBase64}""]}}",
                    };
                    return new WireMock.ResponseMessage
                    {
                        StatusCode = 200,
                        BodyData = new WireMock.Util.BodyData
                        {
                            BodyAsString = body,
                            DetectedBodyType = WireMock.Types.BodyType.String,
                        },
                    };
                }));
    }

    /// <summary>Stubs /requests/status to always return 4000 (never terminal).</summary>
    private void StubStatusPollAlways4000()
    {
        _server
            .Given(Request.Create().WithPath(StatusPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody(@"{""status"":""4000""}"));
    }

    // Counter for user-reject poll responses (4000 → 4000 → 4002)
    private int _rejectPollCount;

    /// <summary>Stubs /requests/status: two 4000 responses then 4002 (user reject).</summary>
    private void StubStatusPollUserReject()
    {
        _rejectPollCount = 0;
        _server
            .Given(Request.Create().WithPath(StatusPath).UsingPost())
            .RespondWith(Response.Create()
                .WithCallback(_ =>
                {
                    var attempt = System.Threading.Interlocked.Increment(ref _rejectPollCount);
                    var body = attempt < 3
                        ? @"{""status"":""4000""}"
                        : @"{""status"":""4002""}";
                    return new WireMock.ResponseMessage
                    {
                        StatusCode = 200,
                        BodyData = new WireMock.Util.BodyData
                        {
                            BodyAsString = body,
                            DetectedBodyType = WireMock.Types.BodyType.String,
                        },
                    };
                }));
    }

    private static SignRequest MakeRequest(MysignSignMode mode)
        => new SignRequest(
            Payload:         [1, 2, 3, 4, 5],
            UserId:          FakeUserId,
            Reason:          "async-test",
            ExtraParameters: new Dictionary<string, string>
            {
                ["mysignMode"] = mode.ToString(),
            });

    // ── Tests ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Test 1: InlinePoll happy path.
    /// WireMock state machine: poll 1 → 4000, poll 2 → 6000, poll 3 → 1+signatures.
    /// Asserts: IsSuccess=true, IsPending=false, Signature=FakeSigBytes.
    /// </summary>
    [Fact]
    public async Task SignAsync_InlinePoll_HappyPath_ReturnsSuccessAfterPolling()
    {
        _server.Reset();
        StubLogin();
        StubCertInfo();
        StubSignHashAsync2();
        StubStatusPollStateMachine_HappyPath();

        var result = await _provider.SignAsync(MakeRequest(MysignSignMode.AsyncPollInline));

        result.IsSuccess.Should().BeTrue("poll should succeed when status reaches '1'");
        result.IsPending.Should().BeFalse("terminal success must not leave IsPending=true");
        result.Signature.Should().Equal(FakeSigBytes, "signature bytes must match WireMock stub");
        result.FailureReason.Should().BeNull();
        result.TransactionId.Should().Be(FakeTxId);
    }

    /// <summary>
    /// Test 2: InlinePoll timeout.
    /// WireMock always returns 4000. PollMaxAttempts=10 exhausted → IsSuccess=false, "Polling timeout".
    /// </summary>
    [Fact]
    public async Task SignAsync_InlinePoll_ExhaustsMaxAttempts_ReturnsTimeoutFailure()
    {
        _server.Reset();
        StubLogin();
        StubCertInfo();
        StubSignHashAsync2();
        StubStatusPollAlways4000();

        var result = await _provider.SignAsync(MakeRequest(MysignSignMode.AsyncPollInline));

        result.IsSuccess.Should().BeFalse("exhausted polls must yield failure");
        result.Signature.Should().BeNull();
        result.FailureReason.Should().NotBeNullOrEmpty();
        result.FailureReason.Should().Contain("Polling timeout",
            "reason must communicate that max attempts were exhausted");
    }

    /// <summary>
    /// Test 3: InlinePoll user reject.
    /// WireMock: 4000, 4000, 4002 → IsSuccess=false, FailureReason contains "rejected".
    /// </summary>
    [Fact]
    public async Task SignAsync_InlinePoll_UserReject_ReturnsFailureWithReason()
    {
        _server.Reset();
        StubLogin();
        StubCertInfo();
        StubSignHashAsync2();
        StubStatusPollUserReject();

        var result = await _provider.SignAsync(MakeRequest(MysignSignMode.AsyncPollInline));

        result.IsSuccess.Should().BeFalse("user reject (4002) must yield failure");
        result.Signature.Should().BeNull();
        result.FailureReason.Should().NotBeNullOrEmpty();
        result.FailureReason.Should().Contain("rejected",
            "MysignStatusMapper maps '4002' to 'User rejected the signing request'");
    }

    /// <summary>
    /// Test 4: DeferredPoll — SignAsync returns IsPending=true immediately.
    /// Then ConfirmAsync resolves to success when WireMock returns status=1.
    /// </summary>
    [Fact]
    public async Task SignAsync_DeferredPoll_ReturnsPendingImmediately_ConfirmAsyncResolvesSuccess()
    {
        _server.Reset();
        StubLogin();
        StubCertInfo();
        StubSignHashAsync2();

        // Phase 1: DeferredPoll — provider must NOT call /requests/status here.
        var signResult = await _provider.SignAsync(MakeRequest(MysignSignMode.AsyncPollDeferred));

        signResult.IsSuccess.Should().BeTrue("deferred sign must be accepted (not a failure)");
        signResult.IsPending.Should().BeTrue("deferred mode must return pending=true immediately");
        signResult.TransactionId.Should().Be(FakeTxId, "transactionId from signHash must be propagated");
        signResult.Signature.Should().BeNull("no signature yet — still pending");

        // Phase 2: Confirm — stub /requests/status to return success.
        // Reset WireMock stubs to serve the confirm scenario cleanly.
        _server.ResetMappings();
        StubLogin(); // token will be cached but CertInfo cache may also be warm
        StubCertInfo();
        _server
            .Given(Request.Create().WithPath(StatusPath).UsingPost())
            .RespondWith(Response.Create()
                .WithStatusCode(200)
                .WithHeader("Content-Type", "application/json")
                .WithBody($@"{{""status"":""1"",""signatures"":[""{FakeSigBase64}""]}}"));

        var confirmResult = await _provider.ConfirmAsync(FakeTxId, FakeUserId, CancellationToken.None);

        confirmResult.IsSuccess.Should().BeTrue("confirm must resolve with success when status=1");
        confirmResult.IsPending.Should().BeFalse();
        confirmResult.Signature.Should().Equal(FakeSigBytes, "confirmed signature must match stub");
        confirmResult.FailureReason.Should().BeNull();
    }

    /// <summary>
    /// Test 5: Cancellation propagates through poll loop.
    /// WireMock always returns 4000. CancellationToken cancelled after 200ms.
    /// Asserts: OperationCanceledException or TaskCanceledException is thrown (NOT silently swallowed).
    /// </summary>
    [Fact]
    public async Task SignAsync_InlinePoll_CancelledToken_PropagatesException()
    {
        _server.Reset();
        StubLogin();
        StubCertInfo();
        StubSignHashAsync2();
        StubStatusPollAlways4000();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        // The poll loop awaits Task.Delay(..., ct) — cancellation should bubble up.
        var act = async () => await _provider.SignAsync(
            MakeRequest(MysignSignMode.AsyncPollInline), cts.Token);

        await act.Should()
            .ThrowAsync<OperationCanceledException>(
                because: "cancelled CancellationToken must propagate, not be swallowed as IsSuccess=false");
    }
}
