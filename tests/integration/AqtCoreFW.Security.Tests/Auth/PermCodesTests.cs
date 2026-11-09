using AqtCoreFW.SharedKernel.Authorization;

namespace AqtCoreFW.Security.Tests.Auth;

/// <summary>
/// TDD tests for PermCodes static constants class.
/// Validates count, uniqueness, and format of all permission codes.
/// </summary>
public sealed class PermCodesTests
{
    [Fact]
    [Trait("Category", "Security")]
    public void All_Should_Contain_129_Entries()
    {
        PermCodes.All.Count.Should().Be(129,
            "seeded permissions = 129 at HEAD (phase-04-A follow-up 2026-05-22 added WORKFLOW.INSTANCE.MANAGE — 4-A-A invariant closure for WorkflowInstancesController.Create). Update method name + assertion + justification together when adding perms.");
    }

    [Fact]
    [Trait("Category", "Security")]
    public void All_Should_Have_No_Duplicates()
    {
        PermCodes.All.Distinct().Count().Should().Be(PermCodes.All.Count,
            "each permission code must be unique");
    }

    [Fact]
    [Trait("Category", "Security")]
    public void All_Should_Match_Module_Resource_Action_Format()
    {
        foreach (var code in PermCodes.All)
        {
            var parts = code.Split('.');
            parts.Should().HaveCount(3, $"'{code}' must be MODULE.RESOURCE.ACTION");
            parts.Should().AllSatisfy(p => p.Should().NotBeNullOrWhiteSpace());
        }
    }

    [Theory]
    [Trait("Category", "Security")]
    [InlineData("IDENTITY.USER.VIEW")]
    [InlineData("SYSTEM.CONFIG.MANAGE")]
    [InlineData("REPORT.REPORT.MANAGE")]
    [InlineData("MASTER.WORKFLOW_TAXONOMY.VIEW")]
    [InlineData("PLATFORM.WORKFLOW_TAXONOMY.MANAGE")]
    public void All_Should_Contain_Key_Permission_Codes(string expectedCode)
    {
        PermCodes.All.Should().Contain(expectedCode);
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Master_WorkflowTaxonomy_View_Should_Match_Code()
    {
        PermCodes.Master.WorkflowTaxonomy.View.Should().Be("MASTER.WORKFLOW_TAXONOMY.VIEW");
    }

    [Fact]
    [Trait("Category", "Security")]
    public void Platform_WorkflowTaxonomy_Manage_Should_Match_Code()
    {
        PermCodes.Platform.WorkflowTaxonomy.Manage.Should().Be("PLATFORM.WORKFLOW_TAXONOMY.MANAGE");
    }
}
