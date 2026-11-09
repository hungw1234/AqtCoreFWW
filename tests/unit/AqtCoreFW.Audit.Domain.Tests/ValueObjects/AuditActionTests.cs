using AqtCoreFW.Audit.Domain.ValueObjects;
using FluentAssertions;

namespace AqtCoreFW.Audit.Domain.Tests.ValueObjects;

/// <summary>
/// Unit tests for AuditAction value object (sealed record).
/// TC-AUD-D012: AuditAction equality and predefined values.
/// </summary>
public sealed class AuditActionTests
{
    // --- TC-AUD-D012: Predefined values carry correct string value ---

    [Theory]
    [InlineData("CREATE")]
    [InlineData("READ")]
    [InlineData("UPDATE")]
    [InlineData("DELETE")]
    [InlineData("LOGIN")]
    [InlineData("LOGOUT")]
    [InlineData("EXPORT")]
    [InlineData("IMPORT")]
    [InlineData("ROLE_ASSIGN")]
    [InlineData("ROLE_REVOKE")]
    [InlineData("TOKEN_REVOKE")]
    [InlineData("DATA_ACCESS")]
    [InlineData("RTBF")]
    public void PredefinedActions_HaveCorrectStringValues(string expected)
    {
        var action = expected switch
        {
            "CREATE"       => AuditAction.Create,
            "READ"         => AuditAction.Read,
            "UPDATE"       => AuditAction.Update,
            "DELETE"       => AuditAction.Delete,
            "LOGIN"        => AuditAction.Login,
            "LOGOUT"       => AuditAction.Logout,
            "EXPORT"       => AuditAction.Export,
            "IMPORT"       => AuditAction.Import,
            "ROLE_ASSIGN"  => AuditAction.RoleAssign,
            "ROLE_REVOKE"  => AuditAction.RoleRevoke,
            "TOKEN_REVOKE" => AuditAction.TokenRevoke,
            "DATA_ACCESS"  => AuditAction.DataAccess,
            "RTBF"         => AuditAction.Rtbf,
            _              => throw new ArgumentOutOfRangeException(nameof(expected))
        };

        action.Value.Should().Be(expected);
    }

    // --- Record equality ---

    [Fact]
    public void AuditAction_SameValue_AreEqual()
    {
        var a = new AuditAction("CREATE");
        var b = new AuditAction("CREATE");

        a.Should().Be(b);
    }

    [Fact]
    public void AuditAction_DifferentValues_AreNotEqual()
    {
        var a = new AuditAction("CREATE");
        var b = new AuditAction("DELETE");

        a.Should().NotBe(b);
    }

    [Fact]
    public void AuditAction_PredefinedCreate_EqualsNewWithSameValue()
    {
        var custom = new AuditAction("CREATE");

        AuditAction.Create.Should().Be(custom);
    }

    [Fact]
    public void AuditAction_GetHashCode_SameForEqualValues()
    {
        var a = new AuditAction("EXPORT");
        var b = new AuditAction("EXPORT");

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    // --- ToString ---

    [Fact]
    public void ToString_ReturnsValue()
    {
        AuditAction.Login.ToString().Should().Be("LOGIN");
    }

    [Fact]
    public void ToString_CustomAction_ReturnsCustomValue()
    {
        var custom = new AuditAction("CUSTOM_OPERATION");

        custom.ToString().Should().Be("CUSTOM_OPERATION");
    }

    // --- Phase D authorization events ---

    [Fact]
    public void PermissionGranted_HasCorrectValue()
    {
        AuditAction.PermissionGranted.Value.Should().Be("PERMISSION_GRANTED");
    }

    [Fact]
    public void AccessDenied_HasCorrectValue()
    {
        AuditAction.AccessDenied.Value.Should().Be("ACCESS_DENIED");
    }

    [Fact]
    public void DelegationCreated_HasCorrectValue()
    {
        AuditAction.DelegationCreated.Value.Should().Be("DELEGATION_CREATED");
    }
}
