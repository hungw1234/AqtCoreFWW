using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Forms.Application.Queries.ResolveInternalDataSource;

/// <summary>
/// Cross-module query: each module registers its own handler to return data source options.
/// If no handler is registered for a given Module/Entity, InternalDataSourceResolver catches
/// the exception and returns an empty list (graceful degradation).
/// </summary>
public sealed record ResolveInternalDataSourceQuery(
    string Module,
    string Entity,
    string LabelField,
    string ValueField,
    Dictionary<string, object>? Filter,
    Guid TenantId) : IQuery<IReadOnlyList<DataSourceOptionDto>>;
