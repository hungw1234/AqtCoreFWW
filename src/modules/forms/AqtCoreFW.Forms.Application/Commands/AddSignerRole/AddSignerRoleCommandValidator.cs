using System.Text.Json;
using FluentValidation;

namespace AqtCoreFW.Forms.Application.Commands.AddSignerRole;

public sealed class AddSignerRoleCommandValidator : AbstractValidator<AddSignerRoleCommand>
{
    public AddSignerRoleCommandValidator()
    {
        RuleFor(x => x.TemplatePublicId)
            .NotEmpty().WithMessage("TemplatePublicId không được để trống.");

        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId không được để trống.");

        RuleFor(x => x.RoleKey)
            .NotEmpty().WithMessage("RoleKey không được để trống.")
            .MaximumLength(128).WithMessage("RoleKey không được vượt quá 128 ký tự.");

        RuleFor(x => x.RoleLabelVi)
            .NotEmpty().WithMessage("Nhãn vai trò (tiếng Việt) không được để trống.")
            .MaximumLength(200).WithMessage("Nhãn vai trò không được vượt quá 200 ký tự.");

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Thứ tự hiển thị phải >= 0.");

        RuleFor(x => x.UpdatedBy)
            .NotEmpty().WithMessage("UpdatedBy không được để trống.");

        // PlacementJson is optional; when provided it must be well-formed JSON
        RuleFor(x => x.PlacementJson)
            .MaximumLength(4000).WithMessage("PlacementJson không được vượt quá 4000 ký tự.")
            .Must(BeValidJson).WithMessage("PlacementJson phải là JSON hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.PlacementJson));
    }

    private static bool BeValidJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return true;
        try
        {
            using var _ = JsonDocument.Parse(json);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
