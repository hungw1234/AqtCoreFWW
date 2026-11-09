namespace AqtCoreFW.SharedKernel.Authorization;

public static partial class PermCodes
{
    // ── IDENTITY ──────────────────────────────────────────────
    public static class Identity
    {
        public static class User
        {
            public const string View = "IDENTITY.USER.VIEW";
            public const string Create = "IDENTITY.USER.CREATE";
            public const string Update = "IDENTITY.USER.UPDATE";
            public const string Delete = "IDENTITY.USER.DELETE";
        }
        public static class Role
        {
            public const string View = "IDENTITY.ROLE.VIEW";
            public const string Manage = "IDENTITY.ROLE.MANAGE";
        }
        public static class Perm
        {
            public const string Assign = "IDENTITY.PERM.ASSIGN";
        }
        public static class Group
        {
            public const string View = "IDENTITY.GROUP.VIEW";
            public const string Manage = "IDENTITY.GROUP.MANAGE";
        }
        public static class Deleg
        {
            public const string View = "IDENTITY.DELEG.VIEW";
            public const string Manage = "IDENTITY.DELEG.MANAGE";
        }
        public static class Abac
        {
            public const string View = "IDENTITY.ABAC.VIEW";
            public const string Manage = "IDENTITY.ABAC.MANAGE";
        }
        public static class Sod
        {
            public const string View = "IDENTITY.SOD.VIEW";
            public const string Manage = "IDENTITY.SOD.MANAGE";
            public const string ViewAuditLog = "IDENTITY.SOD.VIEW_AUDIT_LOG";
            public const string ExportAuditLog = "IDENTITY.SOD.EXPORT_AUDIT_LOG";
        }
        public static class Session
        {
            public const string View = "IDENTITY.SESSION.VIEW";
            public const string Revoke = "IDENTITY.SESSION.REVOKE";
        }
        public static class Menu
        {
            public const string View = "IDENTITY.MENU.VIEW";
            public const string Manage = "IDENTITY.MENU.MANAGE";
        }
        public static class Policy
        {
            public const string View = "IDENTITY.POLICY.VIEW";
            public const string Manage = "IDENTITY.POLICY.MANAGE";
        }
        public static class Consent
        {
            public const string View = "IDENTITY.CONSENT.VIEW";
        }
        public static class DataScope
        {
            public const string Manage = "IDENTITY.DATASCOPE.MANAGE";
        }
        /// <summary>Tenant CRUD permissions — SystemAdmin only.</summary>
        public static class Tenant
        {
            public const string View = "IDENTITY.TENANT.VIEW";
            public const string Manage = "IDENTITY.TENANT.MANAGE";
        }
    }
}
