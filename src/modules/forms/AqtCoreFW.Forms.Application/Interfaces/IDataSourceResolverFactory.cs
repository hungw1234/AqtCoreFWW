using AqtCoreFW.Forms.Domain.Enums;

namespace AqtCoreFW.Forms.Application.Interfaces;

/// <summary>Factory that creates the correct IDataSourceResolver for a given FormFieldType.</summary>
public interface IDataSourceResolverFactory
{
    IDataSourceResolver Create(FormFieldType type);
}
