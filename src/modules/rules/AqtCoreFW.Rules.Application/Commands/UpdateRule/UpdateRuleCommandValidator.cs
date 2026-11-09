using FluentValidation;

namespace AqtCoreFW.Rules.Application.Commands.UpdateRule;

public sealed class UpdateRuleCommandValidator : AbstractValidator<UpdateRuleCommand>
{
    public UpdateRuleCommandValidator()
    {
        RuleFor(x => x.RuleSetId).NotEmpty();
        RuleFor(x => x.RuleId).NotEmpty();
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name is not null);
        RuleFor(x => x.Expression).MaximumLength(4000).When(x => x.Expression is not null);
        RuleFor(x => x.Priority).GreaterThanOrEqualTo(0).When(x => x.Priority.HasValue);
    }
}
