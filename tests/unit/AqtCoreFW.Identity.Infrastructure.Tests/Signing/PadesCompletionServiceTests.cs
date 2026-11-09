using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Repositories;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.Pades;
using AqtCoreFW.Identity.Infrastructure.Services.Signing.ViettelMysign;
using AqtCoreFW.Identity.Infrastructure.Tests.Signing.Fixtures;
using AqtCoreFW.SharedKernel.Abstractions;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Cms;
using Xunit;
using BcX509 = Org.BouncyCastle.X509;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Behaviour of <see cref="PadesCompletionService"/> — the async-poll completion path. The flagship test proves the
/// completion RECONSTRUCTS a cryptographically valid PAdES from the PERSISTED resume state alone (signedAttributes
/// DER + signer cert DER + polled raw signature): the spliced PDF's embedded CMS verifies over its ByteRange — the
/// strongest defence against a reconstruction bug. Plus terminal-failure, still-pending, idempotency, and the
/// success-without-signature anomaly.
/// </summary>
public sealed class PadesCompletionServiceTests
{
    // V7GuidGenerator is internal — hand-roll a real (non-mock) fake so factory calls nested inside
    // other substitutes' .Returns(...) arguments never corrupt NSubstitute's per-thread "last call" state.
    private sealed class TestGuidGenerator : IGuidGenerator
    {
        public Guid NewIdMonotonic() => Guid.CreateVersion7();
        public Guid NewIdRandom() => Guid.NewGuid();
    }
    private static readonly IGuidGenerator Gen = new TestGuidGenerator();

