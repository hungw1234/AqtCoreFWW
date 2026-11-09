using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Queries.GetFormFieldOptions;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using FluentAssertions;
using FluentResults;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Forms.Application.Tests.Queries;

/// <summary>
/// P4 (cascade v1) — GetFormFieldOptionsQueryHandler must forward the query's ParentValues to the
/// data-source resolver (the execution path GET options?p[...] → query → resolver). Backward: a query
/// with no ParentValues (the old two-arg call) must resolve exactly as today (null forwarded). Static
/// (Options-backed) fields are unaffected — they never reach the resolver.
/// </summary>
public sealed class GetFormFieldOptionsQueryHandlerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    private readonly IFormFieldRepository _fields = Substitute.For<IFormFieldRepository>();
    private readonly IDataSourceResolverFactory _factory = Substitute.For<IDataSourceResolverFactory>();
    private readonly IDataSourceResolver _resolver = Substitute.For<IDataSourceResolver>();
    private readonly GetFormFieldOptionsQueryHandler _sut;

    public GetFormFieldOptionsQueryHandlerTests()
    {
        _factory.Create(Arg.Any<FormFieldType>()).Returns(_resolver);
        _resolver.ResolveAsync(
                Arg.Any<string>(), Arg.Any<Guid>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DataSourceOptionDto> { new("1", "L1") });
        _sut = new GetFormFieldOptionsQueryHandler(_fields, _factory);
    }

    private static FormField ExternalRefField() =>
        FormField.Create(
            formTemplateId: 1, fieldKey: "xa", type: FormFieldType.ExternalRef,
            labelVi: "Xã", labelEn: "Ward", displayOrder: 2, required: false, addedInVersion: 1,
            dataSourceJson: """{"ApiUrl":"https://api.example.com/wards","LabelJsonPath":"n","ValueJsonPath":"id"}""");

    [Fact]
    public async Task Handle_WithParentValues_ForwardsThemToResolver()
    {
        _fields.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(ExternalRefField()));

        var parents = new Dictionary<string, string> { ["provinceCode"] = "HN" };
        var result = await _sut.Handle(
            new GetFormFieldOptionsQuery(1, TenantId, parents), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _resolver.Received(1).ResolveAsync(
            Arg.Any<string>(), TenantId,
            Arg.Is<IReadOnlyDictionary<string, string>?>(p => p != null && p["provinceCode"] == "HN"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutParentValues_ForwardsNull_BackwardCompatible()
    {
        _fields.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(ExternalRefField()));

        var result = await _sut.Handle(
            new GetFormFieldOptionsQuery(1, TenantId), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _resolver.Received(1).ResolveAsync(
            Arg.Any<string>(), TenantId,
            Arg.Is<IReadOnlyDictionary<string, string>?>(p => p == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StaticOptionsField_NeverReachesResolver()
    {
        // A field carrying an explicit static Options list — the handler maps these directly and
        // never consults the resolver, regardless of field type.
        var staticField = FormField.Create(
            formTemplateId: 1, fieldKey: "color", type: FormFieldType.EnumRef,
            labelVi: "Màu", labelEn: "Color", displayOrder: 1, required: false, addedInVersion: 1);
        staticField.AddOption(FormFieldOption.Create(staticField.Id, "r", "Đỏ", "Red", 1));
        _fields.GetByIdAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Result.Ok(staticField));

        var result = await _sut.Handle(
            new GetFormFieldOptionsQuery(1, TenantId, new Dictionary<string, string> { ["x"] = "y" }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(o => o.Value == "r");
        await _resolver.DidNotReceive().ResolveAsync(
            Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }
}
