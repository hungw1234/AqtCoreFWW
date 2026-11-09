using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.UpdateUser;

public sealed class UpdateUserCommandValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);

        // Optional signing national ID (CCCD) — when supplied must be exactly 12 digits.
        When(x => !string.IsNullOrEmpty(x.SigningNationalId), () =>
            RuleFor(x => x.SigningNationalId)
                .Matches(@"^\d{12}$")
                .WithMessage("Signing national ID (CCCD) must be exactly 12 digits."));
    }
}