    // ── HTTP wire stub (login + status-poll), tracks whether the status endpoint was hit ─────────────────────
    private sealed class PollRoutingHandler(string loginJson, string statusJson, HttpStatusCode statusCode = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public bool StatusPolled { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            string json;
            var code = HttpStatusCode.OK;
            if (path.Contains("login", StringComparison.Ordinal)) json = loginJson;
            else if (path.Contains("requests/status", StringComparison.Ordinal)) { StatusPolled = true; json = statusJson; code = statusCode; }
            else json = "{}";
            return Task.FromResult(new HttpResponseMessage(code)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static IOptionsMonitor<ViettelMysignOptions> StubOptions()
    {
        var monitor = Substitute.For<IOptionsMonitor<ViettelMysignOptions>>();
        monitor.CurrentValue.Returns(new ViettelMysignOptions
        {
            BaseUrl = "https://mysign.test.local", ClientId = "cid", ClientSecret = "secret", ProfileId = "pid",
        });
        return monitor;
    }

    private static (ViettelMysignLoginClient, ViettelMysignStatusPollClient) BuildClients(PollRoutingHandler handler)
    {
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 50 });
        var opts = StubOptions();
        var login = new ViettelMysignLoginClient(new HttpClient(handler), opts, cache, NullLogger<ViettelMysignLoginClient>.Instance);
        var status = new ViettelMysignStatusPollClient(new HttpClient(handler), opts, NullLogger<ViettelMysignStatusPollClient>.Instance);
        return (login, status);
    }

    private static UserManager<ApplicationUser> UserManagerReturning(ApplicationUser? user)
    {
        var store = Substitute.For<IUserStore<ApplicationUser>>();
        var mgr = Substitute.For<UserManager<ApplicationUser>>(store, null, null, null, null, null, null, null, null);
        mgr.FindByIdAsync(Arg.Any<string>()).Returns(user);
        return mgr;
    }

    /// <summary>Produces a REAL Initiated PAdES transaction + the matching raw RSA signature, exactly as the
    /// initiation path would have persisted it (placeholder bytes + signedAttrs DER + signer cert DER).</summary>
    private static (SigningPendingTransaction Txn, byte[] PlaceholderPdf, byte[] RawSignature) BuildInitiatedPades(
        Guid tenantId, Guid userId)
    {
        var writer = new PdfIncrementalSignatureWriter();
        var cmsBuilder = new PadesCmsBuilder();
        IRawHashSigner localSigner = new LocalRsaHashSigner();

        byte[] input = MinimalPdfFactory.CreateSinglePage("AqtCoreFW PAdES completion");
        var prep = writer.Prepare(input, contentsHexLength: 32 * 1024);
        var attrs = cmsBuilder.BuildSignedAttributes(prep.DocumentDigest, localSigner.SignerChain[0], DateTime.UtcNow);
        byte[] rawSig = localSigner.Sign(attrs.SignInput);

        var txn = SigningPendingTransaction.Initiate(
            transactionId: "txn-cmp", tenantId: tenantId, userId: userId,
            requestId: "req", documentId: "doc", ttl: TimeSpan.FromHours(24));
        txn.AttachPadesResumeState(
            $"{tenantId}/abc.placeholder.pdf", prep.ContentsGapStart, prep.ContentsHexLength,
            attrs.Der, localSigner.SignerChain[0].GetEncoded());

        return (txn, prep.PreparedPdf, rawSig);
    }

    private sealed record Harness(
        PadesCompletionService Service, PollRoutingHandler Handler, List<(byte[] Bytes, string Key)> Writes);

    // Default enrollment repo = a bootstrap signer (zero enrollment rows) so the C5 completion re-check is a
    // no-op and the existing completion tests are unaffected. Pass a custom repo to exercise the re-check.
    private static ISignerCertEnrollmentRepository BootstrapEnrollments()
    {
        var repo = Substitute.For<ISignerCertEnrollmentRepository>();
        repo.HasAnyEnrollmentAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(false);
        return repo;
    }

    private static Harness BuildHarness(
        byte[] placeholderPdf, string statusJson, ApplicationUser user,
        HttpStatusCode statusCode = HttpStatusCode.OK,
        ISignerCertEnrollmentRepository? enrollments = null)
    {
        var handler = new PollRoutingHandler("""{"access_token":"tok","expires_in":3600}""", statusJson, statusCode);
        var (login, status) = BuildClients(handler);

        var reader = Substitute.For<ISigningPlaceholderPdfReader>();
        reader.ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(placeholderPdf);

        var writes = new List<(byte[], string)>();
        var writer = Substitute.For<ISigningPlaceholderPdfWriter>();
        writer.WriteAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(ci => { writes.Add((ci.ArgAt<byte[]>(0), ci.ArgAt<string>(1))); return ci.ArgAt<string>(1); });

        var service = new PadesCompletionService(
            reader, writer, login, status, UserManagerReturning(user), enrollments ?? BootstrapEnrollments(),
            NullLogger<PadesCompletionService>.Instance);
        return new Harness(service, handler, writes);
    }

    // ── Flagship: terminal success reconstructs a VERIFIABLE PAdES from persisted state ──────────────────────

    [Fact]
    public async Task CompleteAsync_TerminalSuccess_SplicesStoresAndCompletes_ProducingVerifiablePades()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (txn, placeholderPdf, rawSig) = BuildInitiatedPades(tenantId, userId);
        var user = new ApplicationUser { Id = userId, TenantId = tenantId, SigningNationalId = "017091004878" };

        string statusJson = $$"""{"status":"1","signatures":["{{Convert.ToBase64String(rawSig)}}"]}""";
        var h = BuildHarness(placeholderPdf, statusJson, user);

        await h.Service.CompleteAsync(txn);

        txn.Status.Should().Be(PendingTransactionStatus.Completed);
        // Deterministic key: a pure function of (tenant, transactionId) — see the HA-idempotency rationale below.
        txn.PadesSignedStorageKey.Should().Be($"{tenantId}/{txn.TransactionId}.signed.pdf");
        txn.Signature.Should().Equal(rawSig);
        h.Writes.Should().ContainSingle();
        h.Writes[0].Key.Should().Be(txn.PadesSignedStorageKey);

        // The stored signed PDF's embedded CMS must verify over its ByteRange — the completion reconstructed a
        // valid PAdES from the persisted DER alone (no in-memory PadesSignedAttributes object survived).
        byte[] signedPdf = h.Writes[0].Bytes;
        var (byteRange, cms) = ReadSignature(signedPdf);
        byte[] covered = ConcatRanges(signedPdf, byteRange);
        var signedData = new CmsSignedData(new CmsProcessableByteArray(covered), cms);
        var certStore = signedData.GetCertificates();
        foreach (SignerInformation si in signedData.GetSignerInfos().GetSigners())
        {
            BcX509.X509Certificate cert = certStore.EnumerateMatches(si.SignerID).First();
            si.Verify(cert).Should().BeTrue(
                because: "completion must reconstruct a CMS that verifies over the document ByteRange");
        }
    }

    [Fact]
    public async Task CompleteAsync_UserRejected_MarksFailed_NoStore()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (txn, placeholderPdf, _) = BuildInitiatedPades(tenantId, userId);
        var user = new ApplicationUser { Id = userId, TenantId = tenantId, SigningNationalId = "017091004878" };
        var h = BuildHarness(placeholderPdf, """{"status":"4002"}""", user); // 4002 = user rejected

        await h.Service.CompleteAsync(txn);

        txn.Status.Should().Be(PendingTransactionStatus.Failed);
        txn.FailureReason.Should().NotBeNullOrEmpty();
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteAsync_StillPending_NoOp_StaysInitiated()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (txn, placeholderPdf, _) = BuildInitiatedPades(tenantId, userId);
        var user = new ApplicationUser { Id = userId, TenantId = tenantId, SigningNationalId = "017091004878" };
        var h = BuildHarness(placeholderPdf, """{"status":"4000"}""", user); // 4000 = waiting for OTP

