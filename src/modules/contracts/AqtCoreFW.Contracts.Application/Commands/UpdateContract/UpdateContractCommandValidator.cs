using FluentValidation;

namespace AqtCoreFW.Contracts.Application.Commands.UpdateContract;

public sealed class UpdateContractCommandValidator : AbstractValidator<UpdateContractCommand>
{
    public UpdateContractCommandValidator()
    {
        RuleFor(command => command.Id)
            .GreaterThan(0).WithMessage("Mã hợp đồng không hợp lệ.");
        RuleFor(command => command.TenantId)
            .NotEmpty().WithMessage("Đơn vị không được để trống.");
        RuleFor(command => command.ContractNumber)
            .NotEmpty().WithMessage("Số hợp đồng không được để trống.")
            .MaximumLength(50).WithMessage("Số hợp đồng không được vượt quá 50 ký tự.");
        RuleFor(command => command.Title)
            .NotEmpty().WithMessage("Tên hợp đồng không được để trống.")
            .MaximumLength(200).WithMessage("Tên hợp đồng không được vượt quá 200 ký tự.");
        RuleFor(command => command.CounterpartyName)
            .NotEmpty().WithMessage("Tên đối tác không được để trống.")
            .MaximumLength(200).WithMessage("Tên đối tác không được vượt quá 200 ký tự.");
        RuleFor(command => command.EndDate)
            .GreaterThanOrEqualTo(command => command.StartDate)
            .WithMessage("Ngày kết thúc phải bằng hoặc sau ngày bắt đầu.");
        RuleFor(command => command.Status)
            .IsInEnum().WithMessage("Trạng thái hợp đồng không hợp lệ.");
        RuleFor(command => command.Description)
            .MaximumLength(2000).WithMessage("Mô tả không được vượt quá 2000 ký tự.");
        RuleFor(command => command.UpdatedBy)
            .NotEmpty().WithMessage("Người cập nhật không được để trống.");
    }
}
