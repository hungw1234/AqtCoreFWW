namespace AqtCoreFW.SharedKernel.Api;

/// <summary>Bulk operation request supporting create/update/delete in one call.</summary>
/// <param name="Atomic">If true, all operations fail or succeed together.</param>
public sealed record BulkRequest<T>(
    IReadOnlyList<BulkOperation<T>> Operations,
    bool Atomic = true);

public sealed record BulkOperation<T>(
    BulkOperationType Op,
    string? Id,
    T? Data);

public enum BulkOperationType { Create, Update, Delete }
