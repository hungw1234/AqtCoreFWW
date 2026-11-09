using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.ManageAbacRule;

public sealed class CreateAbacRuleCommandValidator : AbstractValidator<CreateAbacRuleCommand>
{
    public CreateAbacRuleCommandValidator()
    {
        RuleFor(x => x.Resource).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Action).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AttributeKey).NotEmpty().MaximumLength(100);
        RuleFor(x => x.AttributeValue).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Effect).NotEmpty().Must(e =>
            e.Equals("Allow", StringComparison.OrdinalIgnoreCase) ||
            e.Equals("Deny", StringComparison.OrdinalIgnoreCase))
            .WithMessage("Effect must be 'Allow' or 'Deny'");
    }
}
