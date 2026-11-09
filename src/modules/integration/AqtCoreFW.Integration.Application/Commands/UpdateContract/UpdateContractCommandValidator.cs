using FluentValidation;

namespace AqtCoreFW.Integration.Application.Commands.UpdateContract;

public sealed class UpdateContractCommandValidator : AbstractValidator<UpdateContractCommand>
{
    public UpdateContractCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Title).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Description).MaximumLength(2000);
        RuleFor(x => x.EffectiveDate).NotEmpty();
        RuleFor(x => x.ExpiryDate).GreaterThan(x => x.EffectiveDate).When(x => x.ExpiryDate.HasValue);
    }
}
