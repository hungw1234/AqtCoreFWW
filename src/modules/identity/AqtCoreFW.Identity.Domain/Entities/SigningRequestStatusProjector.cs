namespace AqtCoreFW.Identity.Domain.Entities;

/// <summary>
/// Pure read-time projection from an underlying provider record's state → <see cref="SigningRequestStatus"/>.
///
/// <para>Asymmetric cells are explicit by design: VGCA doc-flow has a real intermediate
/// (<c>Downloaded</c>) so it maps to <see cref="SigningRequestStatus.InProgress"/>; Viettel remote has no
/// pre-callback intermediate, so its <c>Initiated</c> (signHash already submitted, awaiting callback) maps to
/// <see cref="SigningRequestStatus.InProgress"/> as well — the request IS in flight, not merely queued. A
/// request with no underlying record yet projects to <see cref="SigningRequestStatus.Pending"/> unless it
/// carries a creation failure, in which case <see cref="SigningRequestStatus.Failed"/>.</para>
///
/// <para>No DB dependency — fully unit-testable. The orchestrator loads the underlying record (or null) and
/// calls the matching method based on <see cref="SigningRequest.UnderlyingKind"/>.</para>
/// </summary>
public static class SigningRequestStatusProjector
{
    /// <summary>Status of a request that has not yet been linked to an underlying record.</summary>
    public static SigningRequestStatus ForUnlinked(bool hasCreationFailure)
        => hasCreationFailure ? SigningRequestStatus.Failed : SigningRequestStatus.Pending;

    /// <summary>Maps the VGCA doc-flow state machine onto the unified projection.</summary>
    public static SigningRequestStatus FromDocFlow(DocFlowStatus status) => status switch
    {
        DocFlowStatus.Initiated  => SigningRequestStatus.Pending,
        DocFlowStatus.Downloaded => SigningRequestStatus.InProgress,
        DocFlowStatus.Completed  => SigningRequestStatus.Completed,
        DocFlowStatus.Failed     => SigningRequestStatus.Failed,
        DocFlowStatus.Expired    => SigningRequestStatus.Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped DocFlowStatus."),
    };

    /// <summary>Maps the Viettel Mysign async-callback state machine onto the unified projection.</summary>
    public static SigningRequestStatus FromPendingTransaction(PendingTransactionStatus status) => status switch
    {
        // No pre-callback intermediate: signHash submitted == in flight.
        PendingTransactionStatus.Initiated => SigningRequestStatus.InProgress,
        PendingTransactionStatus.Completed => SigningRequestStatus.Completed,
        PendingTransactionStatus.Failed    => SigningRequestStatus.Failed,
        PendingTransactionStatus.Expired   => SigningRequestStatus.Expired,
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unmapped PendingTransactionStatus."),
    };
}
