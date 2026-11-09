using FluentValidation;

namespace AqtCoreFW.Audit.Application.Commands.EvaluateCompliancePolicy;

public sealed class EvaluateCompliancePolicyCommandValidator : AbstractValidator<EvaluateCompliancePolicyCommand>
{
    public EvaluateCompliancePolicyCommandValidator()
    {
        RuleFor(x => x.PolicyId).NotEmpty();
        RuleFor(x => x.EntityType).NotEmpty().MaximumLength(256);
        RuleFor(x => x.EntityId).NotEmpty();
    }
}
