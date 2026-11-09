using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.RegisterUser;

public sealed class RegisterUserCommandValidator : AbstractValidator<RegisterUserCommand>
{
    public RegisterUserCommandValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(254);
        // Min 12 chars, upper+lower+digit+special per QĐ742
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(12)
            .Matches(@"[A-Z]").WithMessage("Password must contain uppercase letter")
            .Matches(@"[a-z]").WithMessage("Password must contain lowercase letter")
            .Matches(@"[0-9]").WithMessage("Password must contain digit")
            .Matches(@"[^a-zA-Z0-9]").WithMessage("Password must contain special character");

        // Optional signing national ID (CCCD) — when supplied must be exactly 12 digits.
        When(x => !string.IsNullOrEmpty(x.SigningNationalId), () =>
            RuleFor(x => x.SigningNationalId)
                .Matches(@"^\d{12}$")
                .WithMessage("Signing national ID (CCCD) must be exactly 12 digits."));
    }
}
