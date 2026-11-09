using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Services;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using MediatR;
using Microsoft.Extensions.Logging;

namespace AqtCoreFW.Forms.Application.Commands.SignSubmissionDocument;

/// <summary>
/// Generates a submission PDF, stages it to the signing-original bucket, and initiates a USB
/// DocFlow signing request via the Identity cross-module client.
///
/// The orchestrator failure codes map to HTTP status as follows (applied in the controller):
///   SIGNING_NOT_FOUND                → 404
///   SIGNING_VALIDATION_FAILED        → 422
///   SIGNING_ROUTE_UNSUPPORTED        → 400
///   SIGNING_SEQUENCE_*               → 400
///   SIGNING_NO_EXECUTION_SURFACE     → 400
///   SIGNING_INVALID_ARTIFACT_KIND    → 400
///   SIGNING_INVALID_SIGN_TYPE        → 400
///   SIGNING_FAILED                   → 500 (only "unknown signing failure" sentinel)
///
/// Security invariants:
///   - Tenant isolation: submission is loaded with TenantId guard; cross-tenant → 404.
///   - Self-sign: AssignedSignerId == InitiatedBy (caller); BP-assigned multi-signer is a later phase.
///   - Tenant-prefixed storage key: "{tenantId}/{docId}/{submissionId}.unsigned.pdf".
///     The InitiateDocFlowCommand validator enforces the leading "{tenantId}/" guard server-side.
///   - BE generates the PDF but NEVER parses it. The VGCA desktop tool stamps it client-side.
///   - MFA is enforced at the controller layer ([RequiresMfa]).
/// </summary>
public sealed class GenerateAndInitiateSubmissionSigningCommandHandler(
    IFormSubmissionRepository submissionRepository,
    ISubmissionPdfComposer pdfComposer,
    ISigningOriginalBucketUploader bucketUploader,
    ISigningRequestClient signingClient,
    ILogger<GenerateAndInitiateSubmissionSigningCommandHandler> logger,
    IGuidGenerator guidGen)
    : IRequestHandler<GenerateAndInitiateSubmissionSigningCommand,
                      Result<GenerateAndInitiateSubmissionSigningResult>>
{
    // Bucket constant mirrored from the StageFileForSigningCommandHandler — both sides must agree.
    // The VGCA DocFlow download reads originals exclusively from this bucket.
    private const string SigningOriginalBucket = "signing-original";

    // Artifact kind string as expected by InProcessSigningRequestClient's TryParseArtifactKind.
    // Using a string constant keeps Forms.Application free of Identity.Domain enum references.
    private const string FormPdfArtifactKind = "FormPdf";

    public async Task<Result<GenerateAndInitiateSubmissionSigningResult>> Handle(
        GenerateAndInitiateSubmissionSigningCommand request,
        CancellationToken cancellationToken)
    {
        // ── Step 1: Load submission with tenant-scope guard (cross-tenant → 404) ──────────────
        var submissionResult = await submissionRepository.GetByIdAsync(
            request.SubmissionPublicId, cancellationToken);

        if (submissionResult.IsFailed)
            return Result.Fail(FormErrors.SubmissionNotFound(request.SubmissionPublicId));

        var submission = submissionResult.Value;

        if (submission.TenantId != request.TenantId)
            return Result.Fail(FormErrors.SubmissionNotFound(request.SubmissionPublicId));

        // Guard: at-most-once signing per submission (GOV_FRM_030 prevents overwrite).
        // Callers wishing to re-sign must explicitly handle the linked request first.
        if (submission.SigningRequestId.HasValue)
            return Result.Fail(new ValidationError(
                $"GOV_FRM_030: FormSubmission {request.SubmissionPublicId} đã liên kết với signing request " +
                $"{submission.SigningRequestId}. Không thể khởi tạo lại.",
                "SigningRequestId"));

        // ── Step 2: Generate PDF bytes ─────────────────────────────────────────────────────────
        // ISubmissionPdfComposer loads the submission + template labels via Dapper and renders
        // the QuestPDF document. BE never parses the resulting bytes (no PDF-lib call here).
        var composeResult = await pdfComposer.ComposeAsync(
            request.SubmissionPublicId, request.TenantId, cancellationToken);

        if (composeResult.IsFailed)
            return composeResult.ToResult();

        var pdfBytes = composeResult.Value.Bytes;

        // ── Step 3: Mint docId and upload to signing-original bucket ───────────────────────────
        // Key format: "{tenantId}/{docId}/{submissionPublicId}.unsigned.pdf"
        // The leading "{tenantId}/" satisfies the InitiateDocFlowCommand validator's tenant-prefix
        // guard, making cross-tenant key guessing structurally impossible at the upload level.
        var docId = guidGen.NewIdMonotonic();
        var originalKey = $"{request.TenantId}/{docId}/{request.SubmissionPublicId}.unsigned.pdf";

        try
        {
            await bucketUploader.UploadAsync(pdfBytes, originalKey, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to upload PDF to signing-original bucket for submission {SubmissionId}. Key={Key}",
                request.SubmissionPublicId, originalKey);
            return Result.Fail(new Error(
                $"Tải lên tài liệu gốc thất bại cho phiếu {request.SubmissionPublicId}."));
        }

        // ── Step 4: Initiate signing request via Identity cross-module client ──────────────────
        // BaseUrl is NOT accepted from the FE — the controller derives it server-side from the
        // request host and threads it through the command, so the VGCA callback URL can never be
        // attacker-supplied. Placement is null this round; anchor-coordinate stamping lands later.
        var clientInput = new SigningRequestClientInput
        {
            TenantId          = request.TenantId,
            ArtifactKind      = FormPdfArtifactKind,
            ArtifactRef       = docId.ToString("D"), // lowercase GUID string as expected by DocFlowSigningStrategy
            ProviderKey       = request.ProviderKey,
            AssignedSignerId  = request.InitiatedBy, // self-sign: signer == caller
            InitiatedBy       = request.InitiatedBy,
            Placement         = null,
            SignerSequenceNumber = 1,
            DocFlow = new DocFlowSignClientParameters
            {
                SignType           = request.SignType,
                OriginalStorageKey = originalKey,
                DocNumber          = request.DocNumber,
                IssuedDate         = request.IssuedDate,
                // BaseUrl derived from the HTTP request by the controller (Scheme + Host).
                // The DocFlowSigningStrategy uses it to build the VGCA handler callback URL.
                BaseUrl            = request.BaseUrl,
                FileName           = null, // auto-generated by strategy from DocNumber
                Extra              = null,
                PreviousSignedStorageKey = null, // sequence 1: no predecessor
            }
        };

        var outcome = await signingClient.RequestSignatureAsync(clientInput, cancellationToken);

        if (!outcome.IsSuccess)
        {
            // Orphan cleanup: the uploaded PDF is now unreferenced. Log for the orphan sweep job;
            // do NOT block the error response on cleanup (sweep job handles stale blobs).
            logger.LogWarning(
                "Signing orchestrator rejected request for submission {SubmissionId}. " +
                "Orphaned key={Key} in bucket={Bucket}. ErrorCode={ErrorCode} Message={Message}",
                request.SubmissionPublicId, originalKey, SigningOriginalBucket,
                outcome.ErrorCode, outcome.ErrorMessage);

            // Map orchestrator error code to the correct FluentResults error type so
            // the controller's ToApiResponse can produce 400/422/404 (never raw 500 except SIGNING_FAILED).
            return Result.Fail(MapOutcomeError(outcome));
        }

        // ── Step 5: Link the signing request to the submission + persist ───────────────────────
        try
        {
            submission.LinkSigningRequest(outcome.RequestPublicId);
        }
        catch (InvalidOperationException ex)
        {
            // Concurrent sign-initiation race: another request already linked a request id
            // between the load check above and this point. Surface as 422.
            logger.LogWarning(ex,
                "LinkSigningRequest race on submission {SubmissionId}: already linked.",
                request.SubmissionPublicId);
            return Result.Fail(new ValidationError(ex.Message, "SigningRequestId"));
        }

        await submissionRepository.UpdateAsync(submission, cancellationToken);

        // ── Step 6: Return FE hand-off ─────────────────────────────────────────────────────────
        return Result.Ok(new GenerateAndInitiateSubmissionSigningResult(
            outcome.RequestPublicId,
            outcome.ClientHandoff));
    }

    /// <summary>
    /// Maps the typed error code on <see cref="SigningRequestOutcome"/> to the FluentResults error
    /// type that <c>ApiControllerBase.ToApiResponse</c> maps to the correct HTTP status:
    ///   NotFoundError     → 404  (SIGNING_NOT_FOUND)
    ///   ValidationError   → 422  (SIGNING_VALIDATION_FAILED)
    ///   BadRequestError   → 400  (SIGNING_ROUTE_UNSUPPORTED, SIGNING_SEQUENCE_*, SIGNING_NO_EXECUTION_SURFACE,
    ///                             SIGNING_INVALID_ARTIFACT_KIND, SIGNING_INVALID_SIGN_TYPE)
    ///   Error (generic)   → 500  (SIGNING_FAILED — the only sentinel that should surface as 500)
    /// </summary>
    private static IError MapOutcomeError(SigningRequestOutcome outcome)
    {
        var msg = outcome.ErrorMessage ?? outcome.ErrorCode ?? "Signing request failed.";
        return outcome.ErrorCode switch
        {
            "SIGNING_NOT_FOUND"
                => new NotFoundError(msg),
            "SIGNING_VALIDATION_FAILED"
                => new ValidationError(msg, "Signing"),
            "SIGNING_ROUTE_UNSUPPORTED"
            or "SIGNING_NO_EXECUTION_SURFACE"
            or "SIGNING_INVALID_ARTIFACT_KIND"
            or "SIGNING_INVALID_SIGN_TYPE"
                => new BadRequestError(msg, outcome.ErrorCode!),
            _ when outcome.ErrorCode?.StartsWith("SIGNING_SEQUENCE", StringComparison.Ordinal) == true
                => new BadRequestError(msg, outcome.ErrorCode!),
            "SIGNING_FAILED"
                => new Error(msg),                  // → 500 via ApiControllerBase fallback
            _   => new Error(msg)                   // unknown → 500 (safe default)
        };
    }
}
