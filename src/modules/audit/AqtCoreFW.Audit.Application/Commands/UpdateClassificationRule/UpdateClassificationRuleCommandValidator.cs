using FluentValidation;

namespace AqtCoreFW.Audit.Application.Commands.UpdateClassificationRule;

public sealed class UpdateClassificationRuleCommandValidator
    : AbstractValidator<UpdateClassificationRuleCommand>
{
    public UpdateClassificationRuleCommandValidator()
    {
        RuleFor(x => x.Level).IsInEnum();

        RuleFor(x => x.MaxRetentionDays)
            .GreaterThan(0)
            .When(x => x.MaxRetentionDays.HasValue)
            .WithMessage("MaxRetentionDays must be greater than 0 when specified.");

        RuleFor(x => x.RowVersion)
            .NotEmpty()
            .WithMessage("RowVersion is required for concurrency control.");
    }
}
