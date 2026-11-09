using FluentValidation;

namespace AqtCoreFW.MasterData.Application.Commands.UpdateDictionary;

public sealed class UpdateDictionaryCommandValidator : AbstractValidator<UpdateDictionaryCommand>
{
    public UpdateDictionaryCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);
        RuleFor(x => x.NameVi).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Description).MaximumLength(1000);
        RuleFor(x => x.ActorId).NotEmpty();
    }
}
