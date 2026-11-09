using AqtCoreFW.Forms.Application.Commands.UpdateFormTemplateCode;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Application.Data;
using AqtCoreFW.SharedKernel.Errors;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for the public-Code global-uniqueness guard on <see cref="UpdateFormTemplateCodeCommandHandler"/>
/// (PUBLIC-FORMS-SETCODE-PUBLIC-RENAME-409). Renaming a <b>Draft+IsPublic=1</b> template to a Code already
/// public in ANOTHER tenant must surface a friendly 409 (<see cref="FormErrors.PublicCodeConflict"/>)
/// instead of tripping the UX_FormTemplate_PublicCode filtered unique index as a raw 500. The guard must
/// fire ONLY when a 500 is reachable: skipped for private templates (outside the IsPublic=1 index) and for
/// non-Draft templates (SetCode is Draft-only → GOV_FRM_010 before any UPDATE). The probe must target
/// <c>request.NewCode</c> (not <c>template.Code</c> — the sibling's expression) excluding self, which the
/// argument assertions below pin down. Cross-tenant visibility of the probe SQL under least-priv is proven
/// separately in PublicFormsLeastPrivilegeTests; here we assert the handler wiring + mapping + gating.
/// </summary>
public sealed class UpdateFormTemplateCodeCommandHandlerTests
{
    private readonly IFormTemplateRepository _repository = Substitute.For<IFormTemplateRepository>();
    private readonly IReadDbConnection       _db         = Substitute.For<IReadDbConnection>();
    private readonly IGuidGenerator          _guidGen    = Substitute.For<IGuidGenerator>();
    private readonly UpdateFormTemplateCodeCommandHandler _handler;

    private const string OriginalCode = "orig_code";
    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId   = Guid.NewGuid();

    public UpdateFormTemplateCodeCommandHandlerTests()
    {
        _guidGen.NewIdMonotonic().Returns(_ => Guid.NewGuid());
        _handler = new UpdateFormTemplateCodeCommandHandler(_repository, _db);
    }

    // --- Draft+public colliding cross-tenant → friendly 409, probe targets NewCode, no mutation/persist ---
    [Fact]
    public async Task Handle_PublicDraftTemplate_RenameToCrossTenantPublicCode_Returns409Conflict()
    {
        var template = CreatePublicDraftTemplate();
        SetupTemplate(template);
        // The bypass probe finds a colliding IsPublic=1 row owned by another tenant.
        _db.QueryFirstOrDefaultWithPublicFormsBypassAsync<int?>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<int?>(1));

