using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.Forms.Domain.Entities;

/// <summary>
/// FormFieldOption — static dropdown/radio options for a FormField.
/// Used with EnumRef-style fields (Phase 1: pre-seeded options list).
/// T1 entity: non-generic Entity (long Id DB-assigned).
/// </summary>
public sealed class FormFieldOption : Entity
{
    public long FormFieldId { get; private set; }
    public string Value { get; private set; } = string.Empty;
    public string LabelVi { get; private set; } = string.Empty;
    public string LabelEn { get; private set; } = string.Empty;
    public int DisplayOrder { get; private set; }

    private FormFieldOption() { } // EF Core

    public static FormFieldOption Create(
        long formFieldId,
        string value,
        string labelVi,
        string labelEn,
        int displayOrder)
    {
        return new FormFieldOption
        {
            FormFieldId = formFieldId,
            Value = value,
            LabelVi = labelVi,
            LabelEn = labelEn,
            DisplayOrder = displayOrder
        };
    }
}
