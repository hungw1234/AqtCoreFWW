using FluentValidation;

namespace AqtCoreFW.Contracts.Application.Commands.DeleteContract;

public sealed class DeleteContractCommandValidator : AbstractValidator<DeleteContractCommand>
{
    public DeleteContractCommandValidator()
    {
        RuleFor(command => command.Id)
            .GreaterThan(0).WithMessage("Mã hợp đồng không hợp lệ.");
        RuleFor(command => command.TenantId)
            .NotEmpty().WithMessage("Đơn vị không được để trống.");
    }
}
