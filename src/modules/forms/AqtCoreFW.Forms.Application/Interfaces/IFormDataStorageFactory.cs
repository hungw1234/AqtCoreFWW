using AqtCoreFW.Forms.Domain.Enums;

namespace AqtCoreFW.Forms.Application.Interfaces;

/// <summary>
/// Factory that resolves the correct IFormDataStorage implementation
/// based on the template's StorageMode.
/// </summary>
public interface IFormDataStorageFactory
{
    IFormDataStorage Create(StorageMode mode);
}
