using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.UpdateTenant;

public sealed class UpdateTenantCommandValidator : AbstractValidator<UpdateTenantCommand>
{
    public UpdateTenantCommandValidator()
    {
        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId không được để trống.");

        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên tenant không được để trống.")
            .MaximumLength(200).WithMessage("Tên tenant tối đa 200 ký tự.");

        RuleFor(x => x.ContactEmail)
            .MaximumLength(200).WithMessage("Email liên hệ tối đa 200 ký tự.")
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.ContactEmail))
            .WithMessage("Email liên hệ không đúng định dạng.");

        RuleFor(x => x.ActorId)
            .NotEmpty().WithMessage("ActorId không được để trống.");
    }
}
