using FluentAssertions;
using static AqtCoreFW.Infrastructure.Configuration.RlsPrincipalValidator;

namespace AqtCoreFW.Infrastructure.Extensions.Tests.Configuration;

/// <summary>
/// Unit tests for <c>RlsPrincipalValidator.BuildCriticalMessage</c> — the pure decision function
/// isolated from IHost wiring and the live SQL probe (integration territory, like
/// <c>ProductionSecretValidator</c>'s untested IHost path).
/// </summary>
public sealed class RlsPrincipalValidatorTests
{
    private static PrincipalRoleFlags Flags(
        bool sysAdmin = false, bool dbOwner = false, bool dbSecurityAdmin = false,
        bool dbDdlAdmin = false, string? login = "aqtcore_app", string? db = "AqtCoreFW") =>
        new(sysAdmin, dbOwner, dbSecurityAdmin, dbDdlAdmin, login, db);

    // ── Safe principal ─────────────────────────────────────────────────────

    [Fact]
    public void BuildCriticalMessage_NoRlsDefeatingRole_ReturnsNull()
    {
        BuildCriticalMessage(Flags()).Should().BeNull();
    }

    [Fact]
    public void DbDdlAdmin_AloneDoesNotDefeatRls()
    {
        // db_ddladmin is informational only — must not trip the Critical message.
        Flags(dbDdlAdmin: true).HasRlsDefeatingRole.Should().BeFalse();
        BuildCriticalMessage(Flags(dbDdlAdmin: true)).Should().BeNull();
    }

    [Fact]
    public void HasRlsDefeatingRole_AllFalse_IsFalse()
    {
        Flags().HasRlsDefeatingRole.Should().BeFalse();
    }

    // ── Each individual RLS-defeating role ─────────────────────────────────

    [Fact]
    public void BuildCriticalMessage_DbOwner_NamesRoleLoginDbAndRemediation()
    {
        var msg = BuildCriticalMessage(Flags(dbOwner: true));

        msg.Should().NotBeNull();
        msg.Should().Contain("db_owner")
            .And.Contain("aqtcore_app")
            .And.Contain("AqtCoreFW")
            .And.Contain("deployment-guide.md");
    }

    [Fact]
    public void BuildCriticalMessage_SysAdmin_NamesServerRole()
    {
        BuildCriticalMessage(Flags(sysAdmin: true)).Should().Contain("sysadmin");
    }

    [Fact]
    public void BuildCriticalMessage_DbSecurityAdmin_NamesRole()
    {
        BuildCriticalMessage(Flags(dbSecurityAdmin: true)).Should().Contain("db_securityadmin");
    }

    // ── Combined roles — all listed, in a stable order ─────────────────────

    [Fact]
    public void BuildCriticalMessage_AllThreeRoles_ListsAllInSysadminDbOwnerSecAdminOrder()
    {
        var msg = BuildCriticalMessage(Flags(sysAdmin: true, dbOwner: true, dbSecurityAdmin: true))!;

        msg.Should().Contain("sysadmin").And.Contain("db_owner").And.Contain("db_securityadmin");
        msg.IndexOf("sysadmin", StringComparison.Ordinal)
            .Should().BeLessThan(msg.IndexOf("db_owner", StringComparison.Ordinal));
        msg.IndexOf("db_owner", StringComparison.Ordinal)
            .Should().BeLessThan(msg.IndexOf("db_securityadmin", StringComparison.Ordinal));
    }

    // ── Null login / db fall back to placeholders, still flags ─────────────

    [Fact]
    public void BuildCriticalMessage_DbOwner_NullLoginAndDb_StillFlagsWithPlaceholders()
    {
        var msg = BuildCriticalMessage(Flags(dbOwner: true, login: null, db: null));

        msg.Should().NotBeNull();
        msg.Should().Contain("unknown login").And.Contain("unknown database");
    }

    // ── HasRlsDefeatingRole reflects the OR of the three defeating flags ───

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, true, true)]
    public void HasRlsDefeatingRole_AnyDefeatingFlagSet_IsTrue(bool sa, bool owner, bool secAdmin)
    {
        Flags(sysAdmin: sa, dbOwner: owner, dbSecurityAdmin: secAdmin)
            .HasRlsDefeatingRole.Should().BeTrue();
    }
}
