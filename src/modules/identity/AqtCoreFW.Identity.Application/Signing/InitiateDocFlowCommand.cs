using System.Text.Json;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Application;
using AqtCoreFW.SharedKernel.Compliance;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using FluentValidation;
using MediatR;

namespace AqtCoreFW.Identity.Application.Signing;

// ─── Command ─────────────────────────────────────────────────────────────────

/// <summary>
/// Initiates a Tier-3 VGCA doc-flow signing session (W3, QĐ 742 mục 5.1).
///
/// <para>Creates a <see cref="SigningDocFlowRecord"/> + <see cref="SigningDocFlowNonce"/>,
/// issues a short-lived HS256 JWT, and returns <see cref="InitiateDocFlowResultDto"/> containing
/// the handler URL, JWT, and docId for the FE to pass into the VGCA plugin call.</para>
///
/// <para>For incremental multi-signer PAdES chaining: when <see cref="SignerSequenceNumber"/> &gt; 1,
/// <see cref="PreviousSignedStorageKey"/> MUST be supplied and must reside within the caller's tenant
/// prefix. The desktop tool downloads this artifact and appends an incremental signature without
/// invalidating prior signers' signatures. The server derives <c>isFirstSignature</c> from the sequence
/// number and never trusts the caller to self-report it.</para>
///
/// [SuppressAutoAudit]: handler emits a structured audit entry with docId, signType, userId.
/// </summary>
[AuditCategory(AuditCategory.AppAccess)]
[SuppressAutoAudit]
public sealed record InitiateDocFlowCommand(
    Guid TenantId,
    Guid DocId,
    DocFlowSignType SignType,
    string OriginalStorageKey,
    DocFlowMetadata Metadata,
    Guid UserId,
    /// <summary>Base URL of the API host (e.g. "https://api.example.com") — used to build HandlerUrl.</summary>
    string BaseUrl,
    /// <summary>
    /// Position of this request in an ordered multi-signer chain (1-based). Defaults to 1 for single-signer
    /// documents. The server derives whether this is the first signature from this value — never caller-trusted.
    /// </summary>
    int SignerSequenceNumber = 1,
    /// <summary>
    /// Storage key (bucket "signing-docflow") of the signed PDF produced by signer N-1. Required when
    /// <see cref="SignerSequenceNumber"/> &gt; 1; must reside within the caller's tenant prefix.
    /// Null for sequence 1 (first signer signs the original, unsigned PDF).
    /// </summary>
    string? PreviousSignedStorageKey = null) : ICommand<InitiateDocFlowResultDto>;

// ─── Result DTO ───────────────────────────────────────────────────────────────

/// <summary>
/// Result returned by <see cref="InitiateDocFlowCommand"/>.
/// FE passes HandlerUrl and Jwt directly into the VGCA plugin function call.
/// </summary>
public sealed record InitiateDocFlowResultDto(
    /// <summary>Full URL for VGCA service to GET/POST: "{BaseUrl}/api/signing/docflow/file".</summary>
    string HandlerUrl,
    /// <summary>Short-lived HS256 JWT (5-min TTL) — passed as JWTToken in plugin params.</summary>
    string Jwt,
    /// <summary>Document identifier echoed back — pass as FileUploadHandler docId param.</summary>
    Guid DocId,
    /// <summary>PK of the created <see cref="SigningDocFlowRecord"/> — used by the unified orchestrator to link
    /// a durable <c>SigningRequest</c> to this round-trip. Null only if initiation created no record.</summary>
    long? RecordId = null);

// ─── Validator ────────────────────────────────────────────────────────────────

public sealed class InitiateDocFlowCommandValidator : AbstractValidator<InitiateDocFlowCommand>
{
    public InitiateDocFlowCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.DocId).NotEmpty();
        // OriginalStorageKey is FE-supplied and feeds a tenant-scoped storage read on the
        // [AllowAnonymous] download round-trip. Pin it to the caller's tenant prefix so a tenant
        // cannot initiate signing that names another tenant's object key (cross-tenant read).
        RuleFor(x => x.OriginalStorageKey)
            .NotEmpty()
            .MaximumLength(512)
            .Must((cmd, key) => IsWithinTenantPrefix(key, cmd.TenantId))
            .WithMessage("OriginalStorageKey must be within the caller's tenant prefix ('<tenantId>/...').");
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.BaseUrl).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Metadata).NotNull();
        RuleFor(x => x.Metadata.DocNumber).NotEmpty().MaximumLength(128)
            .When(x => x.Metadata is not null);

        RuleFor(x => x.SignerSequenceNumber)
            .GreaterThanOrEqualTo(1)
            .WithMessage("SignerSequenceNumber must be >= 1.");

        // Incremental chaining invariant: sequence > 1 requires a previous-signer key that is within
        // the same tenant prefix. Sequence 1 (first signer) must NOT supply a previous key — the server
        // derives whether this is the first signature from the sequence number, never trusts the caller.
        RuleFor(x => x.PreviousSignedStorageKey)
            .NotEmpty()
            .MaximumLength(512)
            .Must((cmd, key) => IsWithinTenantPrefix(key, cmd.TenantId))
            .WithMessage("PreviousSignedStorageKey must be within the caller's tenant prefix ('<tenantId>/...').")
            .When(x => x.SignerSequenceNumber > 1);

        RuleFor(x => x.PreviousSignedStorageKey)
            .Empty()
            .WithMessage("PreviousSignedStorageKey must be null for the first signer (SignerSequenceNumber == 1).")
            .When(x => x.SignerSequenceNumber == 1);
    }

    // Storage keys for signing artifacts follow "{tenantId}/..." (see SigningDocFlowRecord.SignedStorageKey
    // and the FileUploadHandler signed-key builder). Both the original key and any previous-signed key
    // must sit under the caller's tenant prefix to prevent cross-tenant storage reads.
    internal static bool IsWithinTenantPrefix(string? key, Guid tenantId)
        => !string.IsNullOrWhiteSpace(key)
           && key.StartsWith(tenantId.ToString() + "/", StringComparison.OrdinalIgnoreCase);
}

