using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.CreateExternalIdentity;

public sealed class CreateExternalIdentityCommandValidator : AbstractValidator<CreateExternalIdentityCommand>
{
    public CreateExternalIdentityCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.ExternalId).NotEmpty().MaximumLength(500);
        RuleFor(x => x.DisplayName).MaximumLength(200);
        RuleFor(x => x.Email).EmailAddress().MaximumLength(254).When(x => x.Email is not null);
        RuleFor(x => x.ActorId).NotEmpty();
    }
}
