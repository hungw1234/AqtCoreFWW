using FluentValidation;

namespace AqtCoreFW.BusinessProcess.Application.Commands.UpdateBusinessProcess;

/// <summary>
/// Boundary validator — Name/Description are optional patches but if supplied must be within length limits.
/// Name=null (don't touch) ≠ Name="" (also no-op per aggregate semantics): blank strings are rejected here
/// to avoid a confusing "I sent empty but nothing changed" round-trip.
/// </summary>
public sealed class UpdateBusinessProcessCommandValidator : AbstractValidator<UpdateBusinessProcessCommand>
{
    public UpdateBusinessProcessCommandValidator()
    {
        RuleFor(x => x.TenantId).NotEmpty();
        RuleFor(x => x.BusinessProcessPublicId).NotEmpty();
        RuleFor(x => x.ModifiedBy).NotEmpty();
        RuleFor(x => x.Name)
            .NotEmpty().When(x => x.Name is not null)
            .MaximumLength(256);
        RuleFor(x => x.Description).MaximumLength(2048);
    }
}
