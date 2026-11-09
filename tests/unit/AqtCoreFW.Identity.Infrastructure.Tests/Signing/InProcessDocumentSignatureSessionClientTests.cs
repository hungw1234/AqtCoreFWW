using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.Identity.Infrastructure.Services.Signing;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Signing;

/// <summary>
/// Unit tests for <see cref="InProcessDocumentSignatureSessionClient"/> boundary guards.
/// Uses an InMemory database — sufficient for input-validation paths that never reach a
/// unique constraint (no SQL Server needed for these cases).
/// </summary>
public sealed class InProcessDocumentSignatureSessionClientTests
{
    private static readonly Guid Tenant     = Guid.NewGuid();
    private static readonly Guid BpInstance = Guid.NewGuid();
    private static readonly Guid SignerA    = Guid.NewGuid();
    private static readonly Guid SignerB    = Guid.NewGuid();
    private static readonly Guid SignerC    = Guid.NewGuid();

    private static IdentityDbContext BuildDb()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new IdentityDbContext(options);
    }

    private static InProcessDocumentSignatureSessionClient BuildSut(IdentityDbContext db)
    {
        var gen = Substitute.For<IGuidGenerator>();
        gen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        return new InProcessDocumentSignatureSessionClient(
            db, gen, NullLogger<InProcessDocumentSignatureSessionClient>.Instance);
    }

    // ─── M-b: threshold range validation ────────────────────────────────────

    [Fact]
    public async Task OpenSession_ThresholdExceedsSignerCount_ReturnsTypedInvalidInput()
    {
        await using var db = BuildDb();
        var sut = BuildSut(db);

        // 2 signers, threshold = 3 → out of range
        var outcome = await sut.OpenSessionAsync(
            Tenant, BpInstance, "step-sign", "tenant/doc.pdf",
            [SignerA, SignerB], threshold: 3);

        outcome.IsSuccess.Should().BeFalse();
        outcome.ErrorCode.Should().Be("SESSION_INVALID_INPUT");
        outcome.ErrorMessage.Should().Contain("Threshold");
    }

    [Fact]
    public async Task OpenSession_ThresholdZero_ReturnsTypedInvalidInput()
    {
        await using var db = BuildDb();
        var sut = BuildSut(db);

        var outcome = await sut.OpenSessionAsync(
            Tenant, BpInstance, "step-sign", "tenant/doc.pdf",
            [SignerA], threshold: 0);

        outcome.IsSuccess.Should().BeFalse();
        outcome.ErrorCode.Should().Be("SESSION_INVALID_INPUT");
    }

    [Fact]
    public async Task OpenSession_ThresholdNegative_ReturnsTypedInvalidInput()
    {
        await using var db = BuildDb();
        var sut = BuildSut(db);

        var outcome = await sut.OpenSessionAsync(
            Tenant, BpInstance, "step-sign", "tenant/doc.pdf",
            [SignerA, SignerB], threshold: -1);

        outcome.IsSuccess.Should().BeFalse();
        outcome.ErrorCode.Should().Be("SESSION_INVALID_INPUT");
    }

    [Theory]
    [InlineData(1, 1)]   // min valid
    [InlineData(1, 2)]   // 1-of-N
    [InlineData(2, 2)]   // all-of-N
    [InlineData(2, 3)]   // M-of-N
    [InlineData(3, 3)]   // max valid
    public async Task OpenSession_ThresholdInRange_DoesNotReturnInvalidInput(int threshold, int signerCount)
    {
        // Threshold guard fires BEFORE the DB query, so we only need to verify the guard does NOT
        // early-return SESSION_INVALID_INPUT. The subsequent DB operation may fail under InMemory
        // (string-based navigation) — that is NOT what this test covers.
        await using var db = BuildDb();
        var sut = BuildSut(db);
        var signers = Enumerable.Range(0, signerCount).Select(_ => Guid.NewGuid()).ToList();

        OpenSessionOutcome? outcome = null;
        try
        {
            outcome = await sut.OpenSessionAsync(
                Tenant, BpInstance, $"step-{Guid.NewGuid()}", "tenant/doc.pdf",
                signers, threshold: threshold);
        }
        catch (InvalidOperationException)
        {
            // InMemory DB may throw on string-based Include("_signers") navigation.
            // If we reach here the threshold guard passed (did not return early), which is what we verify.
            return;
        }

        // If no exception, must not have been rejected by the threshold guard.
        outcome!.ErrorCode.Should().NotBe("SESSION_INVALID_INPUT",
            "a valid threshold should not be rejected by the SESSION_INVALID_INPUT guard");
    }

    // ─── Basic input validation (unchanged guards) ───────────────────────────

    [Fact]
    public async Task OpenSession_EmptyTenantId_ReturnsTypedInvalidInput()
    {
        await using var db = BuildDb();
        var sut = BuildSut(db);

        var outcome = await sut.OpenSessionAsync(
            Guid.Empty, BpInstance, "step", "tenant/doc.pdf", [SignerA], threshold: null);

        outcome.IsSuccess.Should().BeFalse();
        outcome.ErrorCode.Should().Be("SESSION_INVALID_INPUT");
    }

    [Fact]
    public async Task OpenSession_EmptySignerList_ReturnsTypedInvalidInput()
    {
        await using var db = BuildDb();
        var sut = BuildSut(db);

        var outcome = await sut.OpenSessionAsync(
            Tenant, BpInstance, "step", "tenant/doc.pdf", [], threshold: null);

        outcome.IsSuccess.Should().BeFalse();
        outcome.ErrorCode.Should().Be("SESSION_INVALID_INPUT");
    }
}
