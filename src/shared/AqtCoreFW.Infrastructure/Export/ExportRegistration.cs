using AqtCoreFW.SharedKernel.Application.Export;
using Microsoft.IO;

namespace AqtCoreFW.Infrastructure.Export;

/// <summary>
/// DI registration for export infrastructure — call AddExport() in InfrastructureRegistration
/// or directly in Program.cs.
/// Registers: IExcelExporter (ClosedXML), IPdfExporter (QuestPDF), RecyclableMemoryStreamManager.
/// </summary>
public static class ExportRegistration
{
    public static IServiceCollection AddExport(this IServiceCollection services)
    {
        // RecyclableMemoryStreamManager — singleton, thread-safe pool for allocation-efficient streams
        services.AddSingleton<RecyclableMemoryStreamManager>();

        // Export implementations — singleton: stateless, safe for concurrent use
        services.AddSingleton<IExcelExporter, ClosedXmlExporter>();
        services.AddSingleton<IPdfExporter, QuestPdfExporter>();

        return services;
    }
}
