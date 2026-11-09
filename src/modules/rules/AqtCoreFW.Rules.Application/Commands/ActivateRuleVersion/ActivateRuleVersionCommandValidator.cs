using FluentValidation;

namespace AqtCoreFW.Rules.Application.Commands.ActivateRuleVersion;

public sealed class ActivateRuleVersionCommandValidator : AbstractValidator<ActivateRuleVersionCommand>
{
    public ActivateRuleVersionCommandValidator()
    {
        RuleFor(x => x.RuleSetId).NotEmpty();
    }
}
