using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.Tests.Integration.Infrastructure;
using AqtCoreFW.Tests.Integration.Workflow;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Authorization;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AqtCoreFW.Tests.Integration.BusinessProcess;

/// <summary>
/// Integration tests proving the Phase 02 cross-module DTC elimination:
/// StartBusinessProcessRunCommandHandler now wraps WorkflowDbContext + FormsDbContext
/// writes in a single local SQL transaction (ICrossModuleUnitOfWork) instead of
/// System.Transactions.TransactionScope, so no implicit distributed transaction is created
/// on .NET 10 where MSDTC is unavailable. Tests run against real SQL Server.
///
/// Invariant: No test may set TransactionManager.ImplicitDistributedTransactions = true;
/// that would mask the bug and invalidate CI proofs.
/// </summary>
[Collection("Integration")]
public class StartBusinessProcessRunTransactionTests(DatabaseFixture db) : IntegrationTestBase(db)
{
    private const string BaseUrl = "/api/v1/business-processes";
    private const string WorkflowBaseUrl = "/api/v1/workflow/definitions";
    private const string FormsBaseUrl = "/api/v1/forms/templates";

    private static readonly string ValidFormSchemaJson =
        """[{"fieldId":"applicantName","type":"text","label":"Applicant Name","required":true}]""";

    /// <summary>
    /// Happy path: Start BP Run with intake form returns 200 + persists BOTH WorkflowInstance
    /// and FormSubmission rows under a single local transaction. This test WOULD 500 with
    /// "Implicit distributed transactions have not been enabled" pre-Phase-02.
    ///
    /// The exact failing scenario: BP pins intake form → StartBusinessProcessRunCommandHandler
    /// calls workflowClient.CreateInstanceByInternalIdAsync (WorkflowDbContext writes)
    /// then formSubmissionClient.CreateForBpRunAsync (FormsDbContext writes) in a shared tx.
    /// </summary>
    [Fact]
    public async Task StartBusinessProcessRun_WithIntakeForm_Returns200_AndPersistsBoth()
    {
        // Arrange: create a workflow definition with a first-state transition handler
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var adminClient = CreateAuthenticatedClient(
            userId: userId.ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantId);

        // Create workflow
        var workflowDef = await CreateWorkflowDefinitionAsync(adminClient, tenantId, userId);
        var workflowDefId = workflowDef.GetProperty("id").GetGuid();

        // Seed transition assignment so instance creation doesn't 422 on empty pool
        await WorkflowSeedHelpers.SeedFirstStateAssignmentByPermissionAsync(
            Factory, workflowDefId, PermCodes.Workflow.Task.Process, actionName: "Submit");

        // Create + publish form template (returns Guid PublicId + version for BP pinning)
        var (formTemplateId, formVersion) = await CreatePublishedFormTemplateAsync(adminClient, tenantId, userId);

        // Create BP pinning the workflow + intake form
        var bpCreateResp = await adminClient.PostAsJsonAsync(BaseUrl, new
        {
            Code = "TEST-BP-INTAKE",
            Name = "Test BP with Intake",
            Description = "Tests intake form + workflow atomicity",
            TenantId = tenantId,
            WorkflowDefinitionPublicId = workflowDefId,
            IntakeFormTemplatePublicId = formTemplateId,
            IntakeFormTemplateVersion = formVersion,
        });
        var bpCreateRaw = await bpCreateResp.Content.ReadAsStringAsync();
        bpCreateResp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"BP creation with intake form must succeed. Body: {bpCreateRaw}");
        var bpCreateData = JsonSerializer.Deserialize<JsonElement>(bpCreateRaw);
        // BP create returns the new Guid PublicId directly as the envelope "data" value.
        var bpId = bpCreateData.GetProperty("data").GetGuid();

        // Activate the BP
        var activateResp = await adminClient.PostAsync($"{BaseUrl}/{bpId}/activate", null);
        activateResp.StatusCode.Should().Be(HttpStatusCode.NoContent,
            "BP activation must succeed");

        // Act: Start the BP Run with intake form data
        var intakeData = new Dictionary<string, object?> { { "applicantName", "John Doe" } };
        var startResp = await adminClient.PostAsJsonAsync($"{BaseUrl}/{bpId}/runs", new
        {
            EntityType = "Generic",
            EntityId = Guid.NewGuid(),
            IntakeFormData = intakeData,
            ConsentGiven = true,
        });

        // Assert: response is 200 OK
        var startRaw = await startResp.Content.ReadAsStringAsync();
        startResp.StatusCode.Should().Be(HttpStatusCode.OK,
            $"StartBusinessProcessRun with intake form should return 200 (DTC bug would cause 500). Body: {startRaw}");

