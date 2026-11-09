using AqtCoreFW.Api;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.Identity.Domain.Models;
using AqtCoreFW.Identity.Domain.Services;
using AqtCoreFW.SharedKernel.Authorization;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Domain.Enums;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace AqtCoreFW.Forms.Integration.Tests;

/// <summary>
/// H1 (data-integrity, code review 260706) — proves that <see cref="AqtCoreFW.Infrastructure.Persistence.CrossModuleUnitOfWork"/>
/// leaves every enlisted <c>DbContext</c> usable AFTER <c>CommitAsync</c>/<c>RollbackAsync</c>.
///
/// <see cref="AqtCoreFW.Forms.Application.Commands.SubmitForm.SubmitFormCommandHandler"/> does a POST-commit
/// EF write on the SAME <c>FormsDbContext</c> instance that was enlisted onto the atomic submission write
/// span (<c>submission.LinkWorkflow(...)</c> + <c>submissionRepository.UpdateAsync(...)</c> — see
/// SubmitFormCommandHandler.cs:244-245). Before the fix, <c>CrossModuleUnitOfWork.CommitAsync</c> committed
/// the raw <c>SqlTransaction</c> directly without telling EF, so the enlisted context's
/// <c>Database.CurrentTransaction</c> still pointed at the now-completed transaction — the post-commit
/// <c>SaveChangesAsync</c> threw "This SqlTransaction has completed; it is no longer usable.", got swallowed
/// by the handler's catch-all, and the workflow instance was silently never linked (submission still returns
/// 200 OK).
///
/// Uses the REAL SQL Server integration DB (<see cref="WebAppFixture"/>) — an in-memory/mocked DbContext
/// would not reproduce the real ADO.NET transaction-completion exception this proves against.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class FormSubmissionApprovalWorkflowLinkAtomicityTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid AdminId  = Guid.NewGuid();
    private static readonly Guid UserId   = Guid.NewGuid();

    public FormSubmissionApprovalWorkflowLinkAtomicityTests(WebAppFixture app) => _app = app;

    [Fact]
    public async Task Submit_WithApprovalWorkflow_LinksInstance_NoPostCommitTransactionFailure()
    {
        var admin = CreateClientWithPermissions(_app, AdminId, TenantId,
            PermCodes.Forms.Template.Manage, PermCodes.Workflow.Def.View, PermCodes.Workflow.Def.Manage);
        var user = CreateClientWithPermissions(_app, UserId, TenantId,
            PermCodes.Forms.Submit.Create, PermCodes.Forms.Submit.View);

        var definitionPublicId = await CreateWorkflowDefinitionWithPoolAsync(_app, admin, TenantId, AdminId);

        var code = $"WFL{Guid.NewGuid():N}"[..9];
        var templatePublicId = await CreateActiveJsonTemplateWithApprovalWorkflowAsync(
            admin, code, definitionPublicId);

        var submitResp = await user.PostAsJsonAsync("/api/v1/forms/submissions", new
        {
            formTemplatePublicId = templatePublicId,
            data = new Dictionary<string, string> { ["fullName"] = "Nguyen Van A" }
        });

        submitResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var submissionId = (await submitResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetGuid();

        await using var scope = _app.Services.CreateAsyncScope();
        var formsDb = scope.ServiceProvider.GetRequiredService<FormsDbContext>();

        var submission = await formsDb.FormSubmissions
            .IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(s => s.PublicId == submissionId);

        // H1 acceptance criterion: the enlisted FormsDbContext must be able to open a fresh
        // transaction for this POST-commit write instead of throwing on the dead shared one.
        submission.WorkflowInstanceId.Should().NotBeNull(
            "CrossModuleUnitOfWork.CommitAsync must reset the enlisted context's transaction binding " +
            "so the post-commit LinkWorkflow/UpdateAsync write succeeds instead of throwing " +
            "\"This SqlTransaction has completed\" and being silently swallowed");

        var workflowDb = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var instanceExists = await workflowDb.WorkflowInstances
            .IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(i => i.PublicId == submission.WorkflowInstanceId);
        instanceExists.Should().BeTrue("the linked WorkflowInstanceId must reference a real, persisted instance");

        // A successful link must NOT also leave an orphan pending-link-attempt row — that row is only
        // written on the reject/throw fallback path (FormSubmissionWorkflowLinkPendingEvent handler).
        var pendingAttempts = await formsDb.FormSubmissionWorkflowLinkPendingAttempts
            .IgnoreQueryFilters().AsNoTracking()
            .CountAsync(a => a.SubmissionId == submissionId);
        pendingAttempts.Should().Be(0,
            "a successful workflow-instance link must not ALSO leave a pending-link-attempt row");
    }

    // --- Helpers ---

    /// <summary>
    /// Seeds a 2-state (Pending → Approved), 1-transition ("Approve") WorkflowDefinition with a
    /// ByPermission handler pool on the initial state's outgoing transition. Non-empty pool is
    /// required — <c>InProcessWorkflowModuleClient.CreateInstanceAsync</c> treats an empty pool as
    /// WF_NO_HANDLER (Fail outcome), which would route through the pending-link-attempt path instead
    /// of the LinkWorkflow success path this test targets.
    ///
    /// Actual permission membership of the submitter does not matter for instance creation to
    /// succeed: <c>WorkflowHandlerResolver.Finalize</c> returns a non-empty Pool resolution as soon
    /// as ANY ByPermission clause exists on the transition — resolution to concrete users only
    /// matters later, at task-claim time.
    /// </summary>
    private static async Task<Guid> CreateWorkflowDefinitionWithPoolAsync(
        WebAppFixture app, HttpClient admin, Guid tenantId, Guid createdBy)
    {
        var defResp = await admin.PostAsJsonAsync("/api/v1/workflow/definitions", new
        {
            Name = $"h1-repro-{Guid.NewGuid():N}",
            Description = "H1 repro — CrossModuleUnitOfWork post-commit EF write on enlisted FormsDbContext",
            TenantId = tenantId,
            CreatedBy = createdBy,
            EntityType = "Generic",
            States = new[]
            {
                // "Generic" entity type validates state names against a fixed allowlist
                // (Draft/Submitted/UnderReview/Assigned/Approved/Rejected/Returned/
                // ReturnedForRevision/Closed/Cancelled) — mirrors StartBusinessProcessRunFirstTaskTests's seed.
                new { Name = "Draft", DisplayNameVi = "Nháp", DisplayNameEn = "Draft",
                      IsInitial = true, IsFinal = false, Color = "#808080", SortOrder = 1 },
                new { Name = "Approved", DisplayNameVi = "Đã duyệt", DisplayNameEn = "Approved",
                      IsInitial = false, IsFinal = true, Color = "#52c41a", SortOrder = 2 },
            },
            Transitions = new[]
            {
                new { FromStateName = "Draft", ToStateName = "Approved",
                      ActionName = "Submit", ActionLabelVi = "Nộp", ActionLabelEn = "Submit",
                      ConditionsJson = (string?)null, SortOrder = 1 },
            },
        });
        var defRaw = await defResp.Content.ReadAsStringAsync();
        defResp.StatusCode.Should().Be(HttpStatusCode.OK, $"workflow definition seed must succeed. Body: {defRaw}");
        var definitionPublicId = JsonSerializer.Deserialize<JsonElement>(defRaw)
            .GetProperty("data").GetProperty("id").GetGuid();

        await using var scope = app.Services.CreateAsyncScope();
        var ctx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var def = await ctx.WorkflowDefinitions.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(d => d.PublicId == definitionPublicId);
        var transition = await ctx.WorkflowTransitions.IgnoreQueryFilters().AsNoTracking()
            .FirstAsync(t => t.DefinitionId == def.Id && t.ActionName == "Submit");

        var assignment = WorkflowTransitionAssignment.Create(
            workflowTransitionId: transition.Id, combineMode: AssignmentCombineMode.Union);
        assignment.AddClause(AssignmentClauseMode.ByPermission, $"""["{PermCodes.Workflow.Task.Process}"]""");
        ctx.WorkflowTransitionAssignments.Add(assignment);
        await ctx.SaveChangesAsync();

        return definitionPublicId;
    }

    /// <summary>Mirrors FormSubmissionOutboxAtomicityTests's template seed helper, plus wiring ApprovalWorkflowDefinitionId at create time.</summary>
    private static async Task<Guid> CreateActiveJsonTemplateWithApprovalWorkflowAsync(
        HttpClient admin, string code, Guid approvalWorkflowDefinitionId)
    {
        var createResp = await admin.PostAsJsonAsync("/api/v1/forms/templates", new
        {
            name = $"H1 Repro {code}",
            nameVi = $"H1 Repro {code}",
            code,
            storageMode = 0, // StorageMode.Json (no JsonStringEnumConverter registered)
            approvalWorkflowDefinitionId,
        });
        createResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var templatePublicId = (await createResp.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("data").GetProperty("id").GetGuid();

        var addFieldResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/fields",
            new
            {
                tenantId = TenantId,
                fieldKey = "fullName",
                type = 0, // FormFieldType.Text
                labelEn = "Full Name",
                labelVi = "Họ tên",
                displayOrder = 1,
                required = true,
                updatedBy = AdminId
            });
        addFieldResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var publishResp = await admin.PostAsJsonAsync(
            $"/api/v1/forms/templates/{templatePublicId}/publish",
            new { tenantId = TenantId, updatedBy = AdminId });
        publishResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        return templatePublicId;
    }

    /// <summary>Duplicated locally per repo convention — see FormSubmissionOutboxAtomicityTests's identical helper.</summary>
    private static HttpClient CreateClientWithPermissions(
        WebApplicationFactory<Program> factory, Guid userId, Guid tenantId, params string[] permissions)
    {
        var permService = Substitute.For<IEffectivePermissionService>();
        permService
            .GetSummaryAsync(userId, Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = userId,
                PermissionCodes = new HashSet<string>(permissions)
            });
        permService
            .GetSummaryAsync(Arg.Is<Guid>(id => id != userId), Arg.Any<CancellationToken>())
            .Returns(new EffectivePermissionSummary
            {
                UserId = Guid.Empty,
                PermissionCodes = new HashSet<string>()
            });

        var client = factory.WithWebHostBuilder(builder =>
            builder.ConfigureTestServices(services =>
                services.AddScoped(_ => permService)))
            .CreateClient();

        client.DefaultRequestHeaders.Add(TestAuthHandler.UserIdHeader, userId.ToString());
        client.DefaultRequestHeaders.Add(TestAuthHandler.TenantIdHeader, tenantId.ToString());
        return client;
    }
}
