using FluentValidation;

namespace AqtCoreFW.Identity.Application.Signing.Profiles;

/// <summary>
/// Validates the self-service CCCD set. A Vietnamese CCCD is exactly 12 digits — same rule the admin
/// update path enforces (UpdateUserCommandValidator), so the two write paths cannot diverge on format.
/// </summary>
public sealed class SetMySigningNationalIdCommandValidator : AbstractValidator<SetMySigningNationalIdCommand>
{
    public SetMySigningNationalIdCommandValidator()
    {
        RuleFor(x => x.NationalId)
            .NotEmpty()
            .Matches(@"^\d{12}$")
            .WithMessage("CCCD phải gồm đúng 12 chữ số.");
    }
}
