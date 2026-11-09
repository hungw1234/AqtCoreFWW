using AqtCoreFW.Forms.Application.Commands.CreateFormTemplate;
using AqtCoreFW.Forms.Domain.Entities;
using AqtCoreFW.Forms.Domain.Enums;
using AqtCoreFW.Forms.Domain.Errors;
using AqtCoreFW.Forms.Domain.Repositories;
using AqtCoreFW.SharedKernel.Abstractions;

namespace AqtCoreFW.Forms.Application.Tests.Commands;

/// <summary>
/// Unit tests for CreateFormTemplateCommandHandler.
/// Verifies Code uniqueness check, DTO mapping, and InvalidFormSchemaException handling.
/// </summary>
public sealed class CreateFormTemplateCommandHandlerTests
{
    private readonly IFormTemplateRepository _repository;
    private readonly IGuidGenerator _guidGenerator;
    private readonly CreateFormTemplateCommandHandler _handler;

    private static readonly Guid TestTenantId = Guid.NewGuid();
    private static readonly Guid TestUserId  = Guid.NewGuid();

    public CreateFormTemplateCommandHandlerTests()
    {
        _repository = Substitute.For<IFormTemplateRepository>();
        _guidGenerator = Substitute.For<IGuidGenerator>();

        // Default: no duplicate found
        _repository
            .GetByCodeAsync(Arg.Any<string>(), Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(null));

        // IGuidGenerator returns new Guid on each call
        _guidGenerator.NewIdMonotonic().Returns(_ => Guid.NewGuid());

        _handler = new CreateFormTemplateCommandHandler(_repository, _guidGenerator);
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_ValidCommand_ReturnsSuccessWithDto()
    {
        var command = BuildCommand();

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_ValidCommand_PersistsTemplate()
    {
        var command = BuildCommand(name: "Đơn xin việc", code: "don_xin_viec");

        await _handler.Handle(command, CancellationToken.None);

        await _repository.Received(1).AddAsync(
            Arg.Is<FormTemplate>(t =>
                t.TenantId == TestTenantId &&
                t.Code == "don_xin_viec"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ValidCommand_ReturnedDto_HasCorrectFields()
    {
        var command = BuildCommand(name: "Biểu mẫu A", nameVi: "Biểu mẫu A (VI)", code: "bieu_mau_a");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Name.Should().Be("Biểu mẫu A");
        result.Value.NameVi.Should().Be("Biểu mẫu A (VI)");
        result.Value.Code.Should().Be("bieu_mau_a");
        result.Value.TenantId.Should().Be(TestTenantId);
        result.Value.Status.Should().Be("Draft");
        result.Value.StorageMode.Should().Be(StorageMode.Json.ToString());
    }

    [Fact]
    public async Task Handle_EmptyFields_ReturnsTemplateWithNoFields()
    {
        var command = BuildCommand();

        var result = await _handler.Handle(command, CancellationToken.None);

        result.Value.Fields.Should().BeEmpty();
    }

    // --- Duplicate code guard ---

    [Fact]
    public async Task Handle_DuplicateCode_ReturnsDuplicateCodeError()
    {
        var existingTemplate = FormTemplate.Create(
            _guidGenerator, TestTenantId, "Existing", "Hiện có", "my_code",
            StorageMode.Json, TestUserId);

        _repository
            .GetByCodeAsync("my_code", TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(existingTemplate));

        var command = BuildCommand(code: "my_code");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
        result.Errors.Should().ContainSingle(e =>
            e.Message.Contains("my_code") || e.Message.Contains(FormErrors.Codes.DuplicateTemplateCode));
    }

    [Fact]
    public async Task Handle_DuplicateCode_DoesNotPersistTemplate()
    {
        var existingTemplate = FormTemplate.Create(
            _guidGenerator, TestTenantId, "Existing", "Hiện có", "taken_code",
            StorageMode.Json, TestUserId);

        _repository
            .GetByCodeAsync("taken_code", TestTenantId, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<FormTemplate?>(existingTemplate));

        var command = BuildCommand(code: "taken_code");

        await _handler.Handle(command, CancellationToken.None);

        await _repository.DidNotReceive().AddAsync(Arg.Any<FormTemplate>(), Arg.Any<CancellationToken>());
    }

    // --- InvalidFormSchemaException (invalid Code format) ---

    [Fact]
    public async Task Handle_InvalidCodeFormat_ReturnsValidationError()
    {
        // Code starting with digit violates validation in FormTemplate.Create
        var command = BuildCommand(code: "123invalid");

        var result = await _handler.Handle(command, CancellationToken.None);

        result.IsFailed.Should().BeTrue();
    }

    // --- Helpers ---

    private static CreateFormTemplateCommand BuildCommand(
        string name = "Test Template",
        string nameVi = "Biểu mẫu thử",
        string code = "test_template",
        StorageMode storageMode = StorageMode.Json) =>
        new(TestTenantId, name, nameVi, code, storageMode, TestUserId);
}