        await h.Service.CompleteAsync(txn);

        txn.Status.Should().Be(PendingTransactionStatus.Initiated);
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteAsync_AlreadyCompleted_IsIdempotentNoOp_DoesNotPoll()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (txn, placeholderPdf, rawSig) = BuildInitiatedPades(tenantId, userId);
        txn.CompletePades(rawSig, $"{tenantId}/already.signed.pdf"); // already terminal
        var user = new ApplicationUser { Id = userId, TenantId = tenantId, SigningNationalId = "017091004878" };
        var h = BuildHarness(placeholderPdf, """{"status":"1","signatures":["AAAA"]}""", user);

        await h.Service.CompleteAsync(txn);

        txn.Status.Should().Be(PendingTransactionStatus.Completed);
        h.Handler.StatusPolled.Should().BeFalse(because: "a terminal row must not be re-polled or re-signed");
        h.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task CompleteAsync_SuccessButNoSignature_MarksFailed()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (txn, placeholderPdf, _) = BuildInitiatedPades(tenantId, userId);
        var user = new ApplicationUser { Id = userId, TenantId = tenantId, SigningNationalId = "017091004878" };
        var h = BuildHarness(placeholderPdf, """{"status":"1","signatures":[]}""", user);

        await h.Service.CompleteAsync(txn);

