using FluentValidation;

namespace AqtCoreFW.MasterData.Application.Commands.CreateExternalMapping;

public sealed class CreateExternalMappingCommandValidator : AbstractValidator<CreateExternalMappingCommand>
{
    public CreateExternalMappingCommandValidator()
    {
        RuleFor(x => x.InternalCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExternalSystem).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExternalCode).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ValidFrom).NotEmpty();
        RuleFor(x => x.ValidTo)
            .GreaterThan(x => x.ValidFrom)
            .When(x => x.ValidTo.HasValue)
            .WithMessage("ValidTo must be after ValidFrom");
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();
    }
}
