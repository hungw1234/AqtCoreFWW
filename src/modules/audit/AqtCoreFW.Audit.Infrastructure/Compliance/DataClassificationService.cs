using System.Reflection;
using AqtCoreFW.Audit.Application.DTOs;
using AqtCoreFW.Audit.Application.Queries.GetClassificationInventory;
using AqtCoreFW.Infrastructure.Compliance;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Audit.Infrastructure.Compliance;

/// <summary>
/// Implements <see cref="IDataClassificationService"/> by delegating to
/// <see cref="DataClassificationScanner"/>. Registered as Singleton in module DI.
/// </summary>
public sealed class DataClassificationService : IDataClassificationService
{
    public ClassificationInventoryDto GetInventory()
    {
        var domainAssemblies = AppDomain.CurrentDomain
            .GetAssemblies()
            .Where(a =>
            {
                var name = a.GetName().Name ?? string.Empty;
                return name.StartsWith("AqtCoreFW.", StringComparison.Ordinal)
                    && name.EndsWith(".Domain", StringComparison.Ordinal);
            })
            .ToArray();

        // Defensive fallback: if domain assemblies aren't loaded yet, scan from disk.
        if (domainAssemblies.Length == 0)
        {
            domainAssemblies = Directory
                .GetFiles(AppContext.BaseDirectory, "AqtCoreFW.*.Domain.dll")
                .Select(Assembly.LoadFrom)
                .ToArray();
        }

        var report = DataClassificationScanner.ScanAssemblies(domainAssemblies);

        var entries = report.Entries
            .Select(e => new ClassifiedFieldDto(
                e.AssemblyName,
                e.TypeFullName,
                e.PropertyName,
                e.PropertyType,
                e.Level,
                IsEncryptedAtRest: false)) // TODO Wave 3: derive IsEncryptedAtRest from EF model metadata
            .ToList();

        return new ClassificationInventoryDto(
            ScannedAt: report.ScannedAt.UtcDateTime,
            TotalCount: report.TotalCount,
            RestrictedCount: report.RestrictedCount,
            ConfidentialCount: report.ConfidentialCount,
            Entries: entries);
    }
}
