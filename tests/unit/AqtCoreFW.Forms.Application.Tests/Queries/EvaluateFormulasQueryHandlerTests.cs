using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Queries.EvaluateFormulas;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Errors;
using FluentResults;
using System.Text.Json;
using Xunit;

namespace AqtCoreFW.Forms.Application.Tests.Queries;

/// <summary>
/// Unit tests for EvaluateFormulasQueryHandler.
/// Covers: 404 on missing/inactive template, batch evaluation over active Formula fields
/// (non-Formula fields excluded from the result), null-computed omission.
/// Fixture style mirrors SubmitFormCommandHandlerTests (FormField.Create + reflection helper
/// to attach fields the domain aggregate has no public sugar-method for).
/// </summary>
public sealed class EvaluateFormulasQueryHandlerTests
{
    private readonly IFormTemplateRepository   _templateRepo;
    private readonly IFormulaEvaluationService _formulaService;
    private readonly IGuidGenerator            _guidGen;
    private readonly EvaluateFormulasQueryHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId   = Guid.NewGuid();

    public EvaluateFormulasQueryHandlerTests()
    {
        _templateRepo   = Substitute.For<IFormTemplateRepository>();
        _formulaService = Substitute.For<IFormulaEvaluationService>();
        _guidGen        = Substitute.For<IGuidGenerator>();
        _guidGen.NewIdRandom().Returns(_ => Guid.NewGuid());

        _handler = new EvaluateFormulasQueryHandler(_templateRepo, _formulaService);
    }

    [Fact]
    public async Task Handle_TemplateNotFound_ReturnsNotFoundError()
    {
        _templateRepo
            .GetByPublicIdWithFieldsAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(null));

