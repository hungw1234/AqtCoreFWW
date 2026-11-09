using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.UpdateExternalIdentity;

public sealed class UpdateExternalIdentityCommandValidator : AbstractValidator<UpdateExternalIdentityCommand>
{
    public UpdateExternalIdentityCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.DisplayName).MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(254).When(x => x.Email is not null);
        RuleFor(x => x.ActorId).NotEmpty();
    }
}
