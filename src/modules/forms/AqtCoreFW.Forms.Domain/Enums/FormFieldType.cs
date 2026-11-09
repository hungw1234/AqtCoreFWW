namespace AqtCoreFW.Forms.Domain.Enums;

public enum FormFieldType
{
    // Basic (Phase 1)
    Text = 0,
    Number = 1,
    Date = 2,
    Textarea = 3,
    Boolean = 4,
    File = 5,
    Signature = 6,
    RichText = 7,
    /// <summary>
    /// Digital signature capture field (NĐ68 / QĐ-742 compliant).
    /// The signature itself (cert + sig bytes) is carried in the submission proof bundle,
    /// not stored as a field value — the field declares the signing requirement.
    /// Mode-1: CanonicalJson server-side re-canonicalization (this release).
    /// Mode-2: ServerDocHash (phase 04d — PDF render + hash).
    /// </summary>
    DigitalSignature = 8,

    // Reference (Phase 2)
    EnumRef = 10,
    InternalRef = 11,
    ExternalRef = 12,

    // Complex (Phase 2)
    TableField = 20,
    AddressField = 21,
    DateRange = 22,

    // Computed (Phase 2)
    Formula = 30,

    // UI-only (Phase 2)
    Section = 40,
    Label = 41,
    Divider = 42
}
