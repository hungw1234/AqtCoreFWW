namespace AqtCoreFW.SharedKernel.Extensions.BuiltIn;

/// <summary>
/// Context passed to handlers at the "document.uploaded" extension point.
/// Carry all data a handler might need to process an uploaded file.
/// </summary>
public sealed record DocumentUploadedContext(
    Guid FileId,
    Guid TenantId,
    string FileName,
    string ContentType);

/// <summary>
/// Result returned by a document-upload handler.
/// Metadata is an optional bag of key/value pairs the handler wants to attach.
/// </summary>
public sealed record DocumentProcessResult(
    bool Handled,
    Dictionary<string, string>? Metadata = null);
