using FluentValidation;

namespace AqtCoreFW.MasterData.Application.Commands.CreateDictionary;

public sealed class CreateDictionaryCommandValidator : AbstractValidator<CreateDictionaryCommand>
{
    public CreateDictionaryCommandValidator()
    {
        RuleFor(x => x.Code).NotEmpty().MaximumLength(100)
            .Matches(@"^[A-Z0-9_]+$").WithMessage("Code must be uppercase alphanumeric with underscores only");
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameVi).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.ActorId).NotEmpty();
    }
}
