using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.ChangeTenantStatus;

public sealed class ChangeTenantStatusCommandValidator : AbstractValidator<ChangeTenantStatusCommand>
{
    public ChangeTenantStatusCommandValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId không được để trống.");

        RuleFor(x => x.NewStatus)
            .IsInEnum().WithMessage("Trạng thái không hợp lệ.");

        RuleFor(x => x.ActorId)
            .NotEmpty().WithMessage("ActorId không được để trống.");
    }
}
