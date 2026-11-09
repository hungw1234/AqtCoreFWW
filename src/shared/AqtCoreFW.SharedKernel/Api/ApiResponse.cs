using AqtCoreFW.SharedKernel.Application.Pagination;
using FluentResults;

namespace AqtCoreFW.SharedKernel.Api;

/// <summary>Standard API envelope for all endpoints.</summary>
public sealed record ApiResponse<T>(
    bool Success,
    T? Data,
    ApiMeta? Meta,
    IReadOnlyList<ApiError>? Errors)
{
    public static ApiResponse<T> Ok(T data, PaginationMeta? pagination = null) =>
        new(true, data, new ApiMeta(pagination, Guid.NewGuid().ToString(), DateTimeOffset.UtcNow), null);

    public static ApiResponse<T> Fail(IEnumerable<IError> errors) =>
        new(false, default, null, errors.Select(e => new ApiError(
            e.Metadata.TryGetValue("ErrorCode", out var code) ? code?.ToString() ?? "GOV_SYS_000" : "GOV_SYS_000",
            e.Message,
            e.Metadata.TryGetValue("DetailVi", out var vi) ? vi?.ToString() : null,
            Property: e.Metadata.TryGetValue("Property", out var prop) ? prop?.ToString() : null,
            // DEFAULT-DENY OPT-IN: only the reserved "PublicMetadata" key surfaces in the wire envelope.
            // All other FluentResults metadata keys (ErrorCode, GraphErrors, etc.) are stripped here.
            // IReadOnlyDictionary<TKey,TValue> is invariant in TValue — producer MUST use object? (nullable).
            Metadata: e.Metadata.TryGetValue("PublicMetadata", out var pm)
                && pm is IReadOnlyDictionary<string, object?> dict ? dict : null
        )).ToList());
}

public sealed record ApiMeta(
    PaginationMeta? Pagination,
    string RequestId,
    DateTimeOffset Timestamp);
