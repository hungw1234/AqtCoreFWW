using FluentValidation;

namespace AqtCoreFW.Forms.Application.Commands.SignSubmissionDocument;

/// <summary>
/// Validates inputs before the signing handler runs. Keeps the handler clean of input-shape guards.
///
/// SignType is validated against the exact set of VGCA plugin function names (DocFlowSignType enum
/// values as strings). Rejecting unknown values here avoids a round-trip to the orchestrator that
/// would return SIGNING_INVALID_SIGN_TYPE after expensive PDF generation and bucket upload.
/// </summary>
public sealed class GenerateAndInitiateSubmissionSigningCommandValidator
    : AbstractValidator<GenerateAndInitiateSubmissionSigningCommand>
{
    // VGCA plugin function names that the DocFlow strategy recognises.
    // Mirrors the DocFlowSignType enum values in Identity.Domain (string mirror avoids cross-module ref).
    private static readonly HashSet<string> ValidSignTypes =
    [
        "Issued", "Approved", "Income", "Comment", "Appendix", "Copy", "Files"
    ];

    public GenerateAndInitiateSubmissionSigningCommandValidator()
    {
        RuleFor(x => x.SubmissionPublicId)
            .NotEmpty().WithMessage("SubmissionPublicId không được để trống.");

        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId không được để trống.");

        RuleFor(x => x.InitiatedBy)
            .NotEmpty().WithMessage("InitiatedBy không được để trống.");

        RuleFor(x => x.SignType)
            .NotEmpty().WithMessage("SignType không được để trống.")
            .Must(t => ValidSignTypes.Contains(t))
            .WithMessage(
                $"SignType không hợp lệ. Các giá trị được phép: {string.Join(", ", ValidSignTypes)}.");

        RuleFor(x => x.DocNumber)
            .NotEmpty().WithMessage("Số hiệu văn bản (DocNumber) không được để trống.")
            .MaximumLength(200).WithMessage("Số hiệu văn bản không vượt quá 200 ký tự.");

        RuleFor(x => x.IssuedDate)
            .NotEmpty().WithMessage("Ngày ban hành (IssuedDate) không được để trống.")
            .GreaterThan(DateTime.MinValue).WithMessage("IssuedDate không hợp lệ.");

        RuleFor(x => x.ProviderKey)
            .NotEmpty().WithMessage("ProviderKey không được để trống.")
            .MaximumLength(100).WithMessage("ProviderKey không vượt quá 100 ký tự.");

        // BaseUrl is server-derived by the controller (Scheme + Host) — not from the FE payload.
        // Validated here as defence-in-depth; InitiateDocFlowCommandValidator also enforces this.
        RuleFor(x => x.BaseUrl)
            .NotEmpty().WithMessage("BaseUrl không được để trống.")
            .MaximumLength(256).WithMessage("BaseUrl không vượt quá 256 ký tự.");
    }
}