// ─── Handler ──────────────────────────────────────────────────────────────────

public sealed class InitiateDocFlowCommandHandler(
    IDocumentSigningWorkflow workflow,
    IAuditLogger auditLogger)
    : IRequestHandler<InitiateDocFlowCommand, Result<InitiateDocFlowResultDto>>
{
    public async Task<Result<InitiateDocFlowResultDto>> Handle(
        InitiateDocFlowCommand cmd, CancellationToken ct)
    {
        // Inject runtime context into metadata.Extra so DocumentSigningWorkflow can access it
        // without exposing TenantId/UserId on IDocumentSigningWorkflow (which is domain-level).
        var extra = new Dictionary<string, string>(
            cmd.Metadata.Extra ?? new Dictionary<string, string>())
        {
            ["originalStorageKey"]  = cmd.OriginalStorageKey,
            ["initiatedBy"]         = cmd.UserId.ToString(),
            ["tenantId"]            = cmd.TenantId.ToString(),
            ["signerSequenceNumber"] = cmd.SignerSequenceNumber.ToString(),
        };

        // Thread the previous-signed key for incremental chaining (present only for sequence > 1).
        if (!string.IsNullOrEmpty(cmd.PreviousSignedStorageKey))
            extra["previousSignedStorageKey"] = cmd.PreviousSignedStorageKey;

        var enrichedMetadata = cmd.Metadata with { Extra = extra };

        // Delegate to DocumentSigningWorkflow — one of 7 sign-type methods
        var result = cmd.SignType switch
        {
            DocFlowSignType.Issued   => await workflow.SignIssuedAsync(cmd.DocId, enrichedMetadata, ct),
            DocFlowSignType.Approved => await workflow.SignApprovedAsync(cmd.DocId, enrichedMetadata, ct),
            DocFlowSignType.Income   => await workflow.SignIncomeAsync(cmd.DocId, enrichedMetadata, ct),
            DocFlowSignType.Comment  => await workflow.CommentAsync(cmd.DocId, enrichedMetadata, ct),
            DocFlowSignType.Appendix => await workflow.SignAppendixAsync(cmd.DocId, enrichedMetadata, ct),
            DocFlowSignType.Copy     => await workflow.SignCopyAsync(cmd.DocId, enrichedMetadata, ct),
            DocFlowSignType.Files    => await workflow.SignFilesAsync(
                                           [cmd.DocId], enrichedMetadata, ct),
            _ => new DocFlowResult(false, null, null, null,
                     $"Unsupported sign type: {cmd.SignType}"),
        };

        if (!result.IsSuccess)
            return Result.Fail(new BadRequestError(
                result.FailureReason ?? "DocFlow initiation failed.", "DOCFLOW_INITIATE_FAILED"));

        // DocumentSigningWorkflow returns JWT in DocumentNumber, relative path in DocumentDate
        var jwt        = result.DocumentNumber!;
        var handlerUrl = $"{cmd.BaseUrl.TrimEnd('/')}{result.DocumentDate}";

        var snapshot = JsonSerializer.Serialize(new
        {
            docId      = cmd.DocId,
            signType   = cmd.SignType.ToString(),
            userId     = cmd.UserId,
            tenantId   = cmd.TenantId,
        });

        await auditLogger.LogCommandAsync(
            action:       "DOCFLOW_INITIATED",
            moduleName:   "Identity",
            resourceType: "DocFlowSigningSession",
            resourceId:   cmd.DocId.ToString(),
            commandType:  typeof(InitiateDocFlowCommand),
            dataSnapshot: snapshot,
            cancellationToken: ct);

        return Result.Ok(new InitiateDocFlowResultDto(
            HandlerUrl: handlerUrl,
            Jwt:        jwt,
            DocId:      cmd.DocId,
            RecordId:   result.RecordId));
    }
}
