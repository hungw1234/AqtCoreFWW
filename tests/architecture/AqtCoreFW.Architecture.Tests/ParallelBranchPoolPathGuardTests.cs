using AqtCoreFW.Architecture.Tests.Fixtures;
using FluentAssertions;
using NetArchTest.Rules;

namespace AqtCoreFW.Architecture.Tests;

/// <summary>
/// Architecture guard: ParallelBranchService must not acquire a pool-handler resolution path.
///
/// Today ParallelBranchService creates branch tasks via WorkflowTask.CreateForBranchChild
/// with a direct assignee (child.DefaultAssigneeId) — it does NOT call IWorkflowHandlerResolver
/// and therefore never reaches WorkflowTaskFactory.StageCandidateIndex.
///
/// If a future change wires a pool path into ParallelBranchService (e.g. to support pool-
/// assigned branch children), the test below will fail. The developer must then also wire the
/// candidate-index write path — preventing a silent resurrection of the original P1 data-loss
/// gap where index rows were never produced for pool tasks.
///
/// Failure here means an architectural change was made without updating the index-write path.
/// Fix the source: either wire StageCandidateIndex in the new pool-branch code, OR update this
/// guard with justification that index materialization is handled elsewhere.
/// </summary>
public sealed class ParallelBranchPoolPathGuardTests
{
    [Fact]
    public void ParallelBranchService_Should_Not_Depend_On_IWorkflowHandlerResolver()
    {
        // ParallelBranchService is in the Infrastructure layer.
        // IWorkflowHandlerResolver is in the Application layer (Services namespace).
        // No dependency = no pool-resolution path = no undiscovered index-write gap.
        var result = Types
            .InAssembly(AssemblyFixtures.WorkflowInfrastructure)
            .That()
            .HaveNameEndingWith("ParallelBranchService")
            .Should()
            .NotHaveDependencyOn("AqtCoreFW.Workflow.Application.Services.IWorkflowHandlerResolver")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "ParallelBranchService must not acquire a pool-handler resolution path " +
                     "unless the WorkflowTaskCandidateIndex write path is also wired (latent P1 gap prevention)");
    }

    [Fact]
    public void ParallelBranchService_Should_Not_Depend_On_WorkflowTaskFactory()
    {
        // If someone wires IWorkflowTaskFactory into ParallelBranchService to produce tasks
        // via the factory (which writes index rows), that is the CORRECT path and this test
        // should be removed. Until then, it guards that branch tasks bypass the factory
        // intentionally (not accidentally). Branch tasks today use direct assignees, not pool.
        var result = Types
            .InAssembly(AssemblyFixtures.WorkflowInfrastructure)
            .That()
            .HaveNameEndingWith("ParallelBranchService")
            .Should()
            .NotHaveDependencyOn("AqtCoreFW.Workflow.Application.Services.IWorkflowTaskFactory")
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            because: "ParallelBranchService bypasses IWorkflowTaskFactory intentionally — " +
                     "branch tasks use direct assignees today; any pool-branch path must wire the factory");
    }
}
