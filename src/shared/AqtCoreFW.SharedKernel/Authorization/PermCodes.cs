namespace AqtCoreFW.SharedKernel.Authorization;

/// <summary>
/// Compile-time permission code constants — use with [Authorize(Policy = PermCodes.Files.File.View)].
/// Format: MODULE.RESOURCE.ACTION. Must match seeded Permission.Code values exactly.
///
/// Vocabulary-ownership split: the code constants live in per-module partial files
/// (<c>PermCodes.&lt;Module&gt;.cs</c>) so each module owns its permission namespace at the file boundary —
/// enforced by <c>PermCodeOwnershipConventionTests</c>. This file holds only the aggregate catalog
/// (<see cref="All"/> / <c>_all</c>) consumed for bulk policy registration (Identity ModuleRegistration)
/// and permission seeding (E2ETestDataSeeder). <c>PermCodesAllDriftTests</c> keeps <c>_all</c> in
/// lock-step with the declared consts, and <c>PermCodeCheckedGrantableContractTests</c> keeps every
/// [Authorize]-checked code inside this catalog.
/// </summary>
public static partial class PermCodes
{
    /// <summary>Returns all permission code strings for bulk policy registration.</summary>
    public static IReadOnlyList<string> All => _all;

    private static readonly string[] _all =
    [
        // IDENTITY (25: +2 TENANT.VIEW/MANAGE)
        Identity.User.View, Identity.User.Create, Identity.User.Update, Identity.User.Delete,
        Identity.Role.View, Identity.Role.Manage,
        Identity.Perm.Assign,
        Identity.Group.View, Identity.Group.Manage,
        Identity.Deleg.View, Identity.Deleg.Manage,
        Identity.Abac.View, Identity.Abac.Manage,
        Identity.Sod.View, Identity.Sod.Manage, Identity.Sod.ViewAuditLog, Identity.Sod.ExportAuditLog,
        Identity.Session.View, Identity.Session.Revoke,
        Identity.Menu.View, Identity.Menu.Manage,
        Identity.Policy.View, Identity.Policy.Manage,
        Identity.Consent.View,
        Identity.DataScope.Manage,
        Identity.Tenant.View, Identity.Tenant.Manage,
        // FILES (8)
        Files.File.View, Files.File.Upload, Files.File.Download, Files.File.Delete,
        Files.Template.View, Files.Template.Manage,
        Files.Retention.View, Files.Retention.Manage,
        // AUDIT (9: +2 RETENTION.* — phase-01 slice-D2 QĐ 742 §3.2.b retention policy admin)
        Audit.Log.View, Audit.Log.Export,
        Audit.Login.View,
        Audit.Rtbf.View, Audit.Rtbf.Execute,
        Audit.Incident.View, Audit.Incident.Manage,
        Audit.Retention.View, Audit.Retention.Manage,
        // WORKFLOW (14: +1 INSTANCE.MANAGE — phase-04-A follow-up 2026-05-22, 4-A-A invariant closure for WorkflowInstancesController.Create)
        Workflow.Def.View, Workflow.Def.Manage,
        Workflow.Task.View, Workflow.Task.Process,
        Workflow.Instance.View, Workflow.Instance.Manage,
        Workflow.Assign.Manage,
        Workflow.Delegation.Manage,
        Workflow.Analytics.View,
        Workflow.Migration.Read, Workflow.Migration.Create,
        Workflow.Migration.Approve, Workflow.Migration.Execute, Workflow.Migration.Cancel,
        // FORMS (9: +1 WFLINK.MANAGE — P6-DEFER-1 phase 05, admin orphan-workflow-link retry/shelve)
        Forms.Template.View, Forms.Template.Manage,
        Forms.Submit.View, Forms.Submit.Create, Forms.Submit.Approve, Forms.Submit.Reject, Forms.Submit.Export,
        Forms.FormView.Manage,
        Forms.WorkflowLink.Manage,
        // NOTIF (4)
        Notif.Msg.View, Notif.Msg.Send,
        Notif.Template.View, Notif.Template.Manage,
        // ORG (4)
        Org.Unit.View, Org.Unit.Manage,
        Org.Staff.View, Org.Staff.Manage,
        // MASTER (5)
        Master.Data.View, Master.Data.Manage,
        Master.Dict.View, Master.Dict.Manage,
        Master.ExtMap.Manage,
        // AI (5)
        Ai.Extract.Use,
        Ai.Model.View, Ai.Model.Manage,
        Ai.Prompt.View, Ai.Prompt.Manage,
        // COLLAB (3)
        Collab.Conv.View, Collab.Conv.Create,
        Collab.Msg.Send,
        // INTEG (5)
        Integ.Contract.View, Integ.Contract.Manage,
        Integ.Partner.View, Integ.Partner.Manage,
        Integ.MsgLog.View,
        // REPORT (6)
        Report.Dash.View, Report.Dash.Manage,
        Report.ReportRes.View, Report.ReportRes.Export, Report.ReportRes.Manage,
        Report.Widget.Manage,
        // RULES (5)
        Rules.RuleSet.View, Rules.RuleSet.Manage,
        Rules.DTable.View, Rules.DTable.Manage,
        Rules.Eval.Use,
        // SEARCH (2)
        Search.Query.Use,
        Search.Saved.Manage,
        // SYSPARAM (6)
        SysParam.Param.View, SysParam.Param.Manage,
        SysParam.Flag.View, SysParam.Flag.Manage,
        SysParam.Announce.View, SysParam.Announce.Manage,
        // SYSTEM (2)
        System.Config.View, System.Config.Manage,
        // MASTER — workflow taxonomy (phase-00, +1)
        Master.WorkflowTaxonomy.View,
        // PLATFORM (new tier, phase-00, +1)
        Platform.WorkflowTaxonomy.Manage,
        // SECURITY (phase-03 QĐ742 1.6.a, +4)
        Security.MfaPolicy.View, Security.MfaPolicy.Manage,
        Security.MfaEmergency.Request, Security.MfaEmergency.Approve,
        // SECURITY (phase-05 QĐ742 §2.2.a, +2) IP filter rule admin
        Security.IpRule.View, Security.IpRule.Manage,
        // SECURITY (phase-05 Wave-2 QĐ742 D7, +1) per-criterion compliance visibility
        Security.Health.View,
        // SECURITY (phase-05b W6 QĐ742 §5.1, +2) digital signature config admin
        Security.SignatureConfig.View, Security.SignatureConfig.Manage,
        // COMPLIANCE (phase-04 Wave2 QĐ742 §4.4, +2)
        Compliance.View, Compliance.Manage,
        // BUSINESSPROCESS (BP MVP P4, +3)
        BusinessProcess.Bp.View, BusinessProcess.Bp.Manage, BusinessProcess.Bp.Activate,
        // SIGNATURE (+1) self-service signing-cert enrollment approval — dedicated least-privilege code
        Signature.Enrollment.Approve,
    ];
}
