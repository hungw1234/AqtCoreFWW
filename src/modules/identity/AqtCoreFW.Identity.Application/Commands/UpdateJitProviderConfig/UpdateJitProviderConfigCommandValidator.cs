using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.UpdateJitProviderConfig;

public sealed class UpdateJitProviderConfigCommandValidator : AbstractValidator<UpdateJitProviderConfigCommand>
{
    public UpdateJitProviderConfigCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.DisplayName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.DefaultRoleName).NotEmpty().MaximumLength(100);
        RuleFor(x => x.MaxProvisionsPerHour).GreaterThanOrEqualTo(0);
        RuleFor(x => x.ActorId).NotEmpty();
    }
}
