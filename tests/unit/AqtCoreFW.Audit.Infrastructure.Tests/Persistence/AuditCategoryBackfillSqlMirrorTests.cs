using System.Reflection;
using AqtCoreFW.Audit.Domain.Services;
using AqtCoreFW.Audit.Domain.ValueObjects;
using AqtCoreFW.SharedKernel.Compliance;

namespace AqtCoreFW.Audit.Infrastructure.Tests.Persistence;

/// <summary>
/// Regression guard: every <see cref="AuditAction"/> constant MUST resolve to the same
/// (Category, Severity) pair as <see cref="AuditCategoryMapper"/>. The mirror dictionaries
/// below are a hand-maintained classification table; drift between them and the mapper =
/// silent audit mis-classification.
///
/// When you add an <see cref="AuditAction"/> constant or change the mapper, update the mirror
/// dictionaries below (or fold the addition into the existing fallback if AppAccess/Low is
/// correct). The audit schema carries Category/Severity as first-class columns, so there is no
/// data-backfill step to keep in sync.
/// </summary>
public sealed class AuditCategoryBackfillSqlMirrorTests
{
    // Hand-maintained classification table mirroring AuditCategoryMapper. Keys = NormaliseAction
    // output (uppercase, no underscores). Missing keys fall back to (AppAccess, Low).
    private static readonly Dictionary<string, AuditCategory> SqlCategoryCase = new()
    {
        ["LOGIN"] = AuditCategory.AppAccess,
        ["LOGOUT"] = AuditCategory.AppAccess,
        ["READ"] = AuditCategory.AppAccess,
        ["DATAACCESS"] = AuditCategory.AppAccess,
        ["ASSIGN"] = AuditCategory.AccountManagement,
        ["REVOKE"] = AuditCategory.AccountManagement,
        ["SYNC"] = AuditCategory.AccountManagement,
        ["ROLEASSIGN"] = AuditCategory.AccountManagement,
        ["ROLEREVOKE"] = AuditCategory.AccountManagement,
        ["ROLEASSIGNED"] = AuditCategory.AccountManagement,
        ["ROLEREVOKED"] = AuditCategory.AccountManagement,
        ["PERMISSIONGRANTED"] = AuditCategory.AccountManagement,
        ["PERMISSIONREVOKED"] = AuditCategory.AccountManagement,
        ["GROUPROLEASSIGNED"] = AuditCategory.AccountManagement,
        ["GROUPROLEREVOKED"] = AuditCategory.AccountManagement,
        ["DELEGATIONCREATED"] = AuditCategory.AccountManagement,
        ["DELEGATIONREVOKED"] = AuditCategory.AccountManagement,
        ["DATASCOPECHANGED"] = AuditCategory.AccountManagement,
        ["TOKENREVOKE"] = AuditCategory.AccountManagement,
        ["RTBF"] = AuditCategory.AccountManagement,
        ["ACCESSDENIED"] = AuditCategory.Error,
        ["SENSITIVEACTIONUSED"] = AuditCategory.Error,
    };

    private static readonly Dictionary<string, AuditSeverity> SqlSeverityCase = new()
    {
        ["READ"] = AuditSeverity.Low,
        ["DATAACCESS"] = AuditSeverity.Low,
        ["LOGIN"] = AuditSeverity.Low,
        ["LOGOUT"] = AuditSeverity.Low,
        ["MARK"] = AuditSeverity.Low,
        ["CREATE"] = AuditSeverity.Medium,
        ["UPDATE"] = AuditSeverity.Medium,
        ["ASSIGN"] = AuditSeverity.Medium,
        ["REVOKE"] = AuditSeverity.Medium,
        ["SYNC"] = AuditSeverity.Medium,
        ["SUBMIT"] = AuditSeverity.Medium,
        ["APPROVE"] = AuditSeverity.Medium,
        ["REJECT"] = AuditSeverity.Medium,
        ["CLOSE"] = AuditSeverity.Medium,
        ["PUBLISH"] = AuditSeverity.Medium,
        ["RESET"] = AuditSeverity.Medium,
        ["LOCK"] = AuditSeverity.Medium,
        ["UNLOCK"] = AuditSeverity.Medium,
        ["RESTORE"] = AuditSeverity.Medium,
        ["REGISTER"] = AuditSeverity.Medium,
        ["SEND"] = AuditSeverity.Medium,
        ["INGEST"] = AuditSeverity.Medium,
        ["DELEGATE"] = AuditSeverity.Medium,
        ["TRIGGER"] = AuditSeverity.Medium,
        ["MANAGE"] = AuditSeverity.Medium,
        ["REPORT"] = AuditSeverity.Medium,
        ["EXPORT"] = AuditSeverity.Medium,
        ["IMPORT"] = AuditSeverity.Medium,
        ["ROLEASSIGN"] = AuditSeverity.Medium,
        ["ROLEREVOKE"] = AuditSeverity.Medium,
        ["ROLEASSIGNED"] = AuditSeverity.Medium,
        ["ROLEREVOKED"] = AuditSeverity.Medium,
        ["PERMISSIONGRANTED"] = AuditSeverity.Medium,
        ["PERMISSIONREVOKED"] = AuditSeverity.Medium,
        ["GROUPROLEASSIGNED"] = AuditSeverity.Medium,
        ["GROUPROLEREVOKED"] = AuditSeverity.Medium,
        ["DELEGATIONCREATED"] = AuditSeverity.Medium,
        ["DELEGATIONREVOKED"] = AuditSeverity.Medium,
        ["DATASCOPECHANGED"] = AuditSeverity.Medium,
        ["TOKENREVOKE"] = AuditSeverity.Medium,
        ["DELETE"] = AuditSeverity.High,
        ["RTBF"] = AuditSeverity.Critical,
        ["ACCESSDENIED"] = AuditSeverity.Critical,
        ["SENSITIVEACTIONUSED"] = AuditSeverity.Critical,
    };

    [Fact]
    public void Mirror_Matches_AuditCategoryMapper_For_Every_AuditAction_Constant()
    {
        var mismatches = new List<string>();

        foreach (var actionField in typeof(AuditAction)
                     .GetFields(BindingFlags.Public | BindingFlags.Static)
                     .Where(f => f.FieldType == typeof(AuditAction)))
        {
            var auditAction = (AuditAction)actionField.GetValue(null)!;
            var rawValue = auditAction.Value;
            var key = Normalise(rawValue);

            var expectedCategory = SqlCategoryCase.TryGetValue(key, out var c) ? c : AuditCategory.AppAccess;
            var expectedSeverity = SqlSeverityCase.TryGetValue(key, out var s) ? s : AuditSeverity.Low;

            var (mapperCat, mapperSev) = AuditCategoryMapper.Map(rawValue);

            if (mapperCat != expectedCategory)
                mismatches.Add($"{rawValue} → mapper Category={mapperCat} but SQL CASE = {expectedCategory}");
            if (mapperSev != expectedSeverity)
                mismatches.Add($"{rawValue} → mapper Severity={mapperSev} but SQL CASE = {expectedSeverity}");
        }

        mismatches.Should().BeEmpty(
            "AuditCategoryMapper and this mirror dictionary must agree on every AuditAction constant. " +
            "If you changed the mapper, update the dictionary (or vice versa).");
    }

    private static string Normalise(string action) =>
        string.IsNullOrWhiteSpace(action)
            ? string.Empty
            : action.Trim().Replace("_", string.Empty).ToUpperInvariant();
}
