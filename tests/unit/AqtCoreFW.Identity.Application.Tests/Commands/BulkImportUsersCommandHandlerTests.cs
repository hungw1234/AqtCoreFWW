using AqtCoreFW.Identity.Application.Commands.BulkImportUsers;
using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.SharedKernel.Abstractions;
using AqtCoreFW.SharedKernel.Domain.Events;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Application.Tests.Commands;

/// <summary>
/// Tests for BulkImportUsersCommandHandler.
/// UserManager.CreateAsync / FindByEmailAsync / AddToRoleAsync are virtual — mockable.
/// Covers: all-success, duplicate email skip, CreateAsync failure, empty rows.
/// NOTE: Validator (BulkImportUsersCommandValidator) is a separate FluentValidation concern;
///       tested via its own unit tests. The handler itself does not call the validator.
/// </summary>
public sealed class BulkImportUsersCommandHandlerTests
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IDomainEventPublisher _events;
    private readonly IGuidGenerator _guidGen = Substitute.For<IGuidGenerator>();
    private readonly BulkImportUsersCommandHandler _sut;

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();

    public BulkImportUsersCommandHandlerTests()
    {
        var store = Substitute.For<IUserStore<ApplicationUser>,
                                   IUserPasswordStore<ApplicationUser>>();
        _userManager = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());

        _events = Substitute.For<IDomainEventPublisher>();
        _guidGen.NewIdRandom().Returns(Guid.NewGuid());
        _sut = new BulkImportUsersCommandHandler(
            _userManager, _events, _guidGen,
            Substitute.For<ILogger<BulkImportUsersCommandHandler>>());
    }

    // --- Happy path ---

    [Fact]
    public async Task Handle_AllRowsValid_ReturnsSuccessCountMatchingRowCount()
    {
        SetupCreateSuccess();
        var cmd = BuildCommand([
            new(1, "Nguyen Van A", "a@gov.vn", "IT", null),
            new(2, "Tran Thi B",  "b@gov.vn", "HR", null)]);

        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SuccessCount.Should().Be(2);
        result.Value.FailedCount.Should().Be(0);
        result.Value.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_AllRowsValid_PublishesDomainEventPerUser()
    {
        SetupCreateSuccess();
        var cmd = BuildCommand([
            new(1, "User One", "one@gov.vn", null, null),
            new(2, "User Two", "two@gov.vn", null, null)]);

        await _sut.Handle(cmd, CancellationToken.None);

        // One PublishAsync call per successfully created user
        await _events.Received(2).PublishAsync(
            Arg.Any<IReadOnlyList<IDomainEvent>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_RowWithInitialRole_CallsAddToRoleAsync()
    {
        SetupCreateSuccess();
        var cmd = BuildCommand([new(1, "Officer", "officer@gov.vn", "IT", "GovOfficer")]);

        await _sut.Handle(cmd, CancellationToken.None);

        await _userManager.Received(1).AddToRoleAsync(
            Arg.Any<ApplicationUser>(), "GovOfficer");
    }

    // --- Duplicate email ---

    [Fact]
    public async Task Handle_DuplicateEmail_SkipsRowAndReportsError()
    {
        // SetupCreateSuccess first (Arg.Any), then override specific email after so it takes precedence
        SetupCreateSuccess();
        var existingUser = new ApplicationUser { Email = "dup@gov.vn" };
        _userManager.FindByEmailAsync("dup@gov.vn").Returns(existingUser);

        var cmd = BuildCommand([
            new(1, "Dup User",   "dup@gov.vn",  null, null),
            new(2, "New User",   "new@gov.vn",  null, null)]);

        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.Value.SuccessCount.Should().Be(1);
        result.Value.FailedCount.Should().Be(1);
        result.Value.Errors.Should().ContainSingle(e =>
            e.Email == "dup@gov.vn" && e.Reason.Contains("already exists"));
    }

    // --- CreateAsync failure ---

    [Fact]
    public async Task Handle_CreateAsyncFails_ReportsErrorAndContinues()
    {
        // First row fails, second succeeds
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);
        _userManager.CreateAsync(
            Arg.Is<ApplicationUser>(u => u.Email == "bad@gov.vn"),
            Arg.Any<string>())
            .Returns(IdentityResult.Failed(new IdentityError { Description = "Password too weak" }));
        _userManager.CreateAsync(
            Arg.Is<ApplicationUser>(u => u.Email == "ok@gov.vn"),
            Arg.Any<string>())
            .Returns(IdentityResult.Success);

        var cmd = BuildCommand([
            new(1, "Bad User", "bad@gov.vn", null, null),
            new(2, "Ok User",  "ok@gov.vn",  null, null)]);

        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.Value.SuccessCount.Should().Be(1);
        result.Value.FailedCount.Should().Be(1);
        result.Value.Errors.Should().ContainSingle(e =>
            e.Email == "bad@gov.vn" && e.Reason.Contains("Password too weak"));
    }

    // --- Empty rows (handler-level behaviour, validator not invoked here) ---

    [Fact]
    public async Task Handle_EmptyRows_ReturnsSuccessWithZeroCounts()
    {
        var cmd = BuildCommand([]);

        var result = await _sut.Handle(cmd, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.SuccessCount.Should().Be(0);
        result.Value.FailedCount.Should().Be(0);
    }

    // --- Helpers ---

    private void SetupCreateSuccess()
    {
        _userManager.FindByEmailAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);
        _userManager.CreateAsync(Arg.Any<ApplicationUser>(), Arg.Any<string>())
            .Returns(IdentityResult.Success);
    }

    private static BulkImportUsersCommand BuildCommand(IReadOnlyList<UserImportRow> rows) =>
        new(rows, TenantId, ActorId);
}
