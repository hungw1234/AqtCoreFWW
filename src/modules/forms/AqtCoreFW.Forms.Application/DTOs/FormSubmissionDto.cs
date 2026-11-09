namespace AqtCoreFW.Forms.Application.DTOs;

/// <summary>
/// Read-side projection of a FormSubmission for API responses.
/// Id = PublicId (Guid, T2 external key). FormTemplateId = FormTemplate.PublicId (Guid, T2 AK).
/// Data values are object? to support both Json-mode (string values) and Table-mode (typed SQL values).
/// Phase-07 O-2: FormTemplateId retiered long→Guid (projects FormTemplate.PublicId, not internal FK).
/// SigningRequestId: non-null when a USB signing request was initiated for this submission's PDF.
///   The signed-PDF key and completion status are NOT stored here — read via
///   GET /api/signing/request/{signingRequestId}/status (pull-on-poll, single source of truth).
/// </summary>
public sealed record FormSubmissionDto(
    Guid Id,
    Guid TenantId,
    Guid FormTemplateId,
    int FormTemplateVersion,
    Guid SubmittedBy,
    DateTimeOffset SubmittedAt,
    IDictionary<string, object?> Data,
    string Status,
    Guid? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? ReviewComment,
    Guid? SigningRequestId = null);

/// <summary>
/// Lightweight list item for submission list queries.
/// FormTemplateId = FormTemplate.PublicId (Guid T2 AK) — Phase-07 O-2 retier.
/// </summary>
public sealed record FormSubmissionListItemDto(
    Guid Id,
    Guid FormTemplateId,
    int FormTemplateVersion,
    Guid SubmittedBy,
    DateTimeOffset SubmittedAt,
    string Status,
    Guid? ReviewedBy,
    DateTimeOffset? ReviewedAt,
    string? ReviewComment);
