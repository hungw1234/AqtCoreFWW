using FluentValidation;

namespace AqtCoreFW.Identity.Application.Commands.CreateTenant;

public sealed class CreateTenantCommandValidator : AbstractValidator<CreateTenantCommand>
{
    public CreateTenantCommandValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Tên tenant không được để trống.")
            .MaximumLength(200).WithMessage("Tên tenant tối đa 200 ký tự.");

        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Mã tenant không được để trống.")
            .MaximumLength(50).WithMessage("Mã tenant tối đa 50 ký tự.")
            .Matches(@"^[A-Z0-9_-]+$").WithMessage("Mã tenant chỉ chứa chữ hoa, số, gạch ngang và gạch dưới.");

        RuleFor(x => x.ContactEmail)
            .MaximumLength(200).WithMessage("Email liên hệ tối đa 200 ký tự.")
            .EmailAddress().When(x => !string.IsNullOrEmpty(x.ContactEmail))
            .WithMessage("Email liên hệ không đúng định dạng.");

        RuleFor(x => x.ActorId)
            .NotEmpty().WithMessage("ActorId không được để trống.");
    }
}