        var result = await _handler.Handle(
            new EvaluateFormulasQuery(Guid.NewGuid(), TestTenantId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }

    [Fact]
    public async Task Handle_InactiveTemplate_ReturnsNotFoundError()
    {
        // Draft template (never published) → Status != Active.
        var template = CreateTemplate();
        SetupTemplate(template);

        var result = await _handler.Handle(
            new EvaluateFormulasQuery(template.PublicId, TestTenantId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e is NotFoundError);
    }

    [Fact]
    public async Task Handle_TwoFormulaFieldsAndOneTextField_ResultsHasOnlyFormulaKeys()
    {
        var template = CreateTemplate();
        template.AddField("amount", FormFieldType.Number, "Amount", "Amount", 1, false, null, TestUserId);
        template.AddField("label_only", FormFieldType.Text, "Label", "Label", 2, false, null, TestUserId);

        AddFieldDirectly(template, FormField.Create(
            template.Id, "total", FormFieldType.Formula,
            "Total", "Total", 3, false, 1,
            formulaJson: """{"expression":"[amount]*2"}"""));
        AddFieldDirectly(template, FormField.Create(
            template.Id, "double_total", FormFieldType.Formula,
            "Double Total", "Double Total", 4, false, 1,
            formulaJson: """{"expression":"[amount]*4"}"""));

        template.Publish(TestUserId);
        SetupTemplate(template);

        _formulaService
            .EvaluateAsync("""{"expression":"[amount]*2"}""", Arg.Any<IDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((object?)200m));
        _formulaService
            .EvaluateAsync("""{"expression":"[amount]*4"}""", Arg.Any<IDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((object?)400m));

        var result = await _handler.Handle(
            new EvaluateFormulasQuery(
                template.PublicId, TestTenantId,
                new Dictionary<string, object?> { ["amount"] = 100m }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Results.Should().HaveCount(2);
        result.Value.Results.Should().ContainKey("total").WhoseValue.Should().Be(200m);
        result.Value.Results.Should().ContainKey("double_total").WhoseValue.Should().Be(400m);
        result.Value.Results.Should().NotContainKey("label_only");
        result.Value.Results.Should().NotContainKey("amount");
    }

    [Fact]
    public async Task Handle_JsonElementBoundValue_NormalizedToClrTypeBeforeEvaluation()
    {
        // Real HTTP requests bind Dictionary<string, object?> scalars as JsonElement (no
        // object-unboxing converter registered on the host). NCalc throws on a boxed JsonElement
        // operand — the handler must normalize before handing values to the evaluation service.
        var template = CreateTemplate();
        AddFieldDirectly(template, FormField.Create(
            template.Id, "total", FormFieldType.Formula,
            "Total", "Total", 1, false, 1,
            formulaJson: """{"expression":"[amount]*2"}"""));
        template.Publish(TestUserId);
        SetupTemplate(template);

        IDictionary<string, object?>? capturedValues = null;
        _formulaService
            .EvaluateAsync(
                Arg.Any<string>(),
                Arg.Do<IDictionary<string, object?>>(v => capturedValues = v),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((object?)200m));

        var jsonBoundAmount = JsonDocument.Parse("100").RootElement;

        await _handler.Handle(
            new EvaluateFormulasQuery(
                template.PublicId, TestTenantId,
                new Dictionary<string, object?> { ["amount"] = jsonBoundAmount }),
            CancellationToken.None);

        capturedValues.Should().NotBeNull();
        capturedValues!["amount"].Should().BeOfType<long>().Which.Should().Be(100L);
    }

    [Fact]
    public async Task Handle_FormulaEvaluatesToNull_KeyOmittedFromResults()
    {
        var template = CreateTemplate();
        AddFieldDirectly(template, FormField.Create(
            template.Id, "broken", FormFieldType.Formula,
            "Broken", "Broken", 1, false, 1,
            formulaJson: """{"expression":"1/0"}"""));
        template.Publish(TestUserId);
        SetupTemplate(template);

        _formulaService
            .EvaluateAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult((object?)null));

        var result = await _handler.Handle(
            new EvaluateFormulasQuery(template.PublicId, TestTenantId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Results.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_InactiveFormulaField_ExcludedFromResults()
    {
        var template = CreateTemplate();
        // Publish requires >=1 active field — keep an unrelated active Text field so the
        // deactivated Formula field is the only thing under test.
        template.AddField("keep_active", FormFieldType.Text, "Keep", "Keep", 1, false, null, TestUserId);
        AddFieldDirectly(template, FormField.Create(
            template.Id, "deactivated", FormFieldType.Formula,
            "Deactivated", "Deactivated", 2, false, 1,
            formulaJson: """{"expression":"1+1"}"""));
        var deactivated = template.Fields.Single(f => f.FieldKey == "deactivated");
        deactivated.Deactivate();
        template.Publish(TestUserId);
        SetupTemplate(template);

        var result = await _handler.Handle(
            new EvaluateFormulasQuery(template.PublicId, TestTenantId, new Dictionary<string, object?>()),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Results.Should().BeEmpty();
        await _formulaService.DidNotReceive().EvaluateAsync(
            Arg.Any<string>(), Arg.Any<IDictionary<string, object?>>(), Arg.Any<CancellationToken>());
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private void SetupTemplate(FormTemplate template) =>
        _templateRepo
            .GetByPublicIdWithFieldsAsync(template.PublicId, TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(template));

    private FormTemplate CreateTemplate() =>
        FormTemplate.Create(
            _guidGen, TestTenantId, "Template", "Biểu mẫu", "template_code", StorageMode.Json, TestUserId);

    /// <summary>Adds a pre-built FormField via reflection (mirrors SubmitFormCommandHandlerTests helper) —
    /// needed for Formula fields, which require FormulaJson that the AddField() sugar method doesn't take.</summary>
    private static void AddFieldDirectly(FormTemplate template, FormField field)
    {
        var fieldsField = typeof(FormTemplate)
            .GetField("_fields", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var list = (List<FormField>)fieldsField.GetValue(template)!;
        list.Add(field);
    }
}
