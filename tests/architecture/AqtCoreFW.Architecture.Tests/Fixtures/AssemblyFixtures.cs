using System.Reflection;

namespace AqtCoreFW.Architecture.Tests.Fixtures;

/// <summary>
/// Centralises assembly loading for architecture tests.
/// All assemblies are loaded once via a static field — reflection is expensive.
/// To add a new module: add assembly fields + a new ModuleAssemblies entry in Modules list.
/// </summary>
public static class AssemblyFixtures
{
    // SharedKernel
    public static readonly Assembly SharedKernel =
        typeof(AqtCoreFW.SharedKernel.Domain.AuditableEntity).Assembly;

    // ── Identity ──────────────────────────────────────────────────────────
    public static readonly Assembly IdentityDomain =
        typeof(AqtCoreFW.Identity.Domain.Entities.ApplicationUser).Assembly;
    public static readonly Assembly IdentityApplication =
        Assembly.Load("AqtCoreFW.Identity.Application");
    public static readonly Assembly IdentityInfrastructure =
        Assembly.Load("AqtCoreFW.Identity.Infrastructure");
    public static readonly Assembly IdentityPresentation =
        Assembly.Load("AqtCoreFW.Identity.Presentation");

    // ── Rules ─────────────────────────────────────────────────────────────
    public static readonly Assembly RulesDomain =
        typeof(AqtCoreFW.Rules.Domain.Entities.RuleSet).Assembly;
    public static readonly Assembly RulesApplication =
        Assembly.Load("AqtCoreFW.Rules.Application");
    public static readonly Assembly RulesInfrastructure =
        Assembly.Load("AqtCoreFW.Rules.Infrastructure");
    public static readonly Assembly RulesPresentation =
        typeof(AqtCoreFW.Rules.Presentation.Controllers.RuleSetsController).Assembly;

    // ── Collaboration ─────────────────────────────────────────────────────
    public static readonly Assembly CollaborationDomain =
        typeof(AqtCoreFW.Collaboration.Domain.Entities.Conversation).Assembly;
    public static readonly Assembly CollaborationApplication =
        Assembly.Load("AqtCoreFW.Collaboration.Application");
    public static readonly Assembly CollaborationInfrastructure =
        Assembly.Load("AqtCoreFW.Collaboration.Infrastructure");
    public static readonly Assembly CollaborationPresentation =
        typeof(AqtCoreFW.Collaboration.Presentation.Controllers.ConversationsController).Assembly;

    // ── AI ────────────────────────────────────────────────────────────────
    public static readonly Assembly AiDomain =
        typeof(AqtCoreFW.Ai.Domain.Entities.AiModelProfile).Assembly;
    public static readonly Assembly AiApplication =
        Assembly.Load("AqtCoreFW.Ai.Application");
    public static readonly Assembly AiInfrastructure =
        Assembly.Load("AqtCoreFW.Ai.Infrastructure");
    public static readonly Assembly AiPresentation =
        typeof(AqtCoreFW.Ai.Presentation.Controllers.AiModelProfilesController).Assembly;

    // ── Audit ─────────────────────────────────────────────────────────────
    public static readonly Assembly AuditDomain =
        typeof(AqtCoreFW.Audit.Domain.Entities.AuditLogEntry).Assembly;
    public static readonly Assembly AuditApplication =
        Assembly.Load("AqtCoreFW.Audit.Application");
    public static readonly Assembly AuditInfrastructure =
        Assembly.Load("AqtCoreFW.Audit.Infrastructure");
    public static readonly Assembly AuditPresentation =
        typeof(AqtCoreFW.Audit.Presentation.Controllers.AuditLogsController).Assembly;

    // ── Files ─────────────────────────────────────────────────────────────
    public static readonly Assembly FilesDomain =
        typeof(AqtCoreFW.Files.Domain.Entities.FileRecord).Assembly;
    public static readonly Assembly FilesApplication =
        Assembly.Load("AqtCoreFW.Files.Application");
    public static readonly Assembly FilesInfrastructure =
        Assembly.Load("AqtCoreFW.Files.Infrastructure");
    public static readonly Assembly FilesPresentation =
        typeof(AqtCoreFW.Files.Presentation.Controllers.FilesController).Assembly;

    // ── Forms ─────────────────────────────────────────────────────────────
    public static readonly Assembly FormsDomain =
        typeof(AqtCoreFW.Forms.Domain.Entities.FormTemplate).Assembly;
    public static readonly Assembly FormsApplication =
        Assembly.Load("AqtCoreFW.Forms.Application");
    public static readonly Assembly FormsInfrastructure =
        Assembly.Load("AqtCoreFW.Forms.Infrastructure");
    public static readonly Assembly FormsPresentation =
        typeof(AqtCoreFW.Forms.Presentation.Controllers.FormTemplatesController).Assembly;

    // ── Notifications ─────────────────────────────────────────────────────
    public static readonly Assembly NotificationsDomain =
        typeof(AqtCoreFW.Notifications.Domain.Entities.Notification).Assembly;
    public static readonly Assembly NotificationsApplication =
        Assembly.Load("AqtCoreFW.Notifications.Application");
    public static readonly Assembly NotificationsInfrastructure =
        Assembly.Load("AqtCoreFW.Notifications.Infrastructure");
    public static readonly Assembly NotificationsPresentation =
        typeof(AqtCoreFW.Notifications.Presentation.Controllers.NotificationsController).Assembly;

