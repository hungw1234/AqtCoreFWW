using FluentValidation;

namespace AqtCoreFW.Rules.Application.Commands.CreateRuleSet;

public sealed class CreateRuleSetCommandValidator : AbstractValidator<CreateRuleSetCommand>
{
    public CreateRuleSetCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000).When(x => x.Description is not null);
        RuleFor(x => x.Category).MaximumLength(100).When(x => x.Category is not null);
        RuleFor(x => x.TargetEntityType).MaximumLength(200).When(x => x.TargetEntityType is not null);
    }
}
