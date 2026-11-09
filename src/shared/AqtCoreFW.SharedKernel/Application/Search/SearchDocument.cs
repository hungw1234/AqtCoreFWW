namespace AqtCoreFW.SharedKernel.Application.Search;

/// <summary>
/// Base type for all search-indexed documents.
/// Every document MUST carry TenantId for tenant isolation enforcement.
/// </summary>
public abstract record SearchDocument
{
    /// <summary>Unique document identifier within the index.</summary>
    public required string Id { get; init; }

    /// <summary>Tenant isolation — mandatory on every document. Never omit.</summary>
    public required Guid TenantId { get; init; }
}
