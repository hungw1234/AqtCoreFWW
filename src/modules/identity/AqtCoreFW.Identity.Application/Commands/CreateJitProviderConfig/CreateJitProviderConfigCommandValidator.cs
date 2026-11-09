using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.CreateJitProviderConfig;

public sealed class CreateJitProviderConfigCommandValidator : AbstractValidator<CreateJitProviderConfigCommand>
{
    public CreateJitProviderConfigCommandValidator()
    {
        RuleFor(x => x.Scheme).NotEmpty().MaximumLength(100);
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DefaultRoleName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MaxProvisionsPerHour).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ActorId).NotEmpty();
    }
}