    // ── Reporting ─────────────────────────────────────────────────────────
    public static readonly Assembly ReportingDomain =
        typeof(AqtCoreFW.Reporting.Domain.Entities.ReportDefinition).Assembly;
    public static readonly Assembly ReportingApplication =
        Assembly.Load("AqtCoreFW.Reporting.Application");
    public static readonly Assembly ReportingInfrastructure =
        Assembly.Load("AqtCoreFW.Reporting.Infrastructure");
    public static readonly Assembly ReportingPresentation =
        typeof(AqtCoreFW.Reporting.Presentation.Controllers.ReportsController).Assembly;

    // ── Workflow ──────────────────────────────────────────────────────────
    public static readonly Assembly WorkflowDomain =
        typeof(AqtCoreFW.Workflow.Domain.Entities.WorkflowDefinition).Assembly;
    public static readonly Assembly WorkflowApplication =
        Assembly.Load("AqtCoreFW.Workflow.Application");
    public static readonly Assembly WorkflowInfrastructure =
        Assembly.Load("AqtCoreFW.Workflow.Infrastructure");
    public static readonly Assembly WorkflowPresentation =
        typeof(AqtCoreFW.Workflow.Presentation.Controllers.WorkflowDefinitionsController).Assembly;

    // ── Integration ───────────────────────────────────────────────────────
    public static readonly Assembly IntegrationDomain =
        typeof(AqtCoreFW.Integration.Domain.Entities.Partner).Assembly;
    public static readonly Assembly IntegrationApplication =
        Assembly.Load("AqtCoreFW.Integration.Application");
    public static readonly Assembly IntegrationInfrastructure =
        Assembly.Load("AqtCoreFW.Integration.Infrastructure");
    public static readonly Assembly IntegrationPresentation =
        typeof(AqtCoreFW.Integration.Presentation.Controllers.PartnersController).Assembly;

    // ── AssetManagement ──────────────────────────────────────────────────
    public static readonly Assembly AssetManagementDomain =
        typeof(AqtCoreFW.AssetManagement.Domain.Entities.AssetManagementEntity).Assembly;
    public static readonly Assembly AssetManagementApplication =
        Assembly.Load("AqtCoreFW.AssetManagement.Application");
    public static readonly Assembly AssetManagementInfrastructure =
        Assembly.Load("AqtCoreFW.AssetManagement.Infrastructure");
    public static readonly Assembly AssetManagementPresentation =
        typeof(AqtCoreFW.AssetManagement.Presentation.Controllers.AssetManagementController).Assembly;

    // ── Contracts ────────────────────────────────────────────────────────
    public static readonly Assembly ContractsDomain =
        typeof(AqtCoreFW.Contracts.Domain.Entities.Contract).Assembly;
    public static readonly Assembly ContractsApplication =
        Assembly.Load("AqtCoreFW.Contracts.Application");
    public static readonly Assembly ContractsInfrastructure =
        Assembly.Load("AqtCoreFW.Contracts.Infrastructure");
    public static readonly Assembly ContractsPresentation =
        typeof(AqtCoreFW.Contracts.Presentation.Controllers.ContractsController).Assembly;

    // ── MasterData ────────────────────────────────────────────────────────
    public static readonly Assembly MasterDataDomain =
        typeof(AqtCoreFW.MasterData.Domain.Entities.Dictionary).Assembly;
    public static readonly Assembly MasterDataApplication =
        Assembly.Load("AqtCoreFW.MasterData.Application");
    public static readonly Assembly MasterDataInfrastructure =
        Assembly.Load("AqtCoreFW.MasterData.Infrastructure");
    public static readonly Assembly MasterDataPresentation =
        typeof(AqtCoreFW.MasterData.Presentation.Controllers.MasterDataController).Assembly;

    // ── Organization ──────────────────────────────────────────────────────
    public static readonly Assembly OrganizationDomain =
        typeof(AqtCoreFW.Organization.Domain.Entities.OrgUnit).Assembly;
    public static readonly Assembly OrganizationApplication =
        Assembly.Load("AqtCoreFW.Organization.Application");
    public static readonly Assembly OrganizationInfrastructure =
        Assembly.Load("AqtCoreFW.Organization.Infrastructure");
    public static readonly Assembly OrganizationPresentation =
        typeof(AqtCoreFW.Organization.Presentation.Controllers.OrgUnitsController).Assembly;

    // ── SystemParams ──────────────────────────────────────────────────────
    public static readonly Assembly SystemParamsDomain =
        typeof(AqtCoreFW.SystemParams.Domain.Entities.SystemParameter).Assembly;
    public static readonly Assembly SystemParamsApplication =
        Assembly.Load("AqtCoreFW.SystemParams.Application");
    public static readonly Assembly SystemParamsInfrastructure =
        Assembly.Load("AqtCoreFW.SystemParams.Infrastructure");
    public static readonly Assembly SystemParamsPresentation =
        typeof(AqtCoreFW.SystemParams.Presentation.Controllers.SystemParamsController).Assembly;

