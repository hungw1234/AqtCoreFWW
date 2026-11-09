using System.Text.Json;
using FluentValidation;

namespace AqtCoreFW.Forms.Application.Commands.UpdateFormField;

public sealed class UpdateFormFieldCommandValidator : AbstractValidator<UpdateFormFieldCommand>
{
    public UpdateFormFieldCommandValidator()
    {
        RuleFor(x => x.TemplatePublicId)
            .NotEmpty().WithMessage("TemplatePublicId không được để trống.");

        RuleFor(x => x.FieldId)
            .NotEmpty().WithMessage("FieldId không được để trống.");

        RuleFor(x => x.TenantId)
            .NotEmpty().WithMessage("TenantId không được để trống.");

        RuleFor(x => x.UpdatedBy)
            .NotEmpty().WithMessage("UpdatedBy không được để trống.");

        RuleFor(x => x.LabelVi)
            .MaximumLength(500).WithMessage("Nhãn tiếng Việt không được vượt quá 500 ký tự.")
            .When(x => x.LabelVi is not null);

        RuleFor(x => x.LabelEn)
            .MaximumLength(500).WithMessage("Nhãn tiếng Anh không được vượt quá 500 ký tự.")
            .When(x => x.LabelEn is not null);

        // BP MVP P6-FU1: PrefillSourceJson — cap to DB column size + JSON well-formedness.
        // Semantics: null = no change (skip validation); empty/whitespace = clear pointer (skip JSON check);
        // non-empty = must be ≤2000 chars + parseable JSON. Envelope shape (Guid + SourceFieldKey)
        // is enforced at resolve-time by PrefillSourceMeta.TryParse in BusinessProcess.Application.
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
