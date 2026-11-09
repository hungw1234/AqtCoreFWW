using System.Net;
using System.Net.Http.Json;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.Tests.SharedFixtures;
using AqtCoreFW.Workflow.Domain.Entities;
using AqtCoreFW.Workflow.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AqtCoreFW.Workflow.Integration.Tests;

/// <summary>
/// Regression coverage for PHASE7-WF-SAVEGRAPH-IDENTITY (2026-05-08).
///
/// Pre-fix behavior (raw-SQL INSERT with explicit Id=0 into IDENTITY column):
///   PUT /definitions/{id}/graph returned HTTP 500 with SqlException
///   "Cannot insert explicit value for identity column ... when IDENTITY_INSERT is set to OFF".
///
/// Post-fix expectations (EF tracker + two-phase save, IDENTITY auto-assigned):
///   • HTTP 200 round-trip
///   • All states persisted with non-zero IDENTITY IDs and correct DefinitionId FK
///   • All transitions persisted with FromStateId / ToStateId resolved to the new state IDs
///   • TransactionScope wrap means any phase failure rolls back the whole graph (covers
///     PHASE4B-WF-FU1 in the same change).
///
/// Auth: WebAppFixture wires TestSystemAdminPermissionBypassHandler — any client carrying the
/// "SystemAdmin" role bypasses the PermissionRequirement check, so PUT /graph (gated on
/// PermCodes.Workflow.Def.Manage) is allowed without seeding the User+Role+Permission graph.
/// </summary>
[Collection(SqlServerCollection.CollectionName)]
public sealed class SaveDefinitionGraphIntegrationTests : IClassFixture<WebAppFixture>
{
    private readonly WebAppFixture _app;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public SaveDefinitionGraphIntegrationTests(WebAppFixture app)
    {
        _app = app;
    }

    [Fact]
    public async Task SaveGraph_WithThreeStatesTwoTransitions_Persists_AllRowsWithCorrectFkWiring()
    {
        // Arrange: seed an empty-graph WorkflowDefinition directly via DbContext.
        // Avoids POST /definitions overhead and keeps this test focused on the graph-save bug.
        var publicId = await SeedEmptyDefinitionAsync();

        var client = _app.CreateAuthenticatedClient(ActorId, TenantId, "SystemAdmin");

        var body = new
        {
            states = new object[]
            {
                new { name = "Draft",    displayNameVi = "Nháp",     displayNameEn = "Draft",     isInitial = true,  isFinal = false, color = "#9E9E9E", sortOrder = 1 },
                new { name = "UnderReview", displayNameVi = "Xem xét",  displayNameEn = "In Review", isInitial = false, isFinal = false, color = "#2196F3", sortOrder = 2 },
                new { name = "Approved", displayNameVi = "Đã duyệt", displayNameEn = "Approved",  isInitial = false, isFinal = true,  color = "#4CAF50", sortOrder = 3 },
            },
            transitions = new object[]
            {
                // Phase 07 H1 close: R2-strict at Save (plan 260523-1241) requires every non-recall
                // non-final-outgoing transition to carry an Assignment payload. Use Initiator (ref-less)
                // for both transitions — minimal valid assignment that satisfies R2-R4.
                new
                {
                    fromStateName = "Draft", toStateName = "UnderReview",
                    actionName = "Submit", actionLabelVi = "Nộp", actionLabelEn = "Submit",
                    conditionsJson = (string?)null, sortOrder = 1, isRecall = false,
                    assignment = new
                    {
                        combineMode = 0, // Union
                        allowAssignorOverride = false,
                        clauses = new[] { new { mode = 1, refsJson = (string?)null } }, // Initiator
                    },
                },
                new
                {
                    fromStateName = "UnderReview", toStateName = "Approved",
                    actionName = "Approve", actionLabelVi = "Duyệt", actionLabelEn = "Approve",
                    conditionsJson = (string?)null, sortOrder = 2, isRecall = false,
                    assignment = new
                    {
                        combineMode = 0,
                        allowAssignorOverride = false,
                        clauses = new[] { new { mode = 1, refsJson = (string?)null } },
                    },
                },
            },
        };

        // Act
        var response = await client.PutAsJsonAsync($"/api/v1/workflow/definitions/{publicId}/graph", body);

        // Assert HTTP success — pre-fix this was 500 IDENTITY_INSERT.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "Save graph must round-trip 200 — pre-fix this returned 500 due to raw-SQL INSERT into IDENTITY column.");

        // Re-load the definition from the DB and verify persistence shape.
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();

        var saved = await db.WorkflowDefinitions
            .Include(d => d.States)
            .Include(d => d.Transitions)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstAsync(d => d.PublicId == publicId);

        saved.States.Should().HaveCount(3);
        saved.Transitions.Should().HaveCount(2);

        saved.States.Select(s => s.Id).Should().OnlyContain(id => id > 0,
            "IDENTITY must assign every state a non-zero bigint Id.");
        saved.States.Select(s => s.DefinitionId).Should().OnlyContain(d => d == saved.Id,
            "Every state's DefinitionId must FK to the parent definition.");

        var stateIdByName = saved.States.ToDictionary(s => s.Name, s => s.Id, StringComparer.Ordinal);

        var submit = saved.Transitions.Single(t => t.ActionName == "Submit");
        submit.FromStateId.Should().Be(stateIdByName["Draft"]);
        submit.ToStateId.Should().Be(stateIdByName["UnderReview"]);

        var approve = saved.Transitions.Single(t => t.ActionName == "Approve");
        approve.FromStateId.Should().Be(stateIdByName["UnderReview"]);
        approve.ToStateId.Should().Be(stateIdByName["Approved"]);

        saved.Transitions.Select(t => t.Id).Should().OnlyContain(id => id > 0,
            "IDENTITY must assign every transition a non-zero bigint Id.");
        saved.Transitions.Select(t => t.DefinitionId).Should().OnlyContain(d => d == saved.Id,
            "Every transition's DefinitionId must FK to the parent definition.");

        // Audit fields — old raw-SQL path explicitly wrote UpdatedAt + ModifiedBy. New EF path
        // routes them through definition.ClearGraph(modifiedBy) → SetAuditUpdate. Verify the
        // mutation actually propagates through SaveChanges (silent regression risk).
        saved.ModifiedBy.Should().Be(ActorId);
        saved.UpdatedAt.Should().NotBeNull();
        saved.UpdatedAt!.Value.Should().BeAfter(saved.CreatedAt);
    }

    private async Task<Guid> SeedEmptyDefinitionAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkflowDbContext>();
        var gen = scope.ServiceProvider.GetRequiredService<IGuidGenerator>();

        var def = WorkflowDefinition.Create(
            gen: gen,
            name: $"PHASE7-WF-SAVEGRAPH integ {Guid.NewGuid():N}",
            description: "Empty-graph definition for SaveGraph integration test",
            tenantId: TenantId,
            createdBy: ActorId,
            definitionKey: $"sg-{Guid.NewGuid():N}",
            entityType: "Generic");

        db.WorkflowDefinitions.Add(def);
        await db.SaveChangesAsync();
        return def.PublicId;
    }
}