        txn.Status.Should().Be(PendingTransactionStatus.Failed, because: "success status with no signature is a provider anomaly");
        h.Writes.Should().BeEmpty();
    }

    // ── C5: completion-time approval re-check ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_EnrollmentRevokedAfterInitiate_FailsTerminally_NoStore()
    {
        // The signer was Approved at initiate, but an admin rejected/revoked the enrollment before this async
        // completion ran. Even though Mysign returns a terminal SUCCESS with a real signature, the completion
        // must NOT store the signed PDF — it re-checks the enrollment tier and finds no active+approved row.
        // Teeth: drop the C5 re-check and this stores the blob (Completed) — the exact bypass C5 closes.
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (txn, placeholderPdf, rawSig) = BuildInitiatedPades(tenantId, userId);
        var user = new ApplicationUser { Id = userId, TenantId = tenantId, SigningNationalId = "017091004878" };

        var revoked = Substitute.For<ISignerCertEnrollmentRepository>();
        revoked.HasAnyEnrollmentAsync(tenantId, userId, Arg.Any<CancellationToken>()).Returns(true);
        // No active+approved enrollment remains (admin rejected → IsActive=false → not returned here).
        revoked.GetActiveByUserAsync(tenantId, userId, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<SignerCertEnrollment>());

        string statusJson = $$"""{"status":"1","signatures":["{{Convert.ToBase64String(rawSig)}}"]}""";
        var h = BuildHarness(placeholderPdf, statusJson, user, enrollments: revoked);

        await h.Service.CompleteAsync(txn);

        txn.Status.Should().Be(PendingTransactionStatus.Failed,
            "an enrollment revoked between initiate and completion must block the signature");
        txn.FailureReason.Should().NotBeNullOrEmpty();
        h.Writes.Should().BeEmpty("no signed blob may be stored once the enrollment is no longer approved");
    }

    [Fact]
    public async Task CompleteAsync_EnrollmentStillApproved_CompletesAndStores()
    {
        // Anti-vacuous companion to the revoked case: an active+approved enrollment lets completion proceed,
        // proving the C5 re-check does not block legitimate completions.
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (txn, placeholderPdf, rawSig) = BuildInitiatedPades(tenantId, userId);
        var user = new ApplicationUser { Id = userId, TenantId = tenantId, SigningNationalId = "017091004878" };

        var approved = SignerCertEnrollment.Create(
            tenantId, userId, "S1", "CN=CA", "T", null, userId,
            EnrollmentKind.RemoteViettelMysign, CertApprovalStatus.Approved,
            CertSubjectType.Personal, null, Gen);
        var repo = Substitute.For<ISignerCertEnrollmentRepository>();
        repo.HasAnyEnrollmentAsync(tenantId, userId, Arg.Any<CancellationToken>()).Returns(true);
        repo.GetActiveByUserAsync(tenantId, userId, Arg.Any<CancellationToken>()).Returns(new[] { approved });

        string statusJson = $$"""{"status":"1","signatures":["{{Convert.ToBase64String(rawSig)}}"]}""";
        var h = BuildHarness(placeholderPdf, statusJson, user, enrollments: repo);

        await h.Service.CompleteAsync(txn);

        txn.Status.Should().Be(PendingTransactionStatus.Completed);
        h.Writes.Should().ContainSingle();
    }

    // ── Phase 8: HA-safe deterministic signed-PDF key ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CompleteAsync_SignedKey_IsDeterministicFunctionOfTransactionId()
    {
        // The signed-PDF key must be a pure function of (tenant, transactionId) — never a random guid — so an HA
        // re-run or a second poll replica lands on the exact same object key rather than minting a fresh one.
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (txn, placeholderPdf, rawSig) = BuildInitiatedPades(tenantId, userId);
        var user = new ApplicationUser { Id = userId, TenantId = tenantId, SigningNationalId = "017091004878" };
        string statusJson = $$"""{"status":"1","signatures":["{{Convert.ToBase64String(rawSig)}}"]}""";
        var h = BuildHarness(placeholderPdf, statusJson, user);

        await h.Service.CompleteAsync(txn);

        h.Writes.Should().ContainSingle()
            .Which.Key.Should().Be($"{tenantId}/{txn.TransactionId}.signed.pdf");
    }

    [Fact]
    public async Task CompleteAsync_TwoReplicasSameTransaction_WriteSameKeyAndBytes_CollapseToSingleBlob()
    {
        // Two poll replicas both reach terminal success on the SAME persisted transaction (identical resume state).
        // Each assembles + stores independently; because the key is derived from the transaction identity, both
        // writes target the SAME key with byte-identical content — the object store's overwrite-by-key PUT collapses
        // them to ONE object. RED-first: a random-guid key would produce two distinct keys → two stranded blobs.
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, TenantId = tenantId, SigningNationalId = "017091004878" };

        // Build the crypto ONCE so the two entity instances model the same persisted row (same signedAttributes DER
        // + signer cert DER + polled signature) — exactly what two replicas would each load from the DB.
        var pdfWriter = new PdfIncrementalSignatureWriter();
        var cmsBuilder = new PadesCmsBuilder();
        IRawHashSigner localSigner = new LocalRsaHashSigner();
        byte[] input = MinimalPdfFactory.CreateSinglePage("AqtCoreFW PAdES HA replica");
        var prep = pdfWriter.Prepare(input, contentsHexLength: 32 * 1024);
        var attrs = cmsBuilder.BuildSignedAttributes(prep.DocumentDigest, localSigner.SignerChain[0], DateTime.UtcNow);
        byte[] rawSig = localSigner.Sign(attrs.SignInput);
        byte[] certDer = localSigner.SignerChain[0].GetEncoded();

        SigningPendingTransaction MakeTxn()
        {
            var t = SigningPendingTransaction.Initiate(
                "txn-ha-shared", tenantId, userId, "req", "doc", TimeSpan.FromHours(24));
            t.AttachPadesResumeState(
                $"{tenantId}/abc.placeholder.pdf", prep.ContentsGapStart, prep.ContentsHexLength, attrs.Der, certDer);
            return t;
        }

        string statusJson = $$"""{"status":"1","signatures":["{{Convert.ToBase64String(rawSig)}}"]}""";
        var h = BuildHarness(prep.PreparedPdf, statusJson, user);

        await h.Service.CompleteAsync(MakeTxn());
        await h.Service.CompleteAsync(MakeTxn());

        h.Writes.Should().HaveCount(2);
        h.Writes.Select(w => w.Key).Distinct().Should().ContainSingle(
            because: "a deterministic key makes concurrent replica writes overwrite the same object");
        h.Writes[0].Bytes.Should().Equal(h.Writes[1].Bytes,
            because: "the reconstructed PDF is a pure function of the persisted resume state + polled signature");
    }

    // ── CMS read-back helpers (mirror the spike's structural verify) ─────────────────────────────────────────
    private static (int[] byteRange, byte[] cms) ReadSignature(byte[] pdf)
    {
        string text = Encoding.Latin1.GetString(pdf);
        var br = Regex.Match(text, @"/ByteRange \[\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s*\]");
        br.Success.Should().BeTrue();
        int[] range = [int.Parse(br.Groups[1].Value), int.Parse(br.Groups[2].Value),
                       int.Parse(br.Groups[3].Value), int.Parse(br.Groups[4].Value)];
        int open = text.IndexOf("/Contents <", StringComparison.Ordinal) + "/Contents <".Length;
        int close = text.IndexOf('>', open);
        byte[] gap = Convert.FromHexString(text[open..close]);
        using var ais = new Asn1InputStream(gap);
        byte[] cms = ais.ReadObject().GetEncoded("DER");
        return (range, cms);
    }

    private static byte[] ConcatRanges(byte[] pdf, int[] br)
    {
        byte[] result = new byte[br[1] + br[3]];
        Buffer.BlockCopy(pdf, br[0], result, 0, br[1]);
        Buffer.BlockCopy(pdf, br[2], result, br[1], br[3]);
        return result;
    }
}
