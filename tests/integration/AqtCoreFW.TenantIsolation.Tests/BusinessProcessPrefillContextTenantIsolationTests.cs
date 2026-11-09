using AqtCoreFW.BusinessProcess.Application.ModuleClients;
using AqtCoreFW.SharedKernel.Contracts.Clients;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.TenantIsolation.Tests;

/// <summary>
/// BP MVP P6: tenant-isolation guard on <see cref="InProcessFormPrefillContextClient"/>. Mirrors
/// <see cref="BusinessProcessRuntimeTenantIsolationTests"/> — substitute every cross-module client and
/// assert the resolver returns null AND no downstream lookup fires when the WorkflowInstance is unknown
/// or belongs to another tenant. Caller (controller) maps null -&gt; <c>NotFoundError</c> -&gt; HTTP 404 (D8).
/// </summary>
public sealed class BusinessProcessPrefillContextTenantIsolationTests
{
    private static readonly Guid CallerTenantId = Guid.NewGuid();
    private static readonly Guid VictimTenantId = Guid.NewGuid();
    private static readonly Guid InstanceId = Guid.NewGuid();
    private static readonly Guid TargetFormId = Guid.NewGuid();

    [Fact]
    public async Task GetPrefillContext_InstanceNotFound_ReturnsNull_NoSchemaOrSubmissionLookup()
    {
        var workflow = Substitute.For<IWorkflowModuleClient>();
        var formTemplate = Substitute.For<IFormTemplateQueryClient>();
        var submissionRun = Substitute.For<IFormSubmissionRunClient>();
        workflow.GetTenantForInstanceAsync(InstanceId, Arg.Any<CancellationToken>())
            .Returns((Guid?)null);

        var resolver = new InProcessFormPrefillContextClient(
            workflow, formTemplate, submissionRun,
            NullLogger<InProcessFormPrefillContextClient>.Instance);

        var result = await resolver.GetPrefillContextAsync(
            InstanceId, TargetFormId, targetFormTemplateVersion: 1, CallerTenantId);

        result.Should().BeNull();
        await formTemplate.DidNotReceiveWithAnyArgs()
            .GetVersionSchemaByPublicIdAsync(default, default, default, default);
        await submissionRun.DidNotReceiveWithAnyArgs()
            .GetLatestForRunAsync(default, default, default);
        await submissionRun.DidNotReceiveWithAnyArgs()
            .GetFullSubmissionAsync(default, default, default);
    }

    [Fact]
    public async Task GetPrefillContext_CrossTenantInstance_ReturnsNull_NoFurtherLookup()
    {
        var workflow = Substitute.For<IWorkflowModuleClient>();
        var formTemplate = Substitute.For<IFormTemplateQueryClient>();
        var submissionRun = Substitute.For<IFormSubmissionRunClient>();
        workflow.GetTenantForInstanceAsync(InstanceId, Arg.Any<CancellationToken>())
            .Returns(VictimTenantId);

        var resolver = new InProcessFormPrefillContextClient(
            workflow, formTemplate, submissionRun,
            NullLogger<InProcessFormPrefillContextClient>.Instance);

        var result = await resolver.GetPrefillContextAsync(
            InstanceId, TargetFormId, targetFormTemplateVersion: 1, CallerTenantId);

        result.Should().BeNull();
        await formTemplate.DidNotReceiveWithAnyArgs()
            .GetVersionSchemaByPublicIdAsync(default, default, default, default);
    }
}