        var startBody = JsonSerializer.Deserialize<JsonElement>(startRaw);
        var instanceId = startBody.GetProperty("data").GetProperty("workflowInstanceId").GetGuid();
        var intakeSubmissionId = startBody.GetProperty("data").GetProperty("intakeFormSubmissionId").GetGuid();

        // Assert: both rows committed in their respective schemas (fresh scope = committed reads)
        using var scope = Factory.Services.CreateAsyncScope();
        var wfCtx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var formsCtx = scope.ServiceProvider.GetRequiredService<FormsDbContext>();

        var wfInstanceExists = await wfCtx.WorkflowInstances.AsNoTracking()
            .AnyAsync(i => i.PublicId == instanceId);
        wfInstanceExists.Should().BeTrue("WorkflowInstance must be persisted");

        var formSubmissionExists = await formsCtx.FormSubmissions.AsNoTracking()
            .AnyAsync(s => s.PublicId == intakeSubmissionId);
        formSubmissionExists.Should().BeTrue("FormSubmission must be persisted");
    }

    /// <summary>
    /// Rollback test: when form submission fails mid-transaction (after WorkflowInstance is staged),
    /// both writes must roll back atomically — no orphan WorkflowInstance row.
    ///
    /// Implementation: we inject a failure by providing invalid form data that fails validation
    /// in SubmitFormCommandHandler AFTER the WI is already staged in the shared transaction.
    /// </summary>
    [Fact]
    public async Task StartBusinessProcessRun_FormSubmitFails_RollsBackWorkflowInstance()
    {
        // Arrange: set up BP with intake form (same as above test)
        var tenantId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var adminClient = CreateAuthenticatedClient(
            userId: userId.ToString(),
            roles: ["SystemAdmin"],
            tenantId: tenantId);

        var workflowDef = await CreateWorkflowDefinitionAsync(adminClient, tenantId, userId);
        var workflowDefId = workflowDef.GetProperty("id").GetGuid();

        await WorkflowSeedHelpers.SeedFirstStateAssignmentByPermissionAsync(
            Factory, workflowDefId, PermCodes.Workflow.Task.Process, actionName: "Submit");

        var (formTemplateId, formVersion) = await CreatePublishedFormTemplateAsync(adminClient, tenantId, userId);

        var bpCreateResp = await adminClient.PostAsJsonAsync(BaseUrl, new
        {
            Code = "TEST-BP-ROLLBACK",
            Name = "Test BP Rollback",
            TenantId = tenantId,
            WorkflowDefinitionPublicId = workflowDefId,
            IntakeFormTemplatePublicId = formTemplateId,
            IntakeFormTemplateVersion = formVersion,
        });
        var bpCreateRaw = await bpCreateResp.Content.ReadAsStringAsync();
        bpCreateResp.StatusCode.Should().Be(HttpStatusCode.OK, $"BP creation must succeed. Body: {bpCreateRaw}");
        var bpCreateData = JsonSerializer.Deserialize<JsonElement>(bpCreateRaw);
        var bpId = bpCreateData.GetProperty("data").GetGuid();

        var activateResp = await adminClient.PostAsync($"{BaseUrl}/{bpId}/activate", null);
        activateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var entityId = Guid.NewGuid();

        // Act: Start BP Run with INVALID form data (missing required field "applicantName")
        // This will fail AFTER WI is staged but DURING form submission — the exact failure scenario
        var invalidIntakeData = new Dictionary<string, object?> { }; // Missing applicantName
        var startResp = await adminClient.PostAsJsonAsync($"{BaseUrl}/{bpId}/runs", new
        {
            EntityType = "Generic",
            EntityId = entityId,
            IntakeFormData = invalidIntakeData,
            ConsentGiven = true,
        });

        // Assert: a missing required intake field is a validation error → 422, NOT a 500. The
        // failure originates as a structured Result.Fail(ValidationError) in SubmitFormCommand; it
        // must survive the cross-module hop and surface as Unprocessable Entity (regression guard:
        // pre-fix this propagated as a raw InvalidOperationException → GOV_SYS_000 500).
        var startRaw = await startResp.Content.ReadAsStringAsync();
        startResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            $"missing required intake field must return 422, not 500. Body: {startRaw}");

        // The failing field key must survive the cross-module hop so the FE can render it inline —
        // the whole point of carrying field errors through the transport-neutral outcome.
        var startBody = JsonSerializer.Deserialize<JsonElement>(startRaw);
        var errors = startBody.GetProperty("errors");
        errors.GetArrayLength().Should().BeGreaterThan(0, "a validation failure must surface field errors");
        errors[0].GetProperty("property").GetString().Should().Be("applicantName",
            "the failing field key must reach the client for inline error rendering");

        // Assert: NO orphan WorkflowInstance row for this entityId (transaction rolled back atomically)
        using var scope = Factory.Services.CreateAsyncScope();
        var wfCtx = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var orphanCount = await wfCtx.WorkflowInstances.AsNoTracking()
            .CountAsync(i => i.EntityId == entityId);

        orphanCount.Should().Be(0,
            "no orphan WorkflowInstance should be persisted when form submission fails (transaction must roll back atomically)");
    }

    // ----- HELPERS -----

    private async Task<JsonElement> CreateWorkflowDefinitionAsync(
        HttpClient client, string tenantId, Guid userId)
    {
        var response = await client.PostAsJsonAsync(WorkflowBaseUrl, new
        {
            Name = "Integration Test Workflow",
            Description = "Workflow for DTC elimination tests",
            TenantId = tenantId,
            CreatedBy = userId,
            EntityType = "Generic",
            States = new[]
            {
                new
                {
                    Name = "Draft",
                    DisplayNameVi = "Nháp",
                    DisplayNameEn = "Draft",
                    IsInitial = true,
                    IsFinal = false,
                    Color = "#2196F3",
                    SortOrder = 1,
                },
                new
                {
                    Name = "Submitted",
                    DisplayNameVi = "Đã nộp",
                    DisplayNameEn = "Submitted",
                    IsInitial = false,
                    IsFinal = true,
                    Color = "#4CAF50",
                    SortOrder = 2,
                },
            },
            Transitions = new[]
            {
                new
                {
                    FromStateName = "Draft",
                    ToStateName = "Submitted",
                    ActionName = "Submit",
                    ActionLabelVi = "Nộp hồ sơ",
                    ActionLabelEn = "Submit",
                    ConditionsJson = (string?)null,
                    SortOrder = 1,
                },
            },
        });

        var rawBody = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"workflow creation must succeed. Body: {rawBody}");
        var body = JsonSerializer.Deserialize<JsonElement>(rawBody);
        return body.GetProperty("data");
    }

    /// <summary>
    /// Creates a form template, adds one active required field (publish requires ≥1 active field —
    /// GOV_FRM_003), publishes it, and returns the template's Guid PublicId + published version for
    /// BP intake pinning. Template routes use the numeric long Id; BP pinning uses the Guid PublicId.
    /// </summary>
    private async Task<(Guid PublicId, int Version)> CreatePublishedFormTemplateAsync(
        HttpClient client, string tenantId, Guid userId)
    {
        var createResp = await client.PostAsJsonAsync(FormsBaseUrl, new
        {
            TenantId = tenantId,
            Name = "Integration Test Form",
            NameVi = "Mẫu kiểm tra tích hợp",
            Code = $"test_form_{Guid.NewGuid():N}",
            StorageMode = 0, // Json
            SchemaJson = ValidFormSchemaJson,
            CreatedBy = userId,
        });
        var createRaw = await createResp.Content.ReadAsStringAsync();
        createResp.StatusCode.Should().Be(HttpStatusCode.OK, $"form template creation must succeed. Body: {createRaw}");
        var createData = JsonSerializer.Deserialize<JsonElement>(createRaw).GetProperty("data");
        // FormTemplate is T2 — all routes address it by Guid PublicId ("id" in the response).
        var publicId = createData.GetProperty("id").GetGuid();

        // Publish requires ≥1 active field — SchemaJson alone does not create field entities.
        var addFieldResp = await client.PostAsJsonAsync($"{FormsBaseUrl}/{publicId}/fields", new
        {
            TenantId = tenantId,
            FieldKey = "applicantName",
            Type = 0, // FormFieldType.Text
            LabelVi = "Tên người nộp đơn",
            LabelEn = "Applicant Name",
            DisplayOrder = 1,
            Required = true,
            ValidationRulesJson = (string?)null,
            Options = (object?)null,
            UpdatedBy = userId,
        });
        var addFieldRaw = await addFieldResp.Content.ReadAsStringAsync();
        addFieldResp.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.NoContent],
            $"add field must succeed before publish. Body: {addFieldRaw}");

        var publishResp = await client.PostAsJsonAsync($"{FormsBaseUrl}/{publicId}/publish",
            new { TenantId = tenantId, UpdatedBy = userId });
        var publishRaw = await publishResp.Content.ReadAsStringAsync();
        publishResp.StatusCode.Should().BeOneOf([HttpStatusCode.OK, HttpStatusCode.NoContent],
            $"form template must be published before BP can pin it as intake. Body: {publishRaw}");

        var getResp = await client.GetAsync($"{FormsBaseUrl}/{publicId}?tenantId={tenantId}");
        var getRaw = await getResp.Content.ReadAsStringAsync();
        getResp.StatusCode.Should().Be(HttpStatusCode.OK, $"get form template must succeed. Body: {getRaw}");
        var getData = JsonSerializer.Deserialize<JsonElement>(getRaw).GetProperty("data");
        var version = getData.GetProperty("version").GetInt32();
        return (publicId, version);
    }
}
