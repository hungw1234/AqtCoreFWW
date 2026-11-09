using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Commands.CreateDelegationRule;

public sealed class CreateDelegationRuleCommandValidator : AbstractValidator<CreateDelegationRuleCommand>
{
    public CreateDelegationRuleCommandValidator()
    {
        RuleFor(x => x.DelegatorUserId).NotEmpty();
        RuleFor(x => x.DelegateUserId).NotEmpty();
        RuleFor(x => x.DelegateUserId).NotEqual(x => x.DelegatorUserId)
            .WithMessage("Delegator and delegate must be different users.");
        RuleFor(x => x.ValidFrom).NotEmpty();
        RuleFor(x => x.ValidUntil).GreaterThan(x => x.ValidFrom)
            .WithMessage("ValidUntil must be after ValidFrom.");
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.CreatedBy).NotEmpty();
    }
}
