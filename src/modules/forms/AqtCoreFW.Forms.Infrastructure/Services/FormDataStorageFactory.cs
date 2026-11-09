using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Forms.Infrastructure.Services;

/// <summary>
/// Resolves the correct IFormDataStorage implementation based on StorageMode.
/// Uses IServiceProvider to resolve scoped services safely.
/// </summary>
public sealed class FormDataStorageFactory(IServiceProvider serviceProvider) : IFormDataStorageFactory
{
    public IFormDataStorage Create(StorageMode mode) => mode switch
    {
        StorageMode.Table => serviceProvider.GetRequiredService<TableFormDataStorage>(),
        _ => serviceProvider.GetRequiredService<JsonFormDataStorage>()
    };
}
