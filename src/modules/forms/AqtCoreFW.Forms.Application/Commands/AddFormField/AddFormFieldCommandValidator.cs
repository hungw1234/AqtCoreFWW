using System.Text.Json;
using AqtCoreFW.Forms.Domain.Enums;
using FluentValidation;

namespace AqtCoreFW.Forms.Application.Commands.AddFormField;

public sealed class AddFormFieldCommandValidator : AbstractValidator<AddFormFieldCommand>
{
    private static readonly HashSet<FormFieldType> ReferenceTypes =
    [
        FormFieldType.EnumRef,
        FormFieldType.InternalRef,
        FormFieldType.ExternalRef
    ];

    public AddFormFieldCommandValidator()
    {
        RuleFor(x => x.TemplatePublicId)
            .NotEmpty().WithMessage("TemplatePublicId không được để trống.");

        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId không được để trống.");

        RuleFor(x => x.FieldKey)
            .NotEmpty().WithMessage("FieldKey không được để trống.")
            .MaximumLength(128).WithMessage("FieldKey không được vượt quá 128 ký tự.")
            .Matches(@"^[a-zA-Z_][a-zA-Z0-9_]{0,127}$")
            .WithMessage("FieldKey phải bắt đầu bằng chữ cái hoặc '_', chỉ chứa chữ cái, số, '_'.");

        RuleFor(x => x.Type)
            .IsInEnum().WithMessage("Loại field không hợp lệ.");

        RuleFor(x => x.LabelVi)
            .NotEmpty().WithMessage("Nhãn tiếng Việt không được để trống.")
            .MaximumLength(500).WithMessage("Nhãn tiếng Việt không được vượt quá 500 ký tự.");

        RuleFor(x => x.LabelEn)
            .NotEmpty().WithMessage("Nhãn tiếng Anh không được để trống.")
            .MaximumLength(500).WithMessage("Nhãn tiếng Anh không được vượt quá 500 ký tự.");

        RuleFor(x => x.DisplayOrder)
            .GreaterThanOrEqualTo(0).WithMessage("Thứ tự hiển thị phải >= 0.");

        RuleFor(x => x.UpdatedBy)
            .NotEmpty().WithMessage("UpdatedBy không được để trống.");

        // DataSourceJson required for reference types
        RuleFor(x => x.DataSourceJson)
            .NotEmpty()
            .WithMessage("DataSourceJson là bắt buộc cho loại field tham chiếu (EnumRef/InternalRef/ExternalRef).")
            .When(x => ReferenceTypes.Contains(x.Type));

        // FormulaJson required for Formula type
        RuleFor(x => x.FormulaJson)
            .NotEmpty()
            .WithMessage("FormulaJson là bắt buộc cho loại field Formula.")
            .When(x => x.Type == FormFieldType.Formula);

        // BP MVP P6-FU1: PrefillSourceJson — cap to DB column size + JSON well-formedness.
        // Envelope-shape validation (Guid + SourceFieldKey) intentionally deferred to PrefillSourceMeta.TryParse
        // at resolve-time (BusinessProcess.Application module): keeps Forms validator independent of envelope evolution
        // and matches the partial-fail-tolerance contract (resolver logs+skips bad pointers, never throws).
        RuleFor(x => x.PrefillSourceJson)
            .MaximumLength(2000).WithMessage("PrefillSourceJson không được vượt quá 2000 ký tự.")
            .Must(BeValidJson).WithMessage("PrefillSourceJson phải là JSON hợp lệ.")
            .When(x => !string.IsNullOrWhiteSpace(x.PrefillSourceJson));
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
