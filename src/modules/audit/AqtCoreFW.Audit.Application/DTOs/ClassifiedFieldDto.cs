using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Audit.Application.DTOs;

/// <summary>Single property found by <see cref="DataClassificationScanner"/> scan.</summary>
public sealed record ClassifiedFieldDto(
    string AssemblyName,
    string TypeFullName,
    string PropertyName,
    string PropertyType,
    DataClassificationLevel Level,
    bool IsEncryptedAtRest);
