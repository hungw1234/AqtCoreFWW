namespace AqtCoreFW.SharedKernel.Compliance;

/// <summary>
/// Audit log category per QĐ 742/QĐ-BTTTT mục 3.1.b — five mandatory groups
/// every audit event must be classified into for level-4 compliance evidence.
///
///   AppAccess         — (i)   Truy cập phần mềm        (regular login/read)
///   AdminLogin        — (ii)  Đăng nhập quản trị        (admin/sysadmin auth events)
///   Error             — (iii) Lỗi phát sinh             (handled errors, security incidents)
///   AccountManagement — (iv)  Quản lý tài khoản         (user/role/permission/group mutations)
///   ConfigChange      — (v)   Thay đổi cấu hình hệ thống (system params, workflow, retention, MFA policy)
/// </summary>
public enum AuditCategory : byte
{
    AppAccess = 1,
    AdminLogin = 2,
    Error = 3,
    AccountManagement = 4,
    ConfigChange = 5
}

/// <summary>
/// Decorates a command class (typically <c>IBaseCommand</c>) to declare its
/// QĐ 742 audit category. Source of truth for <c>AuditBehavior</c> — the runtime
/// pipeline reads this attribute via reflection rather than inferring from the
/// command name (which is fragile to renames).
///
/// Inherited so abstract base commands can carry a default category. Commands
/// without an attribute fall back to <see cref="AuditCategory.AppAccess"/> with
/// a warning log; CI guard test (<c>AuditBehaviorAttributeCoverageTests</c>) fails
/// the build if any production command is missing the attribute.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AuditCategoryAttribute(AuditCategory category) : Attribute
{
    public AuditCategory Category { get; } = category;
}
