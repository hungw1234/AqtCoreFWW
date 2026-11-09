using AqtCoreFW.Forms.Application.DTOs;

namespace AqtCoreFW.Forms.Application.Interfaces;

/// <summary>Resolves data source options for reference field types (EnumRef/InternalRef/ExternalRef).</summary>
public interface IDataSourceResolver
{
    /// <param name="parentValues">
    /// Cascade v1 (OPTIONAL, default null ⇒ old static-fetch behavior, backward-safe): selected values of the
    /// field's cascade parents, keyed by <c>filterParamName</c>. When present, the resolver filters/parameterizes
    /// the option-set by these values. Null/empty ⇒ full unfiltered set, byte-identical to the pre-cascade path.
    /// </param>
    Task<IReadOnlyList<DataSourceOptionDto>> ResolveAsync(
        string dataSourceJson, Guid tenantId,
        IReadOnlyDictionary<string, string>? parentValues = null,
        CancellationToken cancellationToken = default);
}
