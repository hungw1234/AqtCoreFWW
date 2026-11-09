using System.Text.Json;
using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Queries.ResolveInternalDataSource;
using MediatR;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// Resolves data source options via a cross-module MediatR query.
/// Each module registers its own IRequestHandler for ResolveInternalDataSourceQuery.
/// If no handler is registered, the default handler returns empty (graceful degradation).
/// Config JSON: { "Module": "Identity", "Entity": "Department", "LabelField": "Name", "ValueField": "Id" }
/// </summary>
public sealed class InternalDataSourceResolver(ISender mediator) : IDataSourceResolver
{
    public async Task<IReadOnlyList<DataSourceOptionDto>> ResolveAsync(
        string dataSourceJson, Guid tenantId,
        IReadOnlyDictionary<string, string>? parentValues = null,
        CancellationToken cancellationToken = default)
    {
        // Cascade v1: Internal per-parent filtering is DEFERRED (red-team H1 — no real module handler
        // exists, so parameterization would be a vacuous no-op). parentValues is accepted for interface
        // uniformity and intentionally ignored until a real internal handler ships.
        _ = parentValues;
        var config = JsonSerializer.Deserialize<InternalDataSourceConfig>(dataSourceJson,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        if (config is null) return [];

        try
        {
            var result = await mediator.Send(
                new ResolveInternalDataSourceQuery(
                    config.Module, config.Entity,
                    config.LabelField, config.ValueField,
                    config.Filter, tenantId),
                cancellationToken);

            return result.IsSuccess ? result.Value : [];
        }
        catch (Exception)
        {
            // Graceful degradation — module may not have registered a handler yet
            return [];
        }
    }

    private sealed record InternalDataSourceConfig(
        string Module,
        string Entity,
        string LabelField,
        string ValueField,
        Dictionary<string, object>? Filter = null);
}
