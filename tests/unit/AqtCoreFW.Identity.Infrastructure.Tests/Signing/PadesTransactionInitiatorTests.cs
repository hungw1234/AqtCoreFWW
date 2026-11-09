using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using AqtCoreFW.Identity.Infrastructure.Tests.Signing.Fixtures;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Behaviour of <see cref="PadesTransactionInitiator"/> — the async-poll PAdES initiation seam. Pins:
/// <list type="bullet">
///   <item><b>Death-trap #1 at the REAL Mysign wire</b>: the bytes <c>signHash</c> receives are
///     <c>SHA-256(DER(signedAttributes))</c> — proven against the resume state actually persisted, never the
///     document digest.</item>
///   <item><b>S2 binding fail-closed</b>: an unbound cert is rejected BEFORE any OTP is prompted (signHash never
///     called, no transaction persisted).</item>
///   <item><b>async=2 + F1</b>: the request returns fast with a pollable transaction carrying the full resume
///     state; the placeholder is staged to storage.</item>
/// </list>
/// </summary>
public sealed class PadesTransactionInitiatorTests
{
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
    private const string RsaOid = "1.2.840.113549.1.1.1";

    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    // ── HTTP wire stub (routes by path, captures the signHash body, optional per-endpoint status) ─────────────
    private sealed class MysignRoutingHandler(
        string loginJson, string certInfoJson, string signHashJson,
        HttpStatusCode loginStatus = HttpStatusCode.OK,
        HttpStatusCode certInfoStatus = HttpStatusCode.OK,
        HttpStatusCode signHashStatus = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public string? CapturedSignHashBody { get; private set; }
        public bool SignHashCalled => CapturedSignHashBody is not null;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            string json;
            var status = HttpStatusCode.OK;
            if (path.Contains("login", StringComparison.Ordinal)) { json = loginJson; status = loginStatus; }
            else if (path.Contains("certificates/info", StringComparison.Ordinal)) { json = certInfoJson; status = certInfoStatus; }
            else if (path.Contains("signHash", StringComparison.Ordinal))
            {
                CapturedSignHashBody = await request.Content!.ReadAsStringAsync(ct);
                json = signHashJson;
                status = signHashStatus;
            }
            else json = "{}";
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static IOptionsMonitor<ViettelMysignOptions> StubOptions()
    {
        var monitor = Substitute.For<IOptionsMonitor<ViettelMysignOptions>>();
        monitor.CurrentValue.Returns(new ViettelMysignOptions
        {
            BaseUrl = "https://mysign.test.local",
            ClientId = "cid",
            ClientSecret = "secret",
            ProfileId = "pid",
            PendingTxTtlHours = 24,
        });
        return monitor;
    }

    private static (ViettelMysignLoginClient, ViettelMysignCertInfoClient, ViettelMysignSignHashClient)
        BuildClients(MysignRoutingHandler handler, IOptionsMonitor<ViettelMysignOptions> opts)
    {
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 50 });
        var login = new ViettelMysignLoginClient(new HttpClient(handler), opts, cache, NullLogger<ViettelMysignLoginClient>.Instance);
        var certInfo = new ViettelMysignCertInfoClient(new HttpClient(handler), opts, cache, NullLogger<ViettelMysignCertInfoClient>.Instance);
        var signHash = new ViettelMysignSignHashClient(new HttpClient(handler), opts, NullLogger<ViettelMysignSignHashClient>.Instance);
        return (login, certInfo, signHash);
    }

    private static string CertInfoJson(byte[] certDer) =>
        "[{\"credential_id\":\"cred-1\",\"cert\":{\"status\":\"valid\",\"certificates\":[\""
        + Convert.ToBase64String(certDer)
        + "\"],\"validFrom\":\"20250101000000Z\",\"validTo\":\"20300101000000Z\","
        + "\"SerialNumber\":\"1\",\"subjectDN\":\"CN=Test\",\"issuerDN\":\"CN=Test\"}}]";

    private static IdentityDbContext NewDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static UserManager<ApplicationUser> UserManagerReturning(ApplicationUser? user)
    {
        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var mgr = Substitute.For<UserManager<ApplicationUser>>(store, null, null, null, null, null, null, null, null);
        mgr.FindByIdAsync(Arg.Any<string>()).Returns(user);
        return mgr;
    }

    private sealed record Harness(
        PadesTransactionInitiator Initiator,
        MysignRoutingHandler Handler,
        IdentityDbContext Db,
        List<(byte[] Bytes, string Key)> PlaceholderWrites);

    /// <summary>Builds the initiator wired to stubs; defaults: RSA cert, bound signer, signHash async returns a txnId.</summary>
    private static Harness BuildHarness(
        Guid tenantId,
        Guid signerId,
        byte[]? certDer = null,
        SignerBindingVerdict? verdict = null,
        string signHashJson = """{"transactionId":"txn-123"}""",
        ApplicationUser? user = null,
        SignatureFlowMode flowMode = SignatureFlowMode.Async,
        HttpStatusCode loginStatus = HttpStatusCode.OK,
        HttpStatusCode certInfoStatus = HttpStatusCode.OK,
        HttpStatusCode signHashStatus = HttpStatusCode.OK,
        UserSignatureProfile? profile = null,
        bool profileResolves = true)
    {
        certDer ??= new LocalRsaHashSigner().SignerChain[0].GetEncoded();
        user ??= new ApplicationUser { Id = signerId, TenantId = tenantId, SigningNationalId = "017091004878" };

        var handler = new MysignRoutingHandler(
            loginJson: """{"access_token":"tok","expires_in":3600}""",
            certInfoJson: CertInfoJson(certDer),
            signHashJson: signHashJson,
            loginStatus: loginStatus,
            certInfoStatus: certInfoStatus,
            signHashStatus: signHashStatus);
        var opts = StubOptions();
        var (login, certInfo, signHash) = BuildClients(handler, opts);

        var reader = Substitute.For<ISigningOriginalPdfReader>();
        reader.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(MinimalPdfFactory.CreateSinglePage());

        var writes = new List<(byte[], string)>();
        var placeholderWriter = Substitute.For<ISigningPlaceholderPdfWriter>();
        placeholderWriter.WriteAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                writes.Add((ci.ArgAt<byte[]>(0), ci.ArgAt<string>(1)));
                return ci.ArgAt<string>(1);
            });

        var guard = Substitute.For<ISignerBindingGuard>();
        guard.VerifyAsync(Arg.Any<CertificateInfo>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(verdict ?? SignerBindingVerdict.Bound("test"));

        var guid = Substitute.For<IGuidGenerator>();
        guid.NewIdMonotonic().Returns(_ => Guid.NewGuid());

        // Per-tenant flow config drives the sync/async branch. Existing tests default to Async.
        var configRepo = Substitute.For<ITenantSignatureConfigRepository>();
        configRepo.GetByTenantIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(TenantSignatureConfig.Create(
                tenantId, "remote-viettel-mysign", null, string.Empty, OcspMode.Soft, true, flowMode, Guid.NewGuid()));

        // Visible-stamp deps. profileRepo returns the supplied profile (or null when profileResolves=false, to
        // exercise the M4 fail-closed path). The rasterizer is stubbed to a synthetic baseline JPEG so the wire
        // test does not depend on the QuestPDF native renderer (that is exercised in the integration project);
        // the composer is the REAL pure-logic one.
        var profileRepo = Substitute.For<IUserSignatureProfileRepository>();
        UserSignatureProfile? resolved = profileResolves ? profile : null;
        profileRepo.GetByPublicIdAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(resolved);
        profileRepo.GetDefaultAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(resolved);

        var imageStore = Substitute.For<IUserSignatureImageStore>();
        imageStore.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(JpegFixtures.BaselineRgb(200, 100));

        var composer = new SignatureStampComposer();
        var rasterizer = Substitute.For<ISignatureStampRasterizer>();
        rasterizer.Rasterize(Arg.Any<ComposedStamp>(), Arg.Any<float>(), Arg.Any<float>())
            .Returns(JpegFixtures.BaselineRgb(200, 100));

        var db = NewDb();
        var initiator = new PadesTransactionInitiator(
            reader, placeholderWriter, guard, login, certInfo, signHash,
            UserManagerReturning(user), configRepo, db, guid, opts,
            profileRepo, imageStore, composer, rasterizer, NullLogger<PadesTransactionInitiator>.Instance);

        return new Harness(initiator, handler, db, writes);
    }

    private static SigningExecutionContext Context(
        Guid tenantId, Guid signerId, int seq = 1, SignatureFlowMode? flowOverride = null,
        string? placement = null) =>
        new(
            new RequestSignatureInput(
                TenantId: tenantId,
                ArtifactKind: SigningArtifactKind.UploadedPdf,
                ArtifactRef: Guid.NewGuid().ToString(),
                ProviderKey: "remote-viettel-mysign",
                AssignedSignerId: signerId,
                InitiatedBy: signerId,
                Placement: placement,
                SignerSequenceNumber: seq,
                Pades: new PadesSignParameters("tenant/original.pdf", "doc", flowOverride)),
            Substitute.For<IDigitalSignatureProvider>());

    /// <summary>A profile owned by the signer with an uploaded full-signature image slot.</summary>
    private static UserSignatureProfile ProfileWithImage(Guid tenantId, Guid signerId)
    {
        var p = UserSignatureProfile.Create(Gen, tenantId, signerId, "Nguyễn Văn Đức", signerId);
        p.SetImages("tenant/user/full.png", null, signerId);
        return p;
    }

    private static string PlacementJson(Guid? profilePublicId = null) =>
        profilePublicId is { } pid
            ? $$"""{"page":1,"x":72,"y":72,"w":200,"h":100,"profilePublicId":"{{pid}}","visualType":"full"}"""
            : """{"page":1,"x":72,"y":72,"w":200,"h":100,"visualType":"full"}""";

    // ── Death-trap #1 + happy path ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Initiate_SubmitsSignedAttributesHashToMysign_NotDocumentDigest()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        var h = BuildHarness(tenantId, signerId);

        var result = await h.Initiator.InitiateAsync(Context(tenantId, signerId));

        result.IsSuccess.Should().BeTrue(result.IsFailed ? result.Errors[0].Message : "");

        // The persisted resume state is the source of truth for what was signed.
        var txn = await h.Db.SigningPendingTransactions.SingleAsync();
        txn.PadesSignedAttributesDer.Should().NotBeNullOrEmpty();

        using var doc = JsonDocument.Parse(h.Handler.CapturedSignHashBody!);
        var root = doc.RootElement;
        byte[] sentHash = Convert.FromBase64String(root.GetProperty("hash")[0].GetString()!);

        // Death-trap #1: the wire hash is SHA-256 of the EXACT signedAttributes DER we persisted — never the
        // document digest (which a "simplification" regression would send directly).
        sentHash.Should().Equal(SHA256.HashData(txn.PadesSignedAttributesDer!),
            because: "Mysign must sign SHA-256(DER(signedAttributes)), proven against the persisted resume state");
        root.GetProperty("async").GetInt32().Should().Be(2, because: "the request must NOT block on the mobile OTP");
        root.GetProperty("hashAlgo").GetString().Should().Be(Sha256Oid);
        root.GetProperty("signAlgo").GetString().Should().Be(RsaOid);
    }

    [Fact]
    public async Task Initiate_PersistsPendingTransaction_WithResumeStateAndPollableHandoff()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        byte[] certDer = new LocalRsaHashSigner().SignerChain[0].GetEncoded();
        var h = BuildHarness(tenantId, signerId, certDer: certDer);

        var result = await h.Initiator.InitiateAsync(Context(tenantId, signerId));

        result.IsSuccess.Should().BeTrue();
        result.Value.UnderlyingKind.Should().Be(SigningUnderlyingKind.PendingTransaction);
        result.Value.UnderlyingRecordId.Should().Be("txn-123");
        result.Value.ClientHandoff["pollable"].Should().Be("true");
        result.Value.ClientHandoff["transactionId"].Should().Be("txn-123");

        var txn = await h.Db.SigningPendingTransactions.SingleAsync();
        txn.TransactionId.Should().Be("txn-123");
        txn.TenantId.Should().Be(tenantId);
        txn.Status.Should().Be(PendingTransactionStatus.Initiated);
        txn.PadesPlaceholderStorageKey.Should().StartWith($"{tenantId}/").And.EndWith(".placeholder.pdf");
        txn.PadesContentsGapStart.Should().BeGreaterThan(0);
        txn.PadesContentsHexLength.Should().Be(32 * 1024);
        txn.PadesSignerCertDer.Should().Equal(certDer, because: "completion embeds this exact signer leaf in the CMS bag");
        txn.PadesSignedStorageKey.Should().BeNull(because: "the signed PDF is produced only by the completion path");

        // The placeholder PDF was staged under the key the resume state references.
        h.PlaceholderWrites.Should().ContainSingle();
        h.PlaceholderWrites[0].Key.Should().Be(txn.PadesPlaceholderStorageKey);
        Encoding.Latin1.GetString(h.PlaceholderWrites[0].Bytes).Should().StartWith("%PDF-");
    }

    // ── S2 binding fail-closed ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Initiate_UnboundCert_FailsClosed_BeforeAnyOtp()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        var h = BuildHarness(tenantId, signerId, verdict: SignerBindingVerdict.NotBound("not enrolled"));

        var result = await h.Initiator.InitiateAsync(Context(tenantId, signerId));

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<ForbiddenError>();
        h.Handler.SignHashCalled.Should().BeFalse(because: "an unbound cert must never trigger a mobile OTP (S2)");
        (await h.Db.SigningPendingTransactions.AnyAsync()).Should().BeFalse();
    }

    // ── ECDSA fail-loud (VIETTEL-SIGN-ALG-ECHO) ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Initiate_NonRsaCert_FailsLoud_BeforeAnyOtp()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        var h = BuildHarness(tenantId, signerId, certDer: GenerateSelfSignedEcCertDer());

        var result = await h.Initiator.InitiateAsync(Context(tenantId, signerId));

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<BadRequestError>();
        h.Handler.SignHashCalled.Should().BeFalse(
            because: "a non-RSA credential must fail before signing with SHA256+RSA params");
    }

    // ── Guard rails: params, multi-signer, identity ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Initiate_NoPadesParams_ReturnsBadRequest()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        var h = BuildHarness(tenantId, signerId);
        var ctx = new SigningExecutionContext(
            Context(tenantId, signerId).Input with { Pades = null },
            Substitute.For<IDigitalSignatureProvider>());

        var result = await h.Initiator.InitiateAsync(ctx);

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<BadRequestError>();
        h.Handler.SignHashCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Initiate_MultiSignerSequence_NotYetSupported_FailsClosed()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        var h = BuildHarness(tenantId, signerId);

        var result = await h.Initiator.InitiateAsync(Context(tenantId, signerId, seq: 2));

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<BadRequestError>();
        h.Handler.SignHashCalled.Should().BeFalse(because: "signer N's base PDF (signer N-1's output) is not wired yet");
    }

    [Fact]
    public async Task Initiate_SignerInDifferentTenant_FailsClosed()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        // The resolved user belongs to a DIFFERENT tenant — must not log in to Mysign with a cross-tenant CCCD.
        var crossTenantUser = new ApplicationUser { Id = signerId, TenantId = Guid.NewGuid(), SigningNationalId = "017091004878" };
        var h = BuildHarness(tenantId, signerId, user: crossTenantUser);

        var result = await h.Initiator.InitiateAsync(Context(tenantId, signerId));

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<ForbiddenError>();
        h.Handler.SignHashCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Initiate_SignHashReturnsNoTransactionId_FailsClosed()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        var h = BuildHarness(tenantId, signerId, signHashJson: """{"transactionId":""}""");

        var result = await h.Initiator.InitiateAsync(Context(tenantId, signerId));

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<BadRequestError>();
        (await h.Db.SigningPendingTransactions.AnyAsync()).Should().BeFalse(
            because: "no transactionId means nothing to poll — do not persist a keyless transaction");
    }

    // ── HIGH-1: Mysign third-party failures map to typed Results, never an opaque 500 ────────────────────────

    [Fact]
    public async Task Initiate_SignHashHttpError_MapsToServiceUnavailable_NoPersist()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        // signHash returns HTTP 500 → the typed client throws MysignSignHashException (an upstream outage).
        var h = BuildHarness(tenantId, signerId, signHashStatus: HttpStatusCode.InternalServerError);

        var result = await h.Initiator.InitiateAsync(Context(tenantId, signerId));

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<ServiceUnavailableError>(
            because: "a Mysign outage is a retryable upstream 503, not the caller's 400 nor an opaque 500");
        (await h.Db.SigningPendingTransactions.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Initiate_LoginHttpError_MapsToServiceUnavailable_BeforeSignHash()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        var h = BuildHarness(tenantId, signerId, loginStatus: HttpStatusCode.BadGateway);

        var result = await h.Initiator.InitiateAsync(Context(tenantId, signerId));

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<ServiceUnavailableError>();
        h.Handler.SignHashCalled.Should().BeFalse();
        (await h.Db.SigningPendingTransactions.AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Initiate_CertInfoUnavailable_MapsToBadRequest_NoOtp()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        // CertInfo returns HTTP 404 → MysignCertInfoException (a signer-account problem, caller-correctable → 400).
        var h = BuildHarness(tenantId, signerId, certInfoStatus: HttpStatusCode.NotFound);

        var result = await h.Initiator.InitiateAsync(Context(tenantId, signerId));

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<BadRequestError>();
        h.Handler.SignHashCalled.Should().BeFalse();
        (await h.Db.SigningPendingTransactions.AnyAsync()).Should().BeFalse();
    }

    // ── Sync flow (opt-in per tenant): block on signHash async=0, finalize in-request ───────────────────────────

    [Fact]
    public async Task Initiate_SyncMode_SubmitsAsync0_CompletesInRequest_StoresSignedPdf()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        // async=0 returns the raw signature in-band (a dummy sig is fine — the CMS just embeds it; correctness of
        // the splice is pinned by PadesPdfAssembler's shared use in the completion flagship test).
        string sigB64 = Convert.ToBase64String(new byte[256]);
        var h = BuildHarness(
            tenantId, signerId,
            flowMode: SignatureFlowMode.Sync,
            signHashJson: $$"""{"signatures":["{{sigB64}}"]}""");

        var result = await h.Initiator.InitiateAsync(Context(tenantId, signerId));

        result.IsSuccess.Should().BeTrue(result.IsFailed ? result.Errors[0].Message : "");
        result.Value.UnderlyingKind.Should().Be(SigningUnderlyingKind.PendingTransaction);
        result.Value.ClientHandoff["pollable"].Should().Be("false", because: "sync finalizes in-request — nothing to poll");

        // The request blocked on the OTP (async=0), not the non-blocking poll mode.
        using var doc = JsonDocument.Parse(h.Handler.CapturedSignHashBody!);
        doc.RootElement.GetProperty("async").GetInt32().Should().Be(0, because: "sync mode must use signHash async=0");

        // The transaction is persisted ALREADY Completed with the signed-PDF key (same shape the async job produces).
        var txn = await h.Db.SigningPendingTransactions.SingleAsync();
        txn.Status.Should().Be(PendingTransactionStatus.Completed);
        txn.PadesSignedStorageKey.Should().NotBeNullOrEmpty().And.EndWith(".signed.pdf");
        txn.PadesPlaceholderStorageKey.Should().BeNull(because: "sync splices in-memory — no placeholder blob is staged");

        // Exactly one storage write: the final signed PDF (no placeholder). It is a real PDF.
        h.PlaceholderWrites.Should().ContainSingle();
        h.PlaceholderWrites[0].Key.Should().Be(txn.PadesSignedStorageKey);
        Encoding.Latin1.GetString(h.PlaceholderWrites[0].Bytes).Should().StartWith("%PDF-");
    }

    [Fact]
    public async Task Initiate_SyncMode_SignHashReturnsNoSignature_FailsClosed_NoPersist()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        var h = BuildHarness(
            tenantId, signerId,
            flowMode: SignatureFlowMode.Sync,
            signHashJson: """{"signatures":[]}""");

        var result = await h.Initiator.InitiateAsync(Context(tenantId, signerId));

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<BadRequestError>();
        (await h.Db.SigningPendingTransactions.AnyAsync()).Should().BeFalse(
            because: "no signature means no signed PDF — do not persist a phantom completed transaction");
        h.PlaceholderWrites.Should().BeEmpty();
    }

    // ── Per-request flow override (beats the tenant config in both directions) ──────────────────────────────────

    [Fact]
    public async Task Initiate_OverrideSync_BeatsTenantAsync_BlocksWithAsync0()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        // Tenant config = Async, but the request overrides to Sync → the in-request OTP block (async=0) must win.
        string sigB64 = Convert.ToBase64String(new byte[256]);
        var h = BuildHarness(
            tenantId, signerId,
            flowMode: SignatureFlowMode.Async,
            signHashJson: $$"""{"signatures":["{{sigB64}}"]}""");

        var result = await h.Initiator.InitiateAsync(
            Context(tenantId, signerId, flowOverride: SignatureFlowMode.Sync));

        result.IsSuccess.Should().BeTrue(result.IsFailed ? result.Errors[0].Message : "");
        result.Value.ClientHandoff["pollable"].Should().Be("false");
        using var doc = JsonDocument.Parse(h.Handler.CapturedSignHashBody!);
        doc.RootElement.GetProperty("async").GetInt32().Should().Be(0,
            because: "a per-request Sync override must win over the tenant's Async config");
        (await h.Db.SigningPendingTransactions.SingleAsync()).Status.Should().Be(PendingTransactionStatus.Completed);
    }

    [Fact]
    public async Task Initiate_OverrideAsync_BeatsTenantSync_UsesAsync2Poll()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        // Tenant config = Sync, but the request overrides to Async → the non-blocking poll path (async=2) must win.
        var h = BuildHarness(tenantId, signerId, flowMode: SignatureFlowMode.Sync);

        var result = await h.Initiator.InitiateAsync(
            Context(tenantId, signerId, flowOverride: SignatureFlowMode.Async));

        result.IsSuccess.Should().BeTrue(result.IsFailed ? result.Errors[0].Message : "");
        result.Value.ClientHandoff["pollable"].Should().Be("true");
        using var doc = JsonDocument.Parse(h.Handler.CapturedSignHashBody!);
        doc.RootElement.GetProperty("async").GetInt32().Should().Be(2,
            because: "a per-request Async override must win over the tenant's Sync config");
        (await h.Db.SigningPendingTransactions.SingleAsync()).Status.Should().Be(PendingTransactionStatus.Initiated);
    }

    // ── Slice 2: visible-stamp wire ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Initiate_WithPlacementAndOwnedProfile_StagesVisibleStampPlaceholder()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        var profile = ProfileWithImage(tenantId, signerId);
        var h = BuildHarness(tenantId, signerId, profile: profile);

        var result = await h.Initiator.InitiateAsync(
            Context(tenantId, signerId, placement: PlacementJson(Guid.NewGuid())));

        result.IsSuccess.Should().BeTrue(result.IsFailed ? result.Errors[0].Message : "");

        // The staged placeholder carries the visible widget: an image XObject (DCTDecode), the /AP appearance
        // reference, and a real /Rect — proving the appearance flowed through compose→raster→Writer.Prepare.
        string staged = Encoding.Latin1.GetString(h.PlaceholderWrites.Single().Bytes);
        staged.Should().Contain("/Subtype /Image").And.Contain("/Filter /DCTDecode");
        staged.Should().Contain("/AP << /N");
        staged.Should().Contain("/Rect [72 72 272 172]");
        staged.Should().NotContain("/Rect [0 0 0 0]", "a placement was supplied — the stamp must be visible");
    }

    [Fact]
    public async Task Initiate_NoPlacement_StagesInvisibleSignature_BackwardCompat()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        // No placement supplied (and no profile) — the legacy invisible path must be byte-shape unchanged.
        var h = BuildHarness(tenantId, signerId);

        var result = await h.Initiator.InitiateAsync(Context(tenantId, signerId));

        result.IsSuccess.Should().BeTrue(result.IsFailed ? result.Errors[0].Message : "");
        string staged = Encoding.Latin1.GetString(h.PlaceholderWrites.Single().Bytes);
        staged.Should().Contain("/Rect [0 0 0 0]", "no placement → invisible widget");
        staged.Should().NotContain("/AP", "the invisible path emits no appearance stream");
        staged.Should().NotContain("/DCTDecode");
    }

    [Fact]
    public async Task Initiate_PlacementButProfileNotOwned_FailsClosed_BeforeAnyOtp()
    {
        var tenantId = Guid.NewGuid();
        var signerId = Guid.NewGuid();
        // The placement names a profile, but the (tenant, signer)-scoped lookup resolves nothing (foreign or
        // missing) — M4 fail-closed: 403, no OTP, no transaction.
        var h = BuildHarness(tenantId, signerId, profileResolves: false);

        var result = await h.Initiator.InitiateAsync(
            Context(tenantId, signerId, placement: PlacementJson(Guid.NewGuid())));

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<ForbiddenError>();
        h.Handler.SignHashCalled.Should().BeFalse(because: "a foreign/missing profile must never trigger a mobile OTP");
        (await h.Db.SigningPendingTransactions.AnyAsync()).Should().BeFalse();
    }

    private static byte[] GenerateSelfSignedEcCertDer()
    {
        var rng = new SecureRandom();
        var gen = new ECKeyPairGenerator();
        gen.Init(new ECKeyGenerationParameters(SecObjectIdentifiers.SecP256r1, rng));
        AsymmetricCipherKeyPair pair = gen.GenerateKeyPair();

        var certGen = new X509V3CertificateGenerator();
        certGen.SetSerialNumber(BigInteger.ProbablePrime(64, rng));
        var name = new X509Name("CN=EC Test Signer, C=VN");
        certGen.SetSubjectDN(name);
        certGen.SetIssuerDN(name);
        certGen.SetNotBefore(DateTime.UtcNow.AddHours(-1));
        certGen.SetNotAfter(DateTime.UtcNow.AddYears(1));
        certGen.SetPublicKey(pair.Public);
        return certGen.Generate(new Asn1SignatureFactory("SHA256withECDSA", pair.Private, rng)).GetEncoded();
    }
}
