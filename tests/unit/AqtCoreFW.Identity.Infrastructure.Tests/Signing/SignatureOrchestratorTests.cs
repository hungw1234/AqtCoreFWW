using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts;
using AqtCoreFW.SharedKernel.Errors;
using FluentAssertions;
using FluentResults;
using Microsoft.EntityFrameworkCore;
using NSubstitute;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Behaviour of <see cref="SignatureOrchestrator.RequestSignatureAsync"/>: it executes the routed strategy, then
/// persists the <see cref="SigningRequest"/> already linked to the underlying record the strategy produced, and
/// surfaces a clear error when the matched strategy has no execution surface yet.
/// </summary>
public sealed class SignatureOrchestratorTests
{
    private const string ProviderKey = "usb-vgca";

    private static IdentityDbContext NewDb() =>
        new(new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    private static SigningStrategyRouter RouterWith(ISigningStrategy strategy)
    {
        var resolver = Substitute.For<ISignatureProviderResolver>();
        resolver.Resolve(ProviderKey).Returns(new FakeProvider(ProviderKey,
            SignatureProviderCapabilities.ClientSideSigning | SignatureProviderCapabilities.DocFlow));
        return new SigningStrategyRouter(resolver, [strategy]);
    }

    private static IGuidGenerator GuidGen()
    {
        var g = Substitute.For<IGuidGenerator>();
        g.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return g;
    }

    // The provider allowlist guard runs first in the orchestrator; these routing/persistence tests are not about
    // authorization, so a permissive guard keeps them focused (guard behaviour is covered by its own suites).
    private static ISigningProviderAllowlistGuard AllowAllGuard()
    {
        var guard = Substitute.For<ISigningProviderAllowlistGuard>();
        guard.EnsureAllowedAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok());
        return guard;
    }

    // The orchestrator gates UploadedPdf on feature:signing.upload-arbitrary-pdf. These routing/persistence tests
    // exercise the UploadedPdf route itself, so they run with the flag ON to pass the gate; the gate's own
    // behaviour (both branches) is covered by UploadedPdf_FlagOff_* / non-upload-kind tests below.
    private static IFeatureFlagService FlagEnabled(bool enabled = true)
    {
        var flags = Substitute.For<IFeatureFlagService>();
        flags.IsEnabled(Arg.Any<string>(), Arg.Any<string?>()).Returns(enabled);
        return flags;
    }

    private static RequestSignatureInput Input() => new(
        TenantId:         Guid.NewGuid(),
        ArtifactKind:     SigningArtifactKind.UploadedPdf,
        ArtifactRef:      Guid.NewGuid().ToString(),
        ProviderKey:      ProviderKey,
        AssignedSignerId: Guid.NewGuid(),
        InitiatedBy:      Guid.NewGuid());

    [Fact]
    public async Task RequestSignatureAsync_PersistsLinkedRequest_AndReturnsHandoff()
    {
        await using var db = NewDb();
        var handoff = new Dictionary<string, string> { ["handlerUrl"] = "u", ["jwt"] = "j", ["docId"] = "d" };
        var strategy = new FakeExecutionStrategy(
            new SigningExecutionResult(SigningUnderlyingKind.DocFlow, "777", handoff));
        var orchestrator = new SignatureOrchestrator(
            db, RouterWith(strategy), AllowAllGuard(), FlagEnabled(), GuidGen(), Substitute.For<ISigningPlaceholderPdfReader>());

        var result = await orchestrator.RequestSignatureAsync(Input());

        result.IsSuccess.Should().BeTrue();
        result.Value.PublicId.Should().NotBeEmpty();
        result.Value.ClientHandoff.Should().BeEquivalentTo(handoff);
        strategy.Executed.Should().BeTrue();

        var saved = await db.SigningRequests.SingleAsync();
        saved.UnderlyingKind.Should().Be(SigningUnderlyingKind.DocFlow);
        saved.UnderlyingRecordId.Should().Be("777");
        saved.PublicId.Should().Be(result.Value.PublicId);
    }

    // ── Untrusted-PDF upload-sign gate ───────────────────────────────────────────
    //
    // The orchestrator is the single chokepoint for BOTH request callers (the MediatR command AND the
    // cross-module InProcessSigningRequestClient), so the flag gate lives here. UploadedPdf is refused when
    // the flag is off; FormPdf / DetachedData are never gated by it.

