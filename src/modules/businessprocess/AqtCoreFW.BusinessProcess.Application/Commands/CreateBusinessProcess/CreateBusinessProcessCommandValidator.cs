using FluentValidation;

namespace AqtCoreFW.BusinessProcess.Application.Commands.CreateBusinessProcess;

/// <summary>
/// Boundary validator — guards code/name shape before the aggregate factory runs. Defense-in-depth:
/// the factory also throws on whitespace, but pipeline rejection returns a clean 422 instead of 500.
/// XOR-pairing (intakeFormTemplateId XOR intakeFormTemplateVersion) is enforced by the aggregate (GOV_BP_010)
/// to keep a single source of truth — duplicating it here would risk drift.
/// </summary>
public sealed class CreateBusinessProcessCommandValidator : AbstractValidator<CreateBusinessProcessCommand>
{
    public CreateBusinessProcessCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Code)
            .NotEmpty()
            .MaximumLength(64)
            .Matches("^[A-Z0-9_-]+$").WithMessage("Code phải viết hoa, chỉ chứa chữ/số/dấu gạch.");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(256);
        RuleFor(x => x.Description).MaximumLength(2048);
        RuleFor(x => x.WorkflowDefinitionPublicId).NotEmpty();
        RuleFor(x => x.IntakeFormTemplateVersion)
            .GreaterThan(0)
            .When(x => x.IntakeFormTemplateVersion.HasValue);
        RuleFor(x => x.CreatedBy).NotEmpty();
    }
}
