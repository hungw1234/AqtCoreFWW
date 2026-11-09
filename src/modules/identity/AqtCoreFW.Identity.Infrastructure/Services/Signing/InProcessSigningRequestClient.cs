using AqtCoreFW.Identity.Application.Signing;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;

namespace AqtCoreFW.Identity.Infrastructure.Services.Signing;

/// <summary>
/// In-process implementation of <see cref="ISigningRequestClient"/> — a thin façade over
/// <see cref="ISignatureOrchestrator.RequestSignatureAsync"/>. Registered in Identity DI so Forms
/// (and any other module) can initiate a signing request without a compile-time reference to
/// Identity.Application or Identity.Infrastructure.
///
/// <para>Contracts cannot reference Identity.Application, so this impl maps between the
/// Contracts-owned DTOs (<see cref="SigningRequestClientInput"/> / <see cref="SigningRequestOutcome"/>)
/// and the Application types (<see cref="RequestSignatureInput"/> / <see cref="RequestSignatureResult"/>).
/// Enum values that live in Identity.Domain (<c>SigningArtifactKind</c>, <c>DocFlowSignType</c>) are
/// carried on the contract surface as strings and parsed here.</para>
///
/// <para>Orchestrator failures (bad route, unknown provider, sequence predecessor not completed)
/// are preserved as typed error codes on <see cref="SigningRequestOutcome"/> so callers can map them
/// to 400/422/404 without catching exceptions or referencing FluentResults.</para>
/// </summary>
public sealed class InProcessSigningRequestClient(
    ISignatureOrchestrator orchestrator,
    SigningStepQueryService stepQueryService)
    : ISigningRequestClient
{
    public Task<IReadOnlyList<SigningStepDto>> GetStepsForFileAsync(
        Guid tenantId, Guid sourceFileId, CancellationToken ct = default)
        => stepQueryService.GetStepsForFileAsync(tenantId, sourceFileId, ct);

    public Task<IReadOnlyList<SigningStepDto>> GetStepsForRequestPublicIdAsync(
        Guid tenantId, Guid requestPublicId, CancellationToken ct = default)
        => stepQueryService.GetStepsForRequestPublicIdAsync(tenantId, requestPublicId, ct);

    public Task<SignedStepResolution> ResolveSignedStepForFileAsync(
        Guid tenantId, Guid sourceFileId, int sequenceNumber, CancellationToken ct = default)
        => stepQueryService.ResolveSignedStepForFileAsync(tenantId, sourceFileId, sequenceNumber, ct);

    public Task<SignedStepResolution> ResolveSignedStepForFileByRequestAsync(
        Guid tenantId, Guid sourceFileId, Guid requestPublicId, CancellationToken ct = default)
        => stepQueryService.ResolveSignedStepForFileByRequestAsync(tenantId, sourceFileId, requestPublicId, ct);

    public Task<SignedStepResolution> ResolveSignedStepForRequestAsync(
        Guid tenantId, Guid requestPublicId, int sequenceNumber, CancellationToken ct = default)
        => stepQueryService.ResolveSignedStepForRequestAsync(tenantId, requestPublicId, sequenceNumber, ct);

    public Task<IReadOnlyCollection<Guid>> GetSignedFileIdsAsync(
        Guid tenantId, IReadOnlyCollection<Guid> sourceFileIds, CancellationToken ct = default)
        => stepQueryService.GetSignedFileIdsAsync(tenantId, sourceFileIds, ct);

    public async Task<SigningRequestOutcome> RequestSignatureAsync(
        SigningRequestClientInput input,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);

        // --- Parse enums from string (keeps Contracts free of Identity.Domain references) ---
        if (!TryParseArtifactKind(input.ArtifactKind, out var artifactKind))
            return SigningRequestOutcome.Fail(
                "SIGNING_INVALID_ARTIFACT_KIND",
                $"Unknown ArtifactKind '{input.ArtifactKind}'. Expected one of: DetachedData, FormPdf, UploadedPdf.");

        DocFlowSignParameters? docFlow = null;
        if (input.DocFlow is not null)
        {
            if (!TryParseSignType(input.DocFlow.SignType, out var signType))
                return SigningRequestOutcome.Fail(
                    "SIGNING_INVALID_SIGN_TYPE",
                    $"Unknown DocFlow SignType '{input.DocFlow.SignType}'. " +
                    "Expected one of: Issued, Approved, Income, Comment, Appendix, Copy, Files.");

            docFlow = new DocFlowSignParameters(
                SignType:                  signType,
                OriginalStorageKey:        input.DocFlow.OriginalStorageKey,
                DocNumber:                 input.DocFlow.DocNumber,
                IssuedDate:                input.DocFlow.IssuedDate,
                BaseUrl:                   input.DocFlow.BaseUrl,
                FileName:                  input.DocFlow.FileName,
                Extra:                     input.DocFlow.Extra,
                PreviousSignedStorageKey:  input.DocFlow.PreviousSignedStorageKey,
                SignerSequenceNumber:      input.SignerSequenceNumber);
        }

        var orchestratorInput = new RequestSignatureInput(
            TenantId:            input.TenantId,
            ArtifactKind:        artifactKind,
            ArtifactRef:         input.ArtifactRef,
            ProviderKey:         input.ProviderKey,
            AssignedSignerId:    input.AssignedSignerId,
            InitiatedBy:         input.InitiatedBy,
            Placement:           input.Placement,
            DocFlow:             docFlow,
            SignerSequenceNumber: input.SignerSequenceNumber,
            SourceFileId:        input.SourceFileId);

        var result = await orchestrator.RequestSignatureAsync(orchestratorInput, ct);

        if (result.IsFailed)
            return MapFailure(result.Errors);

        return SigningRequestOutcome.Success(result.Value.PublicId, result.Value.ClientHandoff);
    }

    /// <summary>
    /// Maps FluentResults error list to a Contracts-layer outcome, preserving the error type so
    /// the Forms command handler can propagate 400/422/404 instead of a generic 500. The error
    /// code is extracted from <c>IError.Metadata["ErrorCode"]</c> when present (the pattern used
    /// by <see cref="BadRequestError"/> and <see cref="NotFoundError"/>); a generic sentinel code
    /// is used for error types that carry no ErrorCode metadata (<see cref="ConflictError"/> etc.).
    /// </summary>
    private static SigningRequestOutcome MapFailure(IReadOnlyCollection<IError> errors)
    {
        // Preserve the most specific typed error; order matches ApiControllerBase.ToApiResponse priority.
        var error = errors.FirstOrDefault() ?? new Error("Unknown signing failure.");
        var message = string.Join("; ", errors.Select(e => e.Message));

        // Extract the typed error code from FluentResults metadata when available.
        var metadataCode = error.Metadata.TryGetValue("ErrorCode", out var code)
            ? code?.ToString()
            : null;

        // Derive a sentinel error code from the CLR error type when no metadata code is set,
        // so callers can still distinguish error categories without referencing FluentResults.
        var errorCode = metadataCode ?? error switch
        {
            NotFoundError          => "SIGNING_NOT_FOUND",
            ForbiddenError         => "SIGNING_FORBIDDEN",
            ConflictError          => "SIGNING_CONFLICT",
            ValidationError        => "SIGNING_VALIDATION_FAILED",
            BadRequestError        => "SIGNING_BAD_REQUEST",
            PreconditionFailedError => "SIGNING_PRECONDITION_FAILED",
            _                      => "SIGNING_FAILED",
        };

        return SigningRequestOutcome.Fail(errorCode, message);
    }

    private static bool TryParseArtifactKind(string? value, out SigningArtifactKind kind)
    {
        switch (value?.Trim())
        {
            case "DetachedData":  kind = SigningArtifactKind.DetachedData; return true;
            case "FormPdf":       kind = SigningArtifactKind.FormPdf;      return true;
            case "UploadedPdf":   kind = SigningArtifactKind.UploadedPdf;  return true;
            default:              kind = default;                           return false;
        }
    }

    private static bool TryParseSignType(string? value, out DocFlowSignType signType)
    {
        switch (value?.Trim())
        {
            case "Issued":   signType = DocFlowSignType.Issued;   return true;
            case "Approved": signType = DocFlowSignType.Approved; return true;
            case "Income":   signType = DocFlowSignType.Income;   return true;
            case "Comment":  signType = DocFlowSignType.Comment;  return true;
            case "Appendix": signType = DocFlowSignType.Appendix; return true;
            case "Copy":     signType = DocFlowSignType.Copy;     return true;
            case "Files":    signType = DocFlowSignType.Files;    return true;
            default:         signType = default;                  return false;
        }
    }
}
