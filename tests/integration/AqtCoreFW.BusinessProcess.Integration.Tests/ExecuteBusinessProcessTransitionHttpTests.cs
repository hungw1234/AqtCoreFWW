using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AqtCoreFW.BusinessProcess.Domain.Entities;
using AqtCoreFW.BusinessProcess.Infrastructure.Persistence;
using AqtCoreFW.BusinessProcess.Infrastructure.Repositories;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Infrastructure.Persistence;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace AqtCoreFW.BusinessProcess.Integration.Tests;

/// <summary>
/// Live HTTP coverage for the BP transition "requiresForms" gating path
/// (<see cref="BusinessProcess.Application.Commands.ExecuteBusinessProcessTransition.ExecuteBusinessProcessTransitionCommandHandler"/>).
///
/// The unit handler test mocks BOTH runtime Dapper queries (transition-form bindings + form-version
/// snapshot) AND the discriminated-outcome serialization. This drives the real controller endpoint
/// over HTTP against a Testcontainers SQL Server, exercising the un-mocked path:
/// real <c>GetTransitionFormTemplateIdsAsync</c> + real <c>GetVersionSchemaByInternalIdAsync</c> +
/// <c>ApiResponse&lt;ExecuteBusinessProcessTransitionOutcome&gt;</c> JSON round-trip.
///
/// Fact 1 (the gap): a transition with a required form, POSTed with no form data, must short-circuit
///   with <c>requiresForms=true</c> + the inline published snapshot, WITHOUT advancing state — the
///   handler returns before opening the cross-module transaction, so no transition authz is touched.
/// Fact 2: POSTing WITH form data clears the requiresForms gate and enters the form-bundled branch;
///   a template id that is not bound to the transition is rejected by the binding guard (422), before
///   any transaction or workflow-authz. The full submit-then-advance journey is covered by the FE
///   Playwright spec + the handler unit happy-path + the Workflow module's transition-execution tests,
///   which stand up the fail-closed transition-assignment authz this self-contained seed omits.
///
/// Tenant alignment is load-bearing: there is no fixed test tenant — every seeded entity uses the same
/// random <c>tenantId</c> the HTTP client authenticates as (X-Test-TenantId), so the handler's
/// <c>request.TenantId == tenantContext.TenantId</c> guard and the EF query filters both match.
/// "SystemAdmin" role bypasses the endpoint's permission policies (Bp.View + Task.Process) via
/// <c>TestSystemAdminPermissionBypassHandler</c>; tenant scoping is unaffected by role.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class ExecuteBusinessProcessTransitionHttpTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    public ExecuteBusinessProcessTransitionHttpTests(WebAppFixture app) => _app = app;

    private static ITenantContext NewTenantContextStub(Guid tenantId)
    {
        // BusinessProcessDbContext writes consult the ambient tenant context; HttpContextTenantContext
        // returns null TenantId outside an HTTP request, so seed BP writes through a stub (mirrors the
        // proven pattern in BusinessProcessRepositoryFormMapsTests).
        var tc = Substitute.For<ITenantContext>();
        tc.TenantId.Returns(tenantId);
        tc.IsSystemAdmin.Returns(false);
        return tc;
    }

    /// <summary>
    /// Seed a self-contained scenario: workflow (Draft → Done, "submit" transition), a published
    /// single-field form template, a BP whose "submit" transition binds that form as REQUIRED, and a
    /// run sitting in the Draft state. All under one tenant. Returns the route + assertion handles.
    /// </summary>
    private static async Task<Scenario> SeedScenarioAsync(IServiceProvider sp, Guid tenantId, Guid actorId)
    {
        const string fieldKey = "ho_ten";
        var gen = sp.GetRequiredService<IGuidGenerator>();

        // Workflow — definition + 2 states + 1 "submit" transition (Draft initial → Done final).
        var workflowDb = sp.GetRequiredService<WorkflowDbContext>();
        var workflow = WorkflowDefinition.Create(gen, $"WF-{Guid.NewGuid():N}", "transition e2e wf", tenantId, actorId);
        workflowDb.WorkflowDefinitions.Add(workflow);
        await workflowDb.SaveChangesAsync();

        var draft = WorkflowState.Create(workflow.Id, "DRAFT", "Draft", "Draft", true, false, "#fff", 1);
        var done = WorkflowState.Create(workflow.Id, "DONE", "Done", "Done", false, true, "#fff", 2);
        workflowDb.Set<WorkflowState>().AddRange(draft, done);
        await workflowDb.SaveChangesAsync();

        var transition = WorkflowTransition.Create(workflow.Id, draft.Id, done.Id, "submit", "Submit", "Submit", null, 1);
        workflowDb.Set<WorkflowTransition>().Add(transition);
        await workflowDb.SaveChangesAsync();

        // Form — one required field, then publish so a FormTemplateVersion snapshot exists (version 1).
        var formsDb = sp.GetRequiredService<FormsDbContext>();
        var template = FormTemplate.Create(
            gen, tenantId, $"FT-{Guid.NewGuid():N}", "Phiếu chuyển trạng thái", $"ft-{Guid.NewGuid():N}",
            StorageMode.Json, actorId);
        template.AddField(fieldKey, FormFieldType.Text, "Họ và tên", "Full name", 1, required: true, null, actorId);
        formsDb.Set<FormTemplate>().Add(template);
        await formsDb.SaveChangesAsync();

        var snapshot = FormTemplateVersionFactory.PublishAndSnapshot(template, actorId);
        formsDb.Set<FormTemplateVersion>().Add(snapshot);
        await formsDb.SaveChangesAsync();

        // BP — definition + REQUIRED transition-form binding (seeded through repo + stub tenant ctx).
        var bpDb = sp.GetRequiredService<BusinessProcessDbContext>();
        var repo = new BusinessProcessRepository(bpDb, NewTenantContextStub(tenantId), NullLogger<BusinessProcessRepository>.Instance);
        var bp = BusinessProcessDefinition.Create(gen, tenantId, $"bp-{Guid.NewGuid():N}", "Transition e2e BP", workflow.Id, actorId);
        await repo.AddAsync(bp); // assigns bp.Id via IDENTITY

        bp.AddTransitionForm(BusinessProcessTransitionForm.Create(
            gen, tenantId, bp.Id, transition.Id, template.Id, snapshot.VersionNumber, actorId, isRequired: true));
        await repo.UpdateAsync(bp);

        // Run — a WorkflowInstance parked in the Draft state so the "submit" transition is reachable.
        var instance = WorkflowInstance.Create(
            gen, workflow.Id, draft.Id, "TransitionE2eRun", Guid.NewGuid(), tenantId, actorId);
        workflowDb.Set<WorkflowInstance>().Add(instance);
        await workflowDb.SaveChangesAsync();

        return new Scenario(
            BpPublicId: bp.PublicId,
            WorkflowInstancePublicId: instance.PublicId,
            InitialStateId: draft.Id,
            FormTemplateId: template.Id,
            FieldKey: fieldKey);
    }

    private sealed record Scenario(
        Guid BpPublicId,
        Guid WorkflowInstancePublicId,
        long InitialStateId,
        long FormTemplateId,
        string FieldKey);

    // ── Fact 1 (the gap): required form not supplied → requiresForms + inline snapshot, no state change ──

    [Fact]
    public async Task ExecuteTransition_RequiredFormNotSupplied_ReturnsRequiresFormsWithInlineSnapshot()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        Scenario seed;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            seed = await SeedScenarioAsync(scope.ServiceProvider, tenantId, actorId);
        }

        var client = _app.CreateAuthenticatedClient(actorId, tenantId, "SystemAdmin");
        var response = await client.PostAsJsonAsync(
            $"/api/v1/business-processes/runs/{seed.WorkflowInstancePublicId}/transitions",
            new
            {
                bpPublicId = seed.BpPublicId,
                actionName = "submit",
                transitionFormTemplateId = (long?)null,
                transitionFormData = (Dictionary<string, object?>?)null,
                consentGiven = false,
                comment = (string?)null
            });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var root = await response.Content.ReadFromJsonAsync<JsonElement>();
        root.GetProperty("success").GetBoolean().Should().BeTrue("API envelope reports transport success");

        var data = root.GetProperty("data");
        data.GetProperty("requiresForms").GetBoolean().Should().BeTrue("transition gated on a required form");
        data.GetProperty("success").GetBoolean().Should().BeFalse("short-circuit must NOT advance the run state");

        var forms = data.GetProperty("transitionForms");
        forms.GetArrayLength().Should().Be(1, "exactly one required form is bound to the transition");

        var schemaJson = forms[0].GetProperty("schemaJson").GetString();
        schemaJson.Should().NotBeNullOrWhiteSpace("the published snapshot is embedded inline for the FE to render");
        schemaJson.Should().Contain(seed.FieldKey, "the inline snapshot carries the seeded field, proving a real version fetch");
        forms[0].GetProperty("transitionFormTemplateId").GetInt64().Should().Be(seed.FormTemplateId);

        // Measure (not just infer) the short-circuit: the run must still sit in its initial state.
        await using var verifyScope = _app.Services.CreateAsyncScope();
        var workflowDb = verifyScope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var instance = await workflowDb.Set<WorkflowInstance>()
            .AsNoTracking()
            .SingleAsync(i => i.PublicId == seed.WorkflowInstancePublicId);
        instance.CurrentStateId.Should().Be(seed.InitialStateId, "the requiresForms short-circuit must not advance the run");
    }

    // ── Fact 2: form supplied → requiresForms gate cleared; unbound template id rejected pre-authz ──

    [Fact]
    public async Task ExecuteTransition_FormSuppliedWithUnboundTemplate_RejectsBindingBeforeAuthz()
    {
        var tenantId = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        Scenario seed;
        await using (var scope = _app.Services.CreateAsyncScope())
        {
            seed = await SeedScenarioAsync(scope.ServiceProvider, tenantId, actorId);
        }

        var client = _app.CreateAuthenticatedClient(actorId, tenantId, "SystemAdmin");
        var response = await client.PostAsJsonAsync(
            $"/api/v1/business-processes/runs/{seed.WorkflowInstancePublicId}/transitions",
            new
            {
                bpPublicId = seed.BpPublicId,
                actionName = "submit",
                // Supplying form data clears the requiresForms gate; the template id is deliberately
                // NOT the one bound to the transition, so the binding guard must reject it (422)
                // before any transaction or workflow-authz is reached.
                transitionFormTemplateId = seed.FormTemplateId + 999_999,
                transitionFormData = new Dictionary<string, object?> { [seed.FieldKey] = "Nguyễn Văn A" },
                consentGiven = false,
                comment = "integration binding-guard"
            });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "an unbound template on a form-bundled transition is a validation error — body was: {0}", body);

        var root = JsonDocument.Parse(body).RootElement;
        root.GetProperty("success").GetBoolean().Should().BeFalse();
        var message = root.GetProperty("errors")[0].GetProperty("message").GetString();
        message.Should().Contain("không thuộc transition",
            "the binding guard ran (proving the requiresForms gate was cleared by the supplied form), not the short-circuit");
    }
}
