using FluentValidation;

namespace AqtCoreFW.Integration.Application.Commands.CreateContract;

public sealed class CreateContractCommandValidator : AbstractValidator<CreateContractCommand>
{
    public CreateContractCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.PartnerPublicId).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.EffectiveDate).NotEmpty();
        RuleFor(x => x.ExpiryDate).GreaterThan(x => x.EffectiveDate).When(x => x.ExpiryDate.HasValue);
    }
}