    // ── BusinessProcess ───────────────────────────────────────────────────
    public static readonly Assembly BusinessProcessDomain =
        typeof(AqtCoreFW.BusinessProcess.Domain.Entities.BusinessProcessDefinition).Assembly;
    public static readonly Assembly BusinessProcessApplication =
        Assembly.Load("AqtCoreFW.BusinessProcess.Application");
    public static readonly Assembly BusinessProcessInfrastructure =
        Assembly.Load("AqtCoreFW.BusinessProcess.Infrastructure");
    public static readonly Assembly BusinessProcessPresentation =
        typeof(AqtCoreFW.BusinessProcess.Presentation.Controllers.BusinessProcessesController).Assembly;

    // ── Hosts ─────────────────────────────────────────────────────────────
    // Host assemblies expose admin controllers that live outside module Presentation layers
    // (e.g. AccountMfaController, MfaPoliciesAdminController). Host assemblies are NOT module
    // layers, so they are not added to the Modules list — sweep tests opt-in via PresentationAssemblies.
    public static readonly Assembly ApiHost =
        typeof(AqtCoreFW.Api.Controllers.Admin.AlertingAdminController).Assembly;

    // The second composition root. Its OAuth/OIDC controllers are governed by the Infrastructure-
    // coupling guard (G1) like any controller host; the single-module placement guard (G3) deliberately
    // does NOT apply (those controllers are OpenIddict-protocol-bound to this host — see ADR-011).
    public static readonly Assembly AuthServerHost =
        typeof(AqtCoreFW.AuthServer.Controllers.AuthorizationController).Assembly;

    /// <summary>
    /// All assemblies that may host MVC/API controllers — module Presentation layers + hosts.
    /// Used by reflection sweeps (e.g. RequiresMfa attribute on every *AdminController).
    /// </summary>
    public static readonly IReadOnlyList<Assembly> PresentationAssemblies =
    [
        IdentityPresentation,
        RulesPresentation,
        CollaborationPresentation,
        AiPresentation,
        AuditPresentation,
        FilesPresentation,
        FormsPresentation,
        NotificationsPresentation,
        ReportingPresentation,
        WorkflowPresentation,
        IntegrationPresentation,
        AssetManagementPresentation,
        ContractsPresentation,
        MasterDataPresentation,
        OrganizationPresentation,
        SystemParamsPresentation,
        BusinessProcessPresentation,
        ApiHost,
        AuthServerHost,
    ];

    /// <summary>
    /// Structured module assembly data — used by data-driven architecture tests.
    /// Add new modules here; tests auto-cover layer isolation + cross-module checks.
    /// </summary>
    public static readonly IReadOnlyList<ModuleAssemblies> Modules =
    [
        new("Identity", IdentityDomain, IdentityApplication, IdentityInfrastructure, IdentityPresentation),
        new("Rules", RulesDomain, RulesApplication, RulesInfrastructure, RulesPresentation),
        new("Collaboration", CollaborationDomain, CollaborationApplication, CollaborationInfrastructure, CollaborationPresentation),
        new("Ai", AiDomain, AiApplication, AiInfrastructure, AiPresentation),
        new("Audit", AuditDomain, AuditApplication, AuditInfrastructure, AuditPresentation),
        new("Files", FilesDomain, FilesApplication, FilesInfrastructure, FilesPresentation),
        new("Forms", FormsDomain, FormsApplication, FormsInfrastructure, FormsPresentation),
        new("Notifications", NotificationsDomain, NotificationsApplication, NotificationsInfrastructure, NotificationsPresentation),
        new("Reporting", ReportingDomain, ReportingApplication, ReportingInfrastructure, ReportingPresentation),
        new("Workflow", WorkflowDomain, WorkflowApplication, WorkflowInfrastructure, WorkflowPresentation),
        new("Integration", IntegrationDomain, IntegrationApplication, IntegrationInfrastructure, IntegrationPresentation),
        new("AssetManagement", AssetManagementDomain, AssetManagementApplication, AssetManagementInfrastructure, AssetManagementPresentation),
        new("Contracts", ContractsDomain, ContractsApplication, ContractsInfrastructure, ContractsPresentation),
        new("MasterData", MasterDataDomain, MasterDataApplication, MasterDataInfrastructure, MasterDataPresentation),
        new("Organization", OrganizationDomain, OrganizationApplication, OrganizationInfrastructure, OrganizationPresentation),
        new("SystemParams", SystemParamsDomain, SystemParamsApplication, SystemParamsInfrastructure, SystemParamsPresentation),
        new("BusinessProcess", BusinessProcessDomain, BusinessProcessApplication, BusinessProcessInfrastructure, BusinessProcessPresentation),
    ];
}

/// <summary>Named tuple grouping a module's 4-layer assemblies for architecture validation.</summary>
public sealed record ModuleAssemblies(
    string Name,
    Assembly Domain,
    Assembly Application,
    Assembly Infrastructure,
    Assembly Presentation);
