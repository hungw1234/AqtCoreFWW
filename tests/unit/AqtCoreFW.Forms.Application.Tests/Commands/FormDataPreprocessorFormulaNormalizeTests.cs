using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AqtCoreFW.Forms.Application.Commands.SubmitForm;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using FluentAssertions;
using NSubstitute;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Pins the submit-time Formula normalization gap: real HTTP submits bind scalars as JsonElement,
/// and the Formula branch must hand the evaluator native CLR types (via the canonical
/// JsonValueNormalizer) — otherwise NCalc throws on a boxed JsonElement operand and every
/// referencing formula silently degrades to null.
/// Mirrors the endpoint-time contract proven by FormulaBatchEvaluator's tests.
/// </summary>
public sealed class FormDataPreprocessorFormulaNormalizeTests
{
    private readonly IFormulaEvaluationService _formulaService = Substitute.For<IFormulaEvaluationService>();

    private static FormField CreateFormulaField(string key = "total", string formulaJson = "{\"expr\":\"amount + 100\"}") =>
        FormField.Create(
            1L, key, FormFieldType.Formula,
            "Tổng", "Total", displayOrder: 1,
            required: false, addedInVersion: 1,
            formulaJson: formulaJson);

    [Fact]
    public async Task PreprocessData_Formula_JsonElementNumericInput_EvaluatorReceivesNativeClr()
    {
        // Arrange — 'amount' arrives as a JsonElement number, exactly as real HTTP binding produces.
        var field = CreateFormulaField();
        var amount = JsonSerializer.Deserialize<JsonElement>("42");
        var data = new Dictionary<string, object?> { ["amount"] = amount };

        IDictionary<string, object?>? capturedValues = null;
        _formulaService
            .EvaluateAsync(Arg.Any<string>(), Arg.Do<IDictionary<string, object?>>(v => capturedValues = v), Arg.Any<CancellationToken>())
            .Returns(142L);

        // Act
        var result = await FormDataPreprocessor.PreprocessDataAsync([field], data, _formulaService, CancellationToken.None);

        // Assert — evaluator saw a native CLR long, NOT a raw JsonElement (which NCalc chokes on).
        capturedValues.Should().NotBeNull();
        capturedValues!["amount"].Should().BeOfType<long>().And.Be(42L);
        capturedValues["amount"].Should().NotBeOfType<JsonElement>();

        // And the computed value is threaded into the result (non-null → key added).
        result["total"].Should().Be(142L);
    }

    [Fact]
    public async Task PreprocessData_Formula_ComputedNull_KeyOmitted()
    {
        // Null-omit contract preserved after the normalize change.
        var field = CreateFormulaField();
        var data = new Dictionary<string, object?> { ["amount"] = JsonSerializer.Deserialize<JsonElement>("5") };

        _formulaService
            .EvaluateAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>>(), Arg.Any<CancellationToken>())
            .Returns((object?)null);

        var result = await FormDataPreprocessor.PreprocessDataAsync([field], data, _formulaService, CancellationToken.None);

        result.ContainsKey("total").Should().BeFalse();
    }

    [Fact]
    public async Task PreprocessData_Formula_EmptyFormulaJson_SkipsEvaluation()
    {
        var field = CreateFormulaField(formulaJson: "");
        var data = new Dictionary<string, object?> { ["amount"] = JsonSerializer.Deserialize<JsonElement>("5") };

        var result = await FormDataPreprocessor.PreprocessDataAsync([field], data, _formulaService, CancellationToken.None);

        await _formulaService.DidNotReceive()
            .EvaluateAsync(Arg.Any<string>(), Arg.Any<IDictionary<string, object?>>(), Arg.Any<CancellationToken>());
        result.ContainsKey("total").Should().BeFalse();
    }
}
