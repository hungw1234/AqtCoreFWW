using AqtCoreFW.SharedKernel.Errors;

namespace AqtCoreFW.Forms.Domain.Errors;

/// <summary>GOV error codes for the Forms module.</summary>
public static class FormErrors
{
    public static class Codes
    {
        public const string TemplateNotFound = "GOV_FRM_001";
        public const string SubmissionNotFound = "GOV_FRM_002";
        public const string InvalidSchema = "GOV_FRM_003";
        public const string ValidationFailed = "GOV_FRM_004";
        public const string PatternTimeout = "GOV_FRM_005";
        public const string Forbidden = "GOV_FRM_006";
        public const string StorageModeImmutable = "GOV_FRM_007";
        public const string FieldKeyImmutable = "GOV_FRM_008";
        public const string FieldTypeImmutable = "GOV_FRM_009";
        public const string InvalidTemplateStatus = "GOV_FRM_010";
        public const string InvalidFieldKey = "GOV_FRM_011";
        public const string DuplicateFieldKey = "GOV_FRM_012";
        public const string MaterializationFailed = "GOV_FRM_013";
        public const string DuplicateTemplateCode = "GOV_FRM_014";
        public const string FieldNotFound = "GOV_FRM_015";
        public const string InvalidSubmissionStatus = "GOV_FRM_017";
        public const string ReviewCommentRequired = "GOV_FRM_018";
        public const string PublicCodeConflict = "GOV_FRM_019";
    }

    public static NotFoundError TemplateNotFound(long id) =>
        new($"FormTemplate {id} không tìm thấy.");

    public static NotFoundError TemplateNotFound(Guid publicId) =>
        new($"FormTemplate {publicId} không tìm thấy.");

    public static NotFoundError SubmissionNotFound(Guid id) =>
        new($"FormSubmission {id} không tìm thấy.");

    public static ValidationError InvalidSchema(string reason) =>
        new($"Schema biểu mẫu không hợp lệ: {reason}", "Schema");

    public static ForbiddenError AccessDenied() =>
        new("Không có quyền truy cập dữ liệu này.");

    public static ValidationError StorageModeImmutable() =>
        new("Chế độ lưu trữ không thể thay đổi sau khi đã có dữ liệu.", Codes.StorageModeImmutable);

    public static ValidationError FieldKeyImmutable(string fieldKey) =>
        new($"FieldKey '{fieldKey}' không thể thay đổi sau khi đã có dữ liệu.", Codes.FieldKeyImmutable);

    public static ValidationError FieldTypeImmutable(string fieldKey) =>
        new($"Kiểu dữ liệu của field '{fieldKey}' không thể thay đổi.", Codes.FieldTypeImmutable);

    public static ValidationError InvalidTemplateStatus(string expected) =>
        new($"Biểu mẫu phải ở trạng thái {expected} để thực hiện thao tác này.", Codes.InvalidTemplateStatus);

    public static ValidationError InvalidFieldKey(string reason) =>
        new($"FieldKey không hợp lệ: {reason}", Codes.InvalidFieldKey);

    public static ValidationError DuplicateFieldKey(string fieldKey) =>
        new($"FieldKey '{fieldKey}' đã tồn tại trong biểu mẫu.", Codes.DuplicateFieldKey);

    public static ValidationError MaterializationFailed(string reason) =>
        new($"Khởi tạo bảng dữ liệu thất bại: {reason}", Codes.MaterializationFailed);

    public static ValidationError DuplicateTemplateCode(string code) =>
        new($"Code biểu mẫu '{code}' đã tồn tại trong tenant này.", Codes.DuplicateTemplateCode);

    /// <summary>
    /// A public (IsPublic=1) template in another tenant already owns this Code. Public Codes must be
    /// globally unique so the anonymous by-Code resolution is unambiguous — 409 Conflict. The code is
    /// prefixed into the message because <see cref="ConflictError"/> carries no separate code field.
    /// </summary>
    public static ConflictError PublicCodeConflict(string code) =>
        new($"{Codes.PublicCodeConflict}: Code công khai '{code}' đã được một biểu mẫu công khai khác sử dụng — Code công khai phải là duy nhất trên toàn hệ thống.");

    public static NotFoundError FieldNotFound(long fieldId) =>
        new($"FormField {fieldId} không tìm thấy.");
}
