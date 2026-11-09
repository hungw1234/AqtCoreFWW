using FluentValidation;

namespace AqtCoreFW.Workflow.Application.Commands.UpdateDelegationRule;

public sealed class UpdateDelegationRuleCommandValidator : AbstractValidator<UpdateDelegationRuleCommand>
{
    public UpdateDelegationRuleCommandValidator()
    {
        RuleFor(x => x.RuleId).NotEmpty();
        RuleFor(x => x.DelegateUserId).NotEmpty();
        RuleFor(x => x.ValidFrom).NotEmpty();
        RuleFor(x => x.ValidUntil).GreaterThan(x => x.ValidFrom)
            .WithMessage("ValidUntil must be after ValidFrom.");
        RuleFor(x => x.ModifiedBy).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
    }
}