    [Fact]
    public async Task RequestSignatureAsync_UploadedPdf_FlagOff_RefusesWithConflict_PersistsNothing()
    {
        await using var db = NewDb();
        var handoff = new Dictionary<string, string> { ["handlerUrl"] = "u", ["jwt"] = "j", ["docId"] = "d" };
        // A working strategy is wired so the ONLY reason to fail is the gate — proving the gate short-circuits
        // before routing/persistence, not that the route happened to be missing.
        var orchestrator = new SignatureOrchestrator(
            db, RouterWith(new FakeExecutionStrategy(
                new SigningExecutionResult(SigningUnderlyingKind.DocFlow, "1", handoff))),
            AllowAllGuard(), FlagEnabled(enabled: false), GuidGen(), Substitute.For<ISigningPlaceholderPdfReader>());

        var result = await orchestrator.RequestSignatureAsync(Input()); // Input() is UploadedPdf

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainItemsAssignableTo<ConflictError>();
        (await db.SigningRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RequestSignatureAsync_FormPdf_FlagOff_NotGated_ReachesRouting()
    {
        await using var db = NewDb();
        // FormPdf (a launch path) must NOT be gated by the upload-sign flag. With a routing-only strategy the
        // request falls through the gate to routing and fails BadRequest there — the point is that the failure
        // is NOT the gate's ConflictError, proving the launch path is untouched with the flag off.
        var input = Input() with { ArtifactKind = SigningArtifactKind.FormPdf };
        var orchestrator = new SignatureOrchestrator(
            db, RouterWith(new RoutingOnlyStrategy()), AllowAllGuard(), FlagEnabled(enabled: false),
            GuidGen(), Substitute.For<ISigningPlaceholderPdfReader>());

        var result = await orchestrator.RequestSignatureAsync(input);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().NotContainItemsAssignableTo<ConflictError>();
        result.Errors.Should().ContainItemsAssignableTo<BadRequestError>();
    }

    [Fact]
    public async Task RequestSignatureAsync_FailsBadRequest_WhenStrategyHasNoExecutionSurface()
    {
        await using var db = NewDb();
        // Routing-only strategy (no ISigningExecutionStrategy) → orchestrator returns a typed failure (→ 400) and
        // persists nothing.
        var orchestrator = new SignatureOrchestrator(
            db, RouterWith(new RoutingOnlyStrategy()), AllowAllGuard(), FlagEnabled(), GuidGen(), Substitute.For<ISigningPlaceholderPdfReader>());

        var result = await orchestrator.RequestSignatureAsync(Input());

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainItemsAssignableTo<BadRequestError>();
        (await db.SigningRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RequestSignatureAsync_WritesNoRequestRow_WhenStrategyThrows()
    {
        await using var db = NewDb();
        // A strategy that throws mid-execution must leave no orphan request row: Add runs only after a successful
        // execution. This locks the create→execute→link→save ordering against future reordering.
        var orchestrator = new SignatureOrchestrator(
            db, RouterWith(new ThrowingExecutionStrategy()), AllowAllGuard(), FlagEnabled(), GuidGen(), Substitute.For<ISigningPlaceholderPdfReader>());

        var act = async () => await orchestrator.RequestSignatureAsync(Input());

        await act.Should().ThrowAsync<InvalidOperationException>();
        (await db.SigningRequests.CountAsync()).Should().Be(0);
    }

    // ── Sequence guard ─────────────────────────────────────────────────────────
    //
    // Invariant: signer N may not begin while signer N-1 has not yet reached Completed.
    // The guard is a typed Result failure (→ 400/422), never an exception, so the HTTP
    // layer maps it to 4xx. The guard reads under TenantId scope to stay db_owner-safe.

    [Fact]
    public async Task RequestSignatureAsync_Sequence2_FailsBadRequest_WhenPredecessorNotFound()
    {
        // No sequence-1 request exists for this artifact — sequence 2 must be rejected.
        await using var db = NewDb();
        var handoff = new Dictionary<string, string> { ["handlerUrl"] = "u", ["jwt"] = "j", ["docId"] = "d" };
        var orchestrator = new SignatureOrchestrator(
            db, RouterWith(new FakeExecutionStrategy(
                new SigningExecutionResult(SigningUnderlyingKind.DocFlow, "1", handoff))),
            AllowAllGuard(), FlagEnabled(), GuidGen(), Substitute.For<ISigningPlaceholderPdfReader>());

        var result = await orchestrator.RequestSignatureAsync(InputWithSequence(sequence: 2));

        result.IsFailed.Should().BeTrue("sequence 2 requires a completed sequence 1 to exist");
        result.Errors.Should().ContainItemsAssignableTo<BadRequestError>();
        result.Errors.First().Message.Should().Contain("sequence 1",
            because: "the error must name the missing predecessor sequence number");
        // No request row should be persisted when the guard rejects.
        (await db.SigningRequests.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task RequestSignatureAsync_Sequence2_FailsBadRequest_WhenPredecessorNotCompleted()
    {
        // Sequence 1 exists but has no linked underlying record (projects to Pending) — must be rejected.
        await using var db = NewDb();
        var tenantId    = Guid.NewGuid();
        var artifactRef = Guid.NewGuid().ToString();

        // Seed an unlinked (Pending) sequence-1 SigningRequest.
        var seq1Request = SigningRequest.Create(
            GuidGen(), tenantId, SigningArtifactKind.UploadedPdf,
            artifactRef, ProviderKey, Guid.NewGuid(), Guid.NewGuid(),
            signerSequenceNumber: 1);
        db.SigningRequests.Add(seq1Request);
        await db.SaveChangesAsync();

        var handoff = new Dictionary<string, string> { ["handlerUrl"] = "u", ["jwt"] = "j", ["docId"] = "d" };
        var orchestrator = new SignatureOrchestrator(
            db, RouterWith(new FakeExecutionStrategy(
                new SigningExecutionResult(SigningUnderlyingKind.DocFlow, "2", handoff))),
            AllowAllGuard(), FlagEnabled(), GuidGen(), Substitute.For<ISigningPlaceholderPdfReader>());

        var input = new RequestSignatureInput(
            TenantId:            tenantId,
            ArtifactKind:        SigningArtifactKind.UploadedPdf,
            ArtifactRef:         artifactRef,
            ProviderKey:         ProviderKey,
            AssignedSignerId:    Guid.NewGuid(),
            InitiatedBy:         Guid.NewGuid(),
            SignerSequenceNumber: 2);

        var result = await orchestrator.RequestSignatureAsync(input);

        result.IsFailed.Should().BeTrue("sequence 2 must be blocked while sequence 1 is not Completed");
        result.Errors.Should().ContainItemsAssignableTo<BadRequestError>();
        result.Errors.First().Message.Should().Contain("Pending",
            because: "the error must report the predecessor's current status");
    }

    [Fact]
    public async Task RequestSignatureAsync_Sequence1_SkipsGuard_AndSucceeds()
    {
        // Sequence 1 never triggers the guard (no predecessor exists by definition).
        await using var db = NewDb();
        var handoff = new Dictionary<string, string> { ["handlerUrl"] = "u", ["jwt"] = "j", ["docId"] = "d" };
        var strategy = new FakeExecutionStrategy(
            new SigningExecutionResult(SigningUnderlyingKind.DocFlow, "1", handoff));
        var orchestrator = new SignatureOrchestrator(
            db, RouterWith(strategy), AllowAllGuard(), FlagEnabled(), GuidGen(), Substitute.For<ISigningPlaceholderPdfReader>());

        var result = await orchestrator.RequestSignatureAsync(InputWithSequence(sequence: 1));

        result.IsSuccess.Should().BeTrue("sequence 1 has no predecessor to guard against");
        strategy.Executed.Should().BeTrue();
    }

    [Fact]
    public async Task RequestSignatureAsync_TwoRoundChain_Sequence2_SucceedsWhenSequence1Completed()
    {
        // Full 2-signer chain:
        //   Round 1: sequence 1, no previous key → request persisted linked to round-1 DocFlowRecord.
        //            DocFlowRecord transitions to Completed (simulating the desktop tool upload).
        //   Round 2: sequence 2, previousSignedKey = round-1 signed key → guard passes (seq1 Completed),
        //            strategy executes, both requests persisted with distinct sequence numbers.
        //
        // NOTE: The final Adobe two-signature-panel verification (both panels valid after incremental
        // sign) is a manual gate — the desktop tool performs the PAdES append; the BE does not parse PDF.
        await using var db = NewDb();

        var tenantId    = Guid.NewGuid();
        var artifactRef = Guid.NewGuid().ToString(); // shared across both signing rounds
        var signerA     = Guid.NewGuid();
        var signerB     = Guid.NewGuid();
        var initiator   = Guid.NewGuid();

        // ── Pre-seed round-1 DocFlowRecord so we know its InMemory-assigned Id ─
        // Seed it FIRST so its Id (InMemory IDENTITY starts at 1) is known before the strategy runs.
        // The FakeExecutionStrategy will report this id as the underlying record id, and the orchestrator
        // stores it — this keeps ProjectStatusAsync able to find and project the record to Completed.
        const string round1SignedKey = "11111111-1111-1111-1111-111111111111/doc/round1.signed.pdf";

        var docFlowRec1 = SigningDocFlowRecord.Initiate(
            tenantId:            tenantId,
            docId:               Guid.NewGuid(),
            signType:            DocFlowSignType.Issued,
            originalStorageKey:  $"{tenantId}/doc/original.pdf",
            jwtNonce:            Guid.NewGuid(),
            initiatedBy:         signerA,
            signerSequenceNumber: 1);
        db.SigningDocFlowRecords.Add(docFlowRec1);
        await db.SaveChangesAsync(); // Id now assigned by InMemory EF

        var round1RecordIdStr = docFlowRec1.Id.ToString();

        // ── Round 1: sequence 1, first signer ─────────────────────────────────
        var round1Handoff = new Dictionary<string, string>
            { ["handlerUrl"] = "u1", ["jwt"] = "jwt1", ["docId"] = "doc1" };

        var round1Strategy = new FakeExecutionStrategy(
            new SigningExecutionResult(SigningUnderlyingKind.DocFlow, round1RecordIdStr, round1Handoff));

        var orch1 = new SignatureOrchestrator(
            db, RouterWith(round1Strategy), AllowAllGuard(), FlagEnabled(), GuidGen(), Substitute.For<ISigningPlaceholderPdfReader>());

        var round1Result = await orch1.RequestSignatureAsync(new RequestSignatureInput(
            TenantId:            tenantId,
            ArtifactKind:        SigningArtifactKind.UploadedPdf,
            ArtifactRef:         artifactRef,
            ProviderKey:         ProviderKey,
            AssignedSignerId:    signerA,
            InitiatedBy:         initiator,
            SignerSequenceNumber: 1));

        round1Result.IsSuccess.Should().BeTrue("round 1 (first signer) must succeed unconditionally");
        round1Strategy.Executed.Should().BeTrue();

        // Transition DocFlowRecord to Completed (simulating desktop tool download → upload).
        docFlowRec1.MarkDownloaded();
        docFlowRec1.Complete(round1SignedKey, signerA);
        await db.SaveChangesAsync();

        // ── Round 2: sequence 2, second signer ────────────────────────────────
        // Pre-seed round-2 DocFlowRecord (same pattern as round 1) so the InMemory-assigned Id is
        // known upfront and passed to the FakeExecutionStrategy as the underlying record Id.
        var docFlowRec2 = SigningDocFlowRecord.Initiate(
            tenantId:            tenantId,
            docId:               Guid.NewGuid(),
            signType:            DocFlowSignType.Issued,
            originalStorageKey:  round1SignedKey, // round 2 downloads the round-1 signed output
            jwtNonce:            Guid.NewGuid(),
            initiatedBy:         signerB,
            signerSequenceNumber: 2,
            previousSignedStorageKey: round1SignedKey);
        db.SigningDocFlowRecords.Add(docFlowRec2);
        await db.SaveChangesAsync();

        var round2Handoff = new Dictionary<string, string>
            { ["handlerUrl"] = "u2", ["jwt"] = "jwt2", ["docId"] = "doc2" };

        var round2Strategy = new FakeExecutionStrategy(
            new SigningExecutionResult(SigningUnderlyingKind.DocFlow, docFlowRec2.Id.ToString(), round2Handoff));

        var orch2 = new SignatureOrchestrator(
            db, RouterWith(round2Strategy), AllowAllGuard(), FlagEnabled(), GuidGen(), Substitute.For<ISigningPlaceholderPdfReader>());

        var round2Result = await orch2.RequestSignatureAsync(new RequestSignatureInput(
            TenantId:            tenantId,
            ArtifactKind:        SigningArtifactKind.UploadedPdf,
            ArtifactRef:         artifactRef,
            ProviderKey:         ProviderKey,
            AssignedSignerId:    signerB,
            InitiatedBy:         initiator,
            SignerSequenceNumber: 2,
            DocFlow: new DocFlowSignParameters(
                SignType:               DocFlowSignType.Issued,
                OriginalStorageKey:     $"{tenantId}/doc/original.pdf",
                DocNumber:              "VB-001",
                IssuedDate:             DateTime.UtcNow,
                BaseUrl:                "https://api.example.com",
                PreviousSignedStorageKey: round1SignedKey,
                SignerSequenceNumber:   2)));

        round2Result.IsSuccess.Should().BeTrue("round 2 must succeed once round 1 is Completed");

        // Verify both signing requests persisted with the correct, distinct sequence numbers.
        var allRequests = await db.SigningRequests
            .Where(r => r.TenantId == tenantId && r.ArtifactRef == artifactRef)
            .OrderBy(r => r.SignerSequenceNumber)
            .ToListAsync();

        allRequests.Should().HaveCount(2, "exactly 2 SigningRequests for this 2-signer chain");
        allRequests[0].SignerSequenceNumber.Should().Be(1);
        allRequests[0].AssignedSignerId.Should().Be(signerA);
        allRequests[1].SignerSequenceNumber.Should().Be(2);
        allRequests[1].AssignedSignerId.Should().Be(signerB);
        allRequests[1].UnderlyingRecordId.Should().Be(docFlowRec2.Id.ToString(),
            "round-2 request must link to its own DocFlowRecord, distinct from round 1");
    }

    // ── GetSignedPdfAsync (server-side PAdES download) ───────────────────────────────────────────────────────

    /// <summary>Seeds a SigningRequest linked to a PendingTransaction; completed (with signed key) when provided.</summary>
    private async Task<Guid> SeedPadesRequestAsync(
        IdentityDbContext db, Guid tenantId, string? signedKey,
        SigningUnderlyingKind kind = SigningUnderlyingKind.PendingTransaction)
    {
        string txnId = $"txn-{Guid.NewGuid():N}";
        var txn = SigningPendingTransaction.Initiate(txnId, tenantId, Guid.NewGuid(), "req", "doc", TimeSpan.FromHours(24));
        txn.AttachPadesResumeState("tenant/p.placeholder.pdf", 790, 32768, [1, 2, 3], [4, 5, 6]);
        if (signedKey is not null) txn.CompletePades([7, 7], signedKey);
        db.SigningPendingTransactions.Add(txn);

        var req = SigningRequest.Create(
            GuidGen(), tenantId, SigningArtifactKind.UploadedPdf, Guid.NewGuid().ToString(),
            ProviderKey, Guid.NewGuid(), Guid.NewGuid(), null, 1, null);
        req.LinkUnderlying(kind, txnId, Guid.NewGuid());
        db.SigningRequests.Add(req);
        await db.SaveChangesAsync();
        return req.PublicId;
    }

    private static SignatureOrchestrator OrchestratorWithReader(IdentityDbContext db, ISigningPlaceholderPdfReader reader)
        => new(db, RouterWith(new RoutingOnlyStrategy()), AllowAllGuard(), FlagEnabled(), GuidGen(), reader);

    [Fact]
    public async Task GetSignedPdfAsync_CompletedPades_StreamsStoredBytes()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var publicId = await SeedPadesRequestAsync(db, tenantId, signedKey: "tenant/x.signed.pdf");

        byte[] pdf = [0x25, 0x50, 0x44, 0x46]; // %PDF
        var reader = Substitute.For<ISigningPlaceholderPdfReader>();
        reader.ReadAsync("tenant/x.signed.pdf", Arg.Any<CancellationToken>()).Returns(pdf);

        var result = await OrchestratorWithReader(db, reader).GetSignedPdfAsync(publicId, tenantId);

        result.IsSuccess.Should().BeTrue(result.IsFailed ? result.Errors[0].Message : "");
        result.Value.Content.Should().Equal(pdf);
        result.Value.FileName.Should().Contain(publicId.ToString());
    }

    [Fact]
    public async Task GetSignedPdfAsync_OtherTenant_ReturnsNotFound_AndNeverReads()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var publicId = await SeedPadesRequestAsync(db, tenantId, signedKey: "tenant/x.signed.pdf");

        var reader = Substitute.For<ISigningPlaceholderPdfReader>();
        var result = await OrchestratorWithReader(db, reader).GetSignedPdfAsync(publicId, Guid.NewGuid());

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<NotFoundError>();
        await reader.DidNotReceive().ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSignedPdfAsync_NotYetCompleted_ReturnsBadRequest()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var publicId = await SeedPadesRequestAsync(db, tenantId, signedKey: null); // Initiated, not completed

        var reader = Substitute.For<ISigningPlaceholderPdfReader>();
        var result = await OrchestratorWithReader(db, reader).GetSignedPdfAsync(publicId, tenantId);

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<BadRequestError>();
        await reader.DidNotReceive().ReadAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSignedPdfAsync_NonPadesRequest_ReturnsBadRequest()
    {
        await using var db = NewDb();
        var tenantId = Guid.NewGuid();
        var publicId = await SeedPadesRequestAsync(db, tenantId, "k", kind: SigningUnderlyingKind.DocFlow);

        var reader = Substitute.For<ISigningPlaceholderPdfReader>();
        var result = await OrchestratorWithReader(db, reader).GetSignedPdfAsync(publicId, tenantId);

        result.IsFailed.Should().BeTrue();
        result.Errors[0].Should().BeOfType<BadRequestError>(because: "a DocFlow request has no BE-assembled PAdES PDF");
    }

    private static RequestSignatureInput InputWithSequence(int sequence)
    {
        var tenantId = Guid.NewGuid();
        return new RequestSignatureInput(
            TenantId:            tenantId,
            ArtifactKind:        SigningArtifactKind.UploadedPdf,
            ArtifactRef:         Guid.NewGuid().ToString(),
            ProviderKey:         ProviderKey,
            AssignedSignerId:    Guid.NewGuid(),
            InitiatedBy:         Guid.NewGuid(),
            SignerSequenceNumber: sequence);
    }

    private sealed class FakeExecutionStrategyCapturing(
        SigningExecutionResult result,
        Action<SigningExecutionContext> capture) : ISigningExecutionStrategy
    {
        public string Name => "fake-capturing";
        public bool CanHandle(SigningArtifactKind k, SignatureProviderCapabilities c) => true;
        public Task<Result<SigningExecutionResult>> ExecuteAsync(SigningExecutionContext context, CancellationToken ct = default)
        {
            capture(context);
            return Task.FromResult(Result.Ok(result));
        }
    }

    private sealed class FakeExecutionStrategy(SigningExecutionResult result) : ISigningExecutionStrategy
    {
        public bool Executed { get; private set; }
        public string Name => "fake-exec";
        public bool CanHandle(SigningArtifactKind k, SignatureProviderCapabilities c) => true;
        public Task<Result<SigningExecutionResult>> ExecuteAsync(SigningExecutionContext context, CancellationToken ct = default)
        {
            Executed = true;
            return Task.FromResult(Result.Ok(result));
        }
    }

    private sealed class ThrowingExecutionStrategy : ISigningExecutionStrategy
    {
        public string Name => "throwing-exec";
        public bool CanHandle(SigningArtifactKind k, SignatureProviderCapabilities c) => true;
        public Task<Result<SigningExecutionResult>> ExecuteAsync(SigningExecutionContext context, CancellationToken ct = default)
            => throw new InvalidOperationException("boom mid-execution");
    }

    private sealed class RoutingOnlyStrategy : ISigningStrategy
    {
        public string Name => "routing-only";
        public bool CanHandle(SigningArtifactKind k, SignatureProviderCapabilities c) => true;
    }

    private sealed class FakeProvider(string key, SignatureProviderCapabilities caps) : IDigitalSignatureProvider
    {
        public string ProviderKey => key;
        public string DisplayName => key;
        public SignatureProviderCapabilities Capabilities => caps;
        public Task<SignResult> SignAsync(SignRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("orchestrator test stub — not invoked");
        public Task<VerifyResult> VerifyAsync(VerifyRequest request, CancellationToken cancellationToken = default)
            => throw new NotSupportedException("orchestrator test stub — not invoked");
    }
}
