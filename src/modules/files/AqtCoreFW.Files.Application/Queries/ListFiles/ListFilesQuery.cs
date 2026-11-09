using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Files.Application.Queries.ListFiles;

/// <summary>List files for a tenant (paginated). Result shape carries enriched uploader display name.</summary>
public sealed record ListFilesQuery(Guid TenantId, int Page, int PageSize) : IQuery<ListFilesResult>;

/// <summary>Wraps page items + total count. Items use anonymous object shape preserved by Newtonsoft serializer
/// (PascalCase field names match FE FileRecordDto — the Files module bypasses EF casing conventions).</summary>
public sealed record ListFilesResult(IReadOnlyList<object> Items, int TotalCount);
