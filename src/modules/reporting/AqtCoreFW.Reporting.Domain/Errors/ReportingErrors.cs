using AqtCoreFW.SharedKernel.Errors;
using FluentResults;

namespace AqtCoreFW.Reporting.Domain.Errors;

/// <summary>GOV error codes for the Reporting module (GOV_RPT_NNN).</summary>
public static class ReportingErrors
{
    public static class Codes
    {
        public const string DefinitionNotFound = "GOV_RPT_001";
        public const string ExecutionNotFound = "GOV_RPT_002";
        public const string DangerousSql = "GOV_RPT_003";
        public const string MissingTenantFilter = "GOV_RPT_004";
        public const string QueryCatalogNotFound = "GOV_RPT_005";
        public const string FileNotReady = "GOV_RPT_006";
    }

    public static Error DefinitionNotFound(Guid id) =>
        new ReportDefinitionNotFoundError($"ReportDefinition '{id}' not found.");

    public static Error ExecutionNotFound(Guid id) =>
        new ReportExecutionNotFoundError($"ReportExecution '{id}' not found.");

    public static Error DangerousSql(string keyword) =>
        new DangerousSqlError($"SQL contains forbidden keyword: '{keyword}'.");

    public static Error MissingTenantFilter() =>
        new MissingTenantFilterError("SQL template must include '@TenantId' parameter for tenant isolation.");

    public static Error QueryCatalogEntryNotFound(string code) =>
        new ReportQueryCatalogNotFoundError($"QueryCatalogEntry '{code}' not found.");

    public static Error FileNotReady() =>
        new ValidationError("Report file is not yet ready for download.")
            .WithMetadata("ErrorCode", Codes.FileNotReady);
}

/// <summary>Maps to HTTP 404 via ApiControllerBase.NotFoundError check.</summary>
public class ReportDefinitionNotFoundError : NotFoundError
{
    // Base NotFoundError already sets ErrorCode = GOV_SYS_001.
    // ModuleErrorCode carries the module-specific GOV_RPT_NNN code without collision.
    public ReportDefinitionNotFoundError(string message) : base(message)
        => this.WithMetadata("ModuleErrorCode", ReportingErrors.Codes.DefinitionNotFound);
}

/// <summary>Maps to HTTP 404 via ApiControllerBase.NotFoundError check.</summary>
public class ReportExecutionNotFoundError : NotFoundError
{
    public ReportExecutionNotFoundError(string message) : base(message)
        => this.WithMetadata("ModuleErrorCode", ReportingErrors.Codes.ExecutionNotFound);
}

/// <summary>Maps to HTTP 422 via ApiControllerBase.ValidationError check.</summary>
public class DangerousSqlError : ValidationError
{
    // Base ValidationError already sets ErrorCode = GOV_SYS_002.
    public DangerousSqlError(string message) : base(message)
        => this.WithMetadata("ModuleErrorCode", ReportingErrors.Codes.DangerousSql);
}

/// <summary>Maps to HTTP 422 via ApiControllerBase.ValidationError check.</summary>
public class MissingTenantFilterError : ValidationError
{
    public MissingTenantFilterError(string message) : base(message)
        => this.WithMetadata("ModuleErrorCode", ReportingErrors.Codes.MissingTenantFilter);
}

/// <summary>Maps to HTTP 404 via ApiControllerBase.NotFoundError check.</summary>
public class ReportQueryCatalogNotFoundError : NotFoundError
{
    public ReportQueryCatalogNotFoundError(string message) : base(message)
        => this.WithMetadata("ModuleErrorCode", ReportingErrors.Codes.QueryCatalogNotFound);
}
