using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// Resolves the correct IDataSourceResolver implementation for a given FormFieldType.
/// Uses IServiceProvider to allow scoped resolver lifetimes.
/// </summary>
public sealed class DataSourceResolverFactory(IServiceProvider serviceProvider)
    : IDataSourceResolverFactory
{
    public IDataSourceResolver Create(FormFieldType type) => type switch
    {
        FormFieldType.EnumRef     => serviceProvider.GetRequiredService<EnumDataSourceResolver>(),
        FormFieldType.InternalRef => serviceProvider.GetRequiredService<InternalDataSourceResolver>(),
        FormFieldType.ExternalRef => serviceProvider.GetRequiredService<ExternalDataSourceResolver>(),
        _ => throw new ArgumentException(
            $"No data source resolver registered for field type '{type}'.", nameof(type))
    };
}
