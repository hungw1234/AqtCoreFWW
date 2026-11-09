using FluentValidation;

namespace AqtCoreFW.Collaboration.Application.Commands.UpdatePresence;

public sealed class UpdatePresenceCommandValidator : AbstractValidator<UpdatePresenceCommand>
{
    public UpdatePresenceCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.Status).NotEmpty().MaximumLength(50);
    }
}
