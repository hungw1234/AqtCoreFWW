using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Queries.EvaluateFormulas;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using Xunit;

namespace AqtCoreFW.Forms.Application.Tests.Queries;

/// <summary>
/// Unit tests for <see cref="EvaluatePublicFormulasQueryHandler"/> — the anonymous/public sibling
/// of <c>EvaluateFormulasQueryHandler</c> (P1). Covers: 404 when the by-Code bypass lookup finds
/// no row (unknown/non-public/inactive Code), 404 when the tenant-pinned EF load then returns
/// null, and the happy-path batch evaluation over active Formula fields. Mocking pattern mirrors
/// <c>SubmitPublicFormCommandHandlerTests.StubTemplateLookup</c> (two-step Dapper-bypass + EF read).
/// </summary>
public sealed class EvaluatePublicFormulasQueryHandlerTests
{
    private readonly IReadDbConnection         _db             = Substitute.For<IReadDbConnection>();
    private readonly IFormTemplateRepository   _templateRepo   = Substitute.For<IFormTemplateRepository>();
    private readonly IFormulaEvaluationService _formulaService = Substitute.For<IFormulaEvaluationService>();
    private readonly IGuidGenerator            _guidGen        = Substitute.For<IGuidGenerator>();
    private readonly EvaluatePublicFormulasQueryHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId   = Guid.NewGuid();
    private const string TemplateCode = "public_formula_template";

    public EvaluatePublicFormulasQueryHandlerTests()
    {
        _guidGen.NewIdRandom().Returns(_ => Guid.NewGuid());
        _handler = new EvaluatePublicFormulasQueryHandler(_db, _templateRepo, _formulaService);
    }

    [Fact]
    public async Task Handle_CodeLookupReturnsNoRow_ReturnsNotFoundError()
    {
        // Unknown/non-public/inactive Code -> the public_forms_bypass Dapper lookup returns null;
        // the EF repo must never be reached.
        _db.QueryFirstOrDefaultWithPublicFormsBypassAsync<EvaluatePublicFormulasQueryHandler.TemplateIdRow>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .ReturnsForAnyArgs(Task.FromResult<EvaluatePublicFormulasQueryHandler.TemplateIdRow?>(null));

        var result = await _handler.Handle(
            new EvaluatePublicFormulasQuery(TemplateCode, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
        await _templateRepo.DidNotReceiveWithAnyArgs()
            .GetPublicWithFieldsAsync(default, default, default);
    }

    [Fact]
    public async Task Handle_EfLoadReturnsNull_ReturnsNotFoundError()
    {
        // Dapper id-lookup succeeds but the tenant-pinned EF read comes back null (e.g. concurrent
        // deactivation between the two reads) -> still 404, not a throw.
        StubIdLookup(internalId: 42L, tenantId: TestTenantId);
        _templateRepo
            .GetPublicWithFieldsAsync(42L, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(null));

        var result = await _handler.Handle(
            new EvaluatePublicFormulasQuery(TemplateCode, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }

    [Fact]
    public async Task Handle_ActivePublicTemplateWithFormulaField_ReturnsComputedResult()
    {
        var template = CreateTemplate();
        template.AddField("amount", FormFieldType.Number, "Amount", "Amount", 1, false, null, TestUserId);
        AddFieldDirectly(template, FormField.Create(
            template.Id, "total", FormFieldType.Formula,
            "Total", "Total", 2, false, 1,
            formulaJson: """{"expression":"[amount]*2"}"""));
        template.Publish(TestUserId);

        StubIdLookup(template.Id, TestTenantId);
        _templateRepo
            .GetPublicWithFieldsAsync(template.Id, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(template));

        _formulaService
            .EvaluateAsync("""{"expression":"[amount]*2"}""", Arg.Any<IDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((object?)200m));

        var result = await _handler.Handle(
            new EvaluatePublicFormulasQuery(
                TemplateCode, new Dictionary<string, object?> { ["amount"] = 100m }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Results.Should().ContainKey("total").WhoseValue.Should().Be(200m);
        result.Value.Results.Should().NotContainKey("amount");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private void StubIdLookup(long internalId, Guid tenantId) =>
        _db.QueryFirstOrDefaultWithPublicFormsBypassAsync<EvaluatePublicFormulasQueryHandler.TemplateIdRow>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>(), Arg.Any<int?>())
            .ReturnsForAnyArgs(Task.FromResult<EvaluatePublicFormulasQueryHandler.TemplateIdRow?>(
                new EvaluatePublicFormulasQueryHandler.TemplateIdRow { Id = internalId, TenantId = tenantId }));

    private FormTemplate CreateTemplate() =>
        FormTemplate.Create(
            _guidGen, TestTenantId, "Public Formula Template", "Biểu mẫu công thức",
            TemplateCode, StorageMode.Json, TestUserId);

    /// <summary>Adds a pre-built FormField via reflection (mirrors EvaluateFormulasQueryHandlerTests
    /// helper) — needed for Formula fields, which require FormulaJson the AddField() sugar method
    /// doesn't take.</summary>
    private static void AddFieldDirectly(FormTemplate template, FormField field)
    {
        var fieldsField = typeof(FormTemplate)
            .GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var list = (List<FormField>)fieldsField.GetValue(template)!;
        list.Add(field);
    }
}
