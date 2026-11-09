using AqtCoreFW.Forms.Application.DTOs;
using AqtCoreFW.Forms.Application.Interfaces;
using AqtCoreFW.Forms.Application.Queries.GetPublicFormFieldOptions;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace AqtCoreFW.Forms.Application.Tests.Queries;

/// <summary>
/// C1 — GetPublicFormFieldOptionsQueryHandler. Execution path: anonymous GET
/// /public/forms/{code}/fields/{fieldKey}/options → Dapper bypass id-lookup → GetPublicWithFieldsAsync
/// (tenant re-pin) → pick field by FieldKey from THIS template → resolve. The security-critical asserts
/// are (a) a fieldKey not in the resolved template's own fields → 404 (no cross-tenant/template field is
/// addressable), and (b) tenant forwarded to the resolver is the SERVER-RESOLVED one, never a caller value.
/// </summary>
public sealed class GetPublicFormFieldOptionsQueryHandlerTests
{
    private const string Code = "public_survey";
    private static readonly Guid ResolvedTenantId = Guid.NewGuid();
    private static readonly Guid CreatedBy = Guid.NewGuid();

    private readonly IReadDbConnection _db = Substitute.For<IReadDbConnection>();
    private readonly IFormTemplateRepository _templates = Substitute.For<IFormTemplateRepository>();
    private readonly IDataSourceResolverFactory _factory = Substitute.For<IDataSourceResolverFactory>();
    private readonly IDataSourceResolver _resolver = Substitute.For<IDataSourceResolver>();
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly GetPublicFormFieldOptionsQueryHandler _sut;

    public GetPublicFormFieldOptionsQueryHandlerTests()
    {
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _factory.Create(Arg.Any<FormFieldType>()).Returns(_resolver);
        _resolver.ResolveAsync(
                Arg.Any<string>(), Arg.Any<Guid>(),
                Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>())
            .Returns(new List<DataSourceOptionDto> { new("1", "L1") });
        _sut = new GetPublicFormFieldOptionsQueryHandler(_db, _templates, _factory);
    }

    // A public template carrying one ExternalRef cascade field "ward".
    private FormTemplate PublicTemplateWithWardField()
    {
        var template = FormTemplate.Create(
            _guidGen, ResolvedTenantId, "Survey", "Khảo sát", Code, StorageMode.Json, CreatedBy);
        template.AddField(
            fieldKey: "ward", type: FormFieldType.ExternalRef, labelVi: "Xã", labelEn: "Ward",
            displayOrder: 1, required: false, validationRulesJson: null, updatedBy: CreatedBy,
            dataSourceJson: """{"ApiUrl":"https://api.example.com/wards","LabelJsonPath":"n","ValueJsonPath":"id"}""");
        return template;
    }

