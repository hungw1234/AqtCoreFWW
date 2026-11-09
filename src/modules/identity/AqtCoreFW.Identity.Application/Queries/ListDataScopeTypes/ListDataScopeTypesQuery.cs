using AqtCoreFW.SharedKernel.Application;

namespace AqtCoreFW.Identity.Application.Queries.ListDataScopeTypes;

/// <summary>List all data scope type lookup entries.</summary>
public sealed record ListDataScopeTypesQuery() : IQuery<IReadOnlyList<DataScopeTypeDto>>;

public sealed record DataScopeTypeDto(Guid Id, string Code, string Name, int SortOrder);
