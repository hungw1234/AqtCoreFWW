using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// In-process implementation of <see cref="IDocumentSignatureSessionClient"/>.
/// Delegates to <see cref="IdentityDbContext"/> for session persistence. Registered in
/// Identity DI so BusinessProcess can reach it through the SharedKernel contract without
/// taking a compile-time dependency on Identity.Application or Identity.Infrastructure.
///
/// <para>Tenant scoping: every query filters TenantId explicitly — RLS is bypassed under the
/// db_owner login used by the integration test suite and must never be relied upon here.</para>
/// </summary>
public sealed class InProcessDocumentSignatureSessionClient(
    IdentityDbContext db,
    IGuidGenerator guidGenerator,
    ILogger<InProcessDocumentSignatureSessionClient> logger)
    : IDocumentSignatureSessionClient
{
    // ─── Open / get-or-create ────────────────────────────────────────────────

    public async Task<OpenSessionOutcome> OpenSessionAsync(
        Guid tenantId,
        Guid bpInstanceId,
        string stepKey,
        string artifactRef,
        IReadOnlyList<Guid> orderedSigners,
        int? threshold,
        CancellationToken ct = default)
    {
        if (tenantId == Guid.Empty || bpInstanceId == Guid.Empty
            || string.IsNullOrWhiteSpace(stepKey) || string.IsNullOrWhiteSpace(artifactRef)
            || orderedSigners is null || orderedSigners.Count == 0)
            return OpenSessionOutcome.Fail("SESSION_INVALID_INPUT",
                "TenantId, BpInstanceId, StepKey, ArtifactRef, and at least one signer are required.");

        int resolvedThreshold = threshold ?? orderedSigners.Count;

        // Validate threshold range before calling DocumentSignatureSession.Create so that out-of-range
        // values produce a typed SESSION_INVALID_INPUT outcome rather than an ArgumentOutOfRangeException
        // propagating through the stack as a generic 500.
        if (resolvedThreshold < 1 || resolvedThreshold > orderedSigners.Count)
            return OpenSessionOutcome.Fail("SESSION_INVALID_INPUT",
                $"Threshold must be between 1 and {orderedSigners.Count} (the signer count). Got {resolvedThreshold}.");

        // Explicit TenantId filter (db_owner bypasses RLS).
        var existing = await db.DocumentSignatureSessions
            .Include("_signers")
            .FirstOrDefaultAsync(
                s => s.TenantId == tenantId
                     && s.BpInstanceId == bpInstanceId
                     && s.StepKey == stepKey,
                ct);

        if (existing is not null)
            return OpenSessionOutcome.Existing(existing.PublicId);

        // A concurrent caller could race between the read above and the insert below.
        // The UNIQUE constraint on (TenantId, BpInstanceId, StepKey) ensures exactly one
        // insert wins; the loser catches DbUpdateException and retries as a load.
        try
        {
            // System actor: session is created by the BP transition engine, not a named user.
            // We use Guid.Empty as the "system" createdBy sentinel consistent with other system
            // writes in this codebase (audit chain sweep, background jobs).
            var session = DocumentSignatureSession.Create(
                guidGenerator,
                tenantId,
                bpInstanceId,
                stepKey,
                artifactRef,
                orderedSigners,
                resolvedThreshold,
                createdBy: Guid.Empty); // system-initiated

            db.DocumentSignatureSessions.Add(session);
            await db.SaveChangesAsync(ct);

            logger.LogInformation(
                "DocumentSignatureSession created. SessionId={SessionId} TenantId={TenantId} BpInstanceId={BpInstanceId} StepKey={StepKey} Signers={SignerCount} Threshold={Threshold}",
                session.PublicId, tenantId, bpInstanceId, stepKey, orderedSigners.Count, resolvedThreshold);

            return OpenSessionOutcome.Created(session.PublicId);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Concurrent insert: another caller won the race. Load the winner.
            logger.LogDebug(
                "Concurrent OpenSession insert detected for TenantId={TenantId} BpInstanceId={BpInstanceId} StepKey={StepKey}; loading existing session.",
                tenantId, bpInstanceId, stepKey);

            var winner = await db.DocumentSignatureSessions
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    s => s.TenantId == tenantId
                         && s.BpInstanceId == bpInstanceId
                         && s.StepKey == stepKey,
                    ct);

            return winner is not null
                ? OpenSessionOutcome.Existing(winner.PublicId)
                : OpenSessionOutcome.Fail("SESSION_CONCURRENT_LOAD_FAILED",
                    "Concurrent session creation conflict could not be resolved.");
        }
    }

    // ─── Gate state query ────────────────────────────────────────────────────

    public async Task<SessionGateStateOutcome> GetGateStateAsync(
        Guid sessionId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var session = await LoadSessionWithSignersAsync(sessionId, tenantId, ct);
        if (session is null)
            return SessionGateStateOutcome.Fail("SESSION_NOT_FOUND",
                $"Signing session {sessionId} was not found in tenant {tenantId}.");

        var slots = MapSlots(session);

        if (session.IsGateSatisfied())
            return SessionGateStateOutcome.Satisfied(slots);

        var next = session.NextExpectedSigner();
        if (next is null)
            // All terminal but threshold not met (e.g. some Rejected) — gate cannot advance.
            return SessionGateStateOutcome.Pending(Guid.Empty, slots);

        return SessionGateStateOutcome.Pending(next.Value, slots);
    }

    // ─── Signer-slot mutations ───────────────────────────────────────────────

    public async Task<SignerSlotUpdateOutcome> MarkSignerCompletedAsync(
        Guid sessionId,
        Guid tenantId,
        Guid signerId,
        Guid signingRequestPublicId,
        CancellationToken ct = default)
    {
        var session = await LoadSessionWithSignersAsync(sessionId, tenantId, ct);
        if (session is null)
            return SignerSlotUpdateOutcome.Fail("SESSION_NOT_FOUND",
                $"Signing session {sessionId} was not found in tenant {tenantId}.");

        try
        {
            session.MarkSignerCompleted(signerId, signingRequestPublicId, modifiedBy: Guid.Empty);
            await db.SaveChangesAsync(ct);
            return SignerSlotUpdateOutcome.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return SignerSlotUpdateOutcome.Fail("SESSION_SLOT_STATE_CONFLICT", ex.Message);
        }
    }

    public async Task<SignerSlotUpdateOutcome> MarkSignerPendingVerifyAsync(
        Guid sessionId,
        Guid tenantId,
        Guid signerId,
        Guid signingRequestPublicId,
        CancellationToken ct = default)
    {
        var session = await LoadSessionWithSignersAsync(sessionId, tenantId, ct);
        if (session is null)
            return SignerSlotUpdateOutcome.Fail("SESSION_NOT_FOUND",
                $"Signing session {sessionId} was not found in tenant {tenantId}.");

        try
        {
            session.MarkSignerPendingVerify(signerId, signingRequestPublicId, modifiedBy: Guid.Empty);
            await db.SaveChangesAsync(ct);
            return SignerSlotUpdateOutcome.Ok();
        }
        catch (InvalidOperationException ex)
        {
            return SignerSlotUpdateOutcome.Fail("SESSION_SLOT_STATE_CONFLICT", ex.Message);
        }
    }

    // ─── Next signer resolution ──────────────────────────────────────────────

    public async Task<NextSignerOutcome> GetNextSignerAsync(
        Guid sessionId,
        Guid tenantId,
        CancellationToken ct = default)
    {
        var session = await LoadSessionWithSignersAsync(sessionId, tenantId, ct);
        if (session is null)
            return NextSignerOutcome.Fail("SESSION_NOT_FOUND",
                $"Signing session {sessionId} was not found in tenant {tenantId}.");

        var next = session.NextExpectedSigner();
        return next.HasValue
            ? NextSignerOutcome.Found(next.Value)
            : NextSignerOutcome.AllTerminal();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads a session including signer slots, scoped explicitly to TenantId.
    /// Returns null for not-found or cross-tenant (does not leak existence).
    /// The PublicId is the external caller reference; the long Id is internal only.
    /// </summary>
    private async Task<DocumentSignatureSession?> LoadSessionWithSignersAsync(
        Guid sessionPublicId,
        Guid tenantId,
        CancellationToken ct)
        => await db.DocumentSignatureSessions
            .Include("_signers")
            .FirstOrDefaultAsync(
                s => s.PublicId == sessionPublicId && s.TenantId == tenantId,
                ct);

    private static IReadOnlyList<AqtCoreFW.SharedKernel.Contracts.Clients.SignerSlotStatus> MapSlots(
        DocumentSignatureSession session)
        => session.Signers
            .OrderBy(sl => sl.SequenceNumber)
            .Select(sl => new AqtCoreFW.SharedKernel.Contracts.Clients.SignerSlotStatus(
                sl.SignerId,
                sl.SequenceNumber,
                MapState(sl.State),
                sl.SigningRequestPublicId))
            .ToList()
            .AsReadOnly();

    private static AqtCoreFW.SharedKernel.Contracts.Clients.SignerSlotState MapState(
        AqtCoreFW.Identity.Domain.Entities.SignerSlotState domainState)
        => domainState switch
        {
            AqtCoreFW.Identity.Domain.Entities.SignerSlotState.Pending
                => AqtCoreFW.SharedKernel.Contracts.Clients.SignerSlotState.Pending,
            AqtCoreFW.Identity.Domain.Entities.SignerSlotState.PendingVerify
                => AqtCoreFW.SharedKernel.Contracts.Clients.SignerSlotState.PendingVerify,
            AqtCoreFW.Identity.Domain.Entities.SignerSlotState.Completed
                => AqtCoreFW.SharedKernel.Contracts.Clients.SignerSlotState.Completed,
            AqtCoreFW.Identity.Domain.Entities.SignerSlotState.Rejected
                => AqtCoreFW.SharedKernel.Contracts.Clients.SignerSlotState.Rejected,
            _ => throw new ArgumentOutOfRangeException(nameof(domainState), domainState, null)
        };

    /// <summary>
    /// SQL Server unique constraint violation: error number 2601 (duplicate key in unique index)
    /// or 2627 (unique constraint). Catching these lets OpenSessionAsync treat a race as
    /// "load the winner" rather than surfacing a 500 to the caller.
    /// </summary>
    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        var inner = ex.InnerException;
        while (inner is not null)
        {
            if (inner is Microsoft.Data.SqlClient.SqlException sql
                && (sql.Number == 2601 || sql.Number == 2627))
                return true;
            inner = inner.InnerException;
        }
        return false;
    }
}