    private void StubBypassResolves()
        => _db.QueryFirstOrDefaultWithPublicFormsBypassAsync<GetPublicFormFieldOptionsQueryHandler.TemplateIdRow>(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(new GetPublicFormFieldOptionsQueryHandler.TemplateIdRow { Id = 1, TenantId = ResolvedTenantId });

    [Fact]
    public async Task Handle_ValidCodeAndFieldKey_WithParentValues_ForwardsServerTenantAndParents()
    {
        // Build the template BEFORE the .Returns() call — FormTemplate.Create calls _guidGen inside,
        // and a substitute call nested in Returns(...) corrupts NSubstitute's pending LastCall.
        var template = PublicTemplateWithWardField();
        StubBypassResolves();
        _templates.GetPublicWithFieldsAsync(1, ResolvedTenantId, Arg.Any<CancellationToken>())
            .Returns(template);

        var parents = new Dictionary<string, string> { ["provinceCode"] = "HN" };
        var result = await _sut.Handle(
            new GetPublicFormFieldOptionsQuery(Code, "ward", parents), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        // Tenant forwarded to resolver MUST be the server-resolved one, and parents forwarded intact.
        await _resolver.Received(1).ResolveAsync(
            Arg.Any<string>(), ResolvedTenantId,
            Arg.Is<IReadOnlyDictionary<string, string>?>(p => p != null && p["provinceCode"] == "HN"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_WithoutParentValues_ForwardsNull_BackwardCompatible()
    {
        var template = PublicTemplateWithWardField();
        StubBypassResolves();
        _templates.GetPublicWithFieldsAsync(1, ResolvedTenantId, Arg.Any<CancellationToken>())
            .Returns(template);

        var result = await _sut.Handle(
            new GetPublicFormFieldOptionsQuery(Code, "ward"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        await _resolver.Received(1).ResolveAsync(
            Arg.Any<string>(), ResolvedTenantId,
            Arg.Is<IReadOnlyDictionary<string, string>?>(p => p == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FieldKeyNotInTemplate_Returns404_NoCrossTenantLeak()
    {
        // The template resolves, but the requested fieldKey is not one of ITS fields → 404.
        // This is the C1 core: an attacker-chosen key for a field of ANOTHER form is unreachable.
        var template = PublicTemplateWithWardField();
        StubBypassResolves();
        _templates.GetPublicWithFieldsAsync(1, ResolvedTenantId, Arg.Any<CancellationToken>())
            .Returns(template);

        var result = await _sut.Handle(
            new GetPublicFormFieldOptionsQuery(Code, "some_other_forms_field"), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _resolver.DidNotReceive().ResolveAsync(
            Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_CodeNotPublicOrMissing_Returns404_BeforeAnyFieldRead()
    {
        // Dapper bypass returns null (Status != Active / IsPublic = 0 / no such code) → 404, never
        // touches the template repo. IsPublic=1 is enforced in the bypass SQL WHERE (trục-4).
        _db.QueryFirstOrDefaultWithPublicFormsBypassAsync<GetPublicFormFieldOptionsQueryHandler.TemplateIdRow>(
                Arg.Any<string>(), Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns((GetPublicFormFieldOptionsQueryHandler.TemplateIdRow?)null);

        var result = await _sut.Handle(
            new GetPublicFormFieldOptionsQuery(Code, "ward"), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _templates.DidNotReceive().GetPublicWithFieldsAsync(
            Arg.Any<long>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_FieldKeyMatchesOnlyDeactivatedField_Returns404_NotReachableOnPublicSurface()
    {
        // A deactivated field must be OFF the anonymous surface — mirrors every sibling public handler's
        // IsActive filter. AddField's uniqueness guard is scoped to IsActive, so a template can hold an
        // inactive field whose key an attacker knows; resolving its data-source (and, for ExternalRef,
        // firing the outbound HTTP) for a retired field is a data-exposure. Assert 404 + resolver untouched.
        var template = FormTemplate.Create(
            _guidGen, ResolvedTenantId, "Survey", "Khảo sát", Code, StorageMode.Json, CreatedBy);
        var ward = template.AddField(
            fieldKey: "ward", type: FormFieldType.ExternalRef, labelVi: "Xã", labelEn: "Ward",
            displayOrder: 1, required: false, validationRulesJson: null, updatedBy: CreatedBy,
            dataSourceJson: """{"ApiUrl":"https://api.example.com/wards","LabelJsonPath":"n","ValueJsonPath":"id"}""");
        ward.Deactivate();

        StubBypassResolves();
        _templates.GetPublicWithFieldsAsync(1, ResolvedTenantId, Arg.Any<CancellationToken>())
            .Returns(template);

        var result = await _sut.Handle(
            new GetPublicFormFieldOptionsQuery(Code, "ward"), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        await _resolver.DidNotReceive().ResolveAsync(
            Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_StaticOptionsField_MapsDirectly_NeverReachesResolver()
    {
        var template = FormTemplate.Create(
            _guidGen, ResolvedTenantId, "Survey", "Khảo sát", Code, StorageMode.Json, CreatedBy);
        var field = template.AddField(
            fieldKey: "color", type: FormFieldType.EnumRef, labelVi: "Màu", labelEn: "Color",
            displayOrder: 1, required: false, validationRulesJson: null, updatedBy: CreatedBy);
        field.AddOption(FormFieldOption.Create(field.Id, "r", "Đỏ", "Red", 1));

        StubBypassResolves();
        _templates.GetPublicWithFieldsAsync(1, ResolvedTenantId, Arg.Any<CancellationToken>())
            .Returns(template);

        var result = await _sut.Handle(
            new GetPublicFormFieldOptionsQuery(Code, "color", new Dictionary<string, string> { ["x"] = "y" }),
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainSingle(o => o.Value == "r");
        await _resolver.DidNotReceive().ResolveAsync(
            Arg.Any<string>(), Arg.Any<Guid>(),
            Arg.Any<IReadOnlyDictionary<string, string>?>(), Arg.Any<CancellationToken>());
    }
}