        var result = await _handler.Handle(BuildCommand(template.PublicId, "taken_code"), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle()
              .Which.Should().BeOfType<ConflictError>()
              .Which.Message.Should().Contain(FormErrors.Codes.PublicCodeConflict);
        // Probe must target request.NewCode (NOT template.Code) excluding self — the one behaviour that
        // distinguishes this guard from the sibling; Arg.Any would let a template.Code copy-paste pass.
        await _db.Received(1).QueryFirstOrDefaultWithPublicFormsBypassAsync<int?>(
            Arg.Any<string>(),
            Arg.Is<object>(p => ProbeParamMatches(p, "taken_code", template.Id)),
            Arg.Any<CancellationToken>());
        // On conflict the Code is neither mutated nor persisted.
        template.Code.Should().Be(OriginalCode);
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<FormTemplate>(), Arg.Any<CancellationToken>());
    }

    // --- Draft+public, no collision → success; probe still targets NewCode; Code applied + persisted ---
    [Fact]
    public async Task Handle_PublicDraftTemplate_RenameToFreeCode_Succeeds()
    {
        var template = CreatePublicDraftTemplate();
        SetupTemplate(template);
        _db.QueryFirstOrDefaultWithPublicFormsBypassAsync<int?>(
                Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>())
           .Returns(Task.FromResult<int?>(null));

        var result = await _handler.Handle(BuildCommand(template.PublicId, "free_code"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        template.Code.Should().Be("free_code");
        await _db.Received(1).QueryFirstOrDefaultWithPublicFormsBypassAsync<int?>(
            Arg.Any<string>(),
            Arg.Is<object>(p => ProbeParamMatches(p, "free_code", template.Id)),
            Arg.Any<CancellationToken>());
        await _repository.Received(1).UpdateAsync(
            Arg.Is<FormTemplate>(t => t.Id == template.Id), Arg.Any<CancellationToken>());
    }

    // --- Private template → probe NEVER runs (no false 409, no bypass connection opened) ---
    [Fact]
    public async Task Handle_PrivateDraftTemplate_SkipsCrossTenantProbe_AndSucceeds()
    {
        var template = CreateDraftTemplate(); // IsPublic stays false — outside the filtered index
        SetupTemplate(template);

        var result = await _handler.Handle(BuildCommand(template.PublicId, "free_code"), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        template.Code.Should().Be("free_code");
        await _db.DidNotReceive().QueryFirstOrDefaultWithPublicFormsBypassAsync<int?>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    // --- Active+public template → probe skipped (SetCode is Draft-only → no UPDATE, no reachable 500) ---
    // Guard must not turn a "must be Draft" (GOV_FRM_010) into a misleading 409; the probe fires only
    // when the rename can actually reach the index.
    [Fact]
    public async Task Handle_ActivePublicTemplate_SkipsProbe_AndSurfacesDraftOnlyError()
    {
        var template = CreateActivePublicTemplate();
        SetupTemplate(template);

        var result = await _handler.Handle(BuildCommand(template.PublicId, "taken_code"), CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        // SetCode throws GOV_FRM_010 for a non-Draft template → mapped to a ValidationError, not a 409.
        result.Errors.Should().ContainSingle()
              .Which.Message.Should().Contain(FormErrors.Codes.InvalidTemplateStatus);
        await _db.DidNotReceive().QueryFirstOrDefaultWithPublicFormsBypassAsync<int?>(
            Arg.Any<string>(), Arg.Any<object?>(), Arg.Any<CancellationToken>());
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    private FormTemplate CreatePublicDraftTemplate()
    {
        var t = CreateDraftTemplate();
        // SetPublicAccess has no Status precondition → a Draft can be IsPublic=1 (the reachable scenario).
        t.SetPublicAccess(true, TestUserId);
        return t;
    }

    private FormTemplate CreateActivePublicTemplate()
    {
        var t = CreateDraftTemplate();
        t.AddField("field1", FormFieldType.Text, "Tên", "Name", 1, true, null, TestUserId);
        t.Publish(TestUserId);            // Draft → Active (Json mode)
        t.SetPublicAccess(true, TestUserId);
        return t;
    }

    private FormTemplate CreateDraftTemplate()
        => FormTemplate.Create(_guidGen, TestTenantId, "Template", "Biểu mẫu", OriginalCode, StorageMode.Json, TestUserId);

    private void SetupTemplate(FormTemplate template)
    {
        _repository.GetByPublicIdWithFieldsAsync(template.PublicId, TestTenantId, Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult<FormTemplate?>(template));
        // No per-tenant duplicate by default — the cross-tenant probe governs the public-Code case.
        _repository.GetByCodeAsync(Arg.Any<string>(), TestTenantId, Arg.Any<CancellationToken>())
                   .Returns(Task.FromResult<FormTemplate?>(null));
    }

    // Reflects over the anonymous probe param `new { Code, ExcludeId }` to prove the handler passes the
    // NEW code (not the template's current Code) and excludes itself by Id.
    private static bool ProbeParamMatches(object? param, string expectedCode, long expectedExcludeId)
    {
        if (param is null) return false;
        var type = param.GetType();
        var code = type.GetProperty("Code")?.GetValue(param) as string;
        var excludeId = type.GetProperty("ExcludeId")?.GetValue(param);
        return code == expectedCode && excludeId is long id && id == expectedExcludeId;
    }

    private static UpdateFormTemplateCodeCommand BuildCommand(Guid publicId, string newCode)
        => new(publicId, TestTenantId, newCode, TestUserId);
}
