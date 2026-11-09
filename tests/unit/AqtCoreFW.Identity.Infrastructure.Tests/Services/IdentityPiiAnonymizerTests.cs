using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Services;

/// <summary>
/// Unit tests for IdentityPiiAnonymizer.
/// Uses mocked UserManager — no real DB or Identity store required.
/// </summary>
public sealed class IdentityPiiAnonymizerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid SubjectId = Guid.NewGuid();

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<IdentityPiiAnonymizer> _logger =
        Substitute.For<ILogger<IdentityPiiAnonymizer>>();
    private readonly IdentityPiiAnonymizer _sut;

    public IdentityPiiAnonymizerTests()
    {
        var store = Substitute.For<IUserStore<ApplicationUser>,
            IUserEmailStore<ApplicationUser>>();

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

        _sut = new IdentityPiiAnonymizer(_userManager, _logger);
    }

    [Fact]
    public void ModuleName_ReturnsIdentity()
    {
        _sut.ModuleName.Should().Be("Identity");
    }

    [Fact]
    public async Task AnonymizeAsync_UserNotFound_ReturnsOkWithZeroRecords()
    {
        _userManager.FindByIdAsync(SubjectId.ToString()).Returns((ApplicationUser?)null);

        var result = await _sut.AnonymizeAsync(SubjectId, TenantId, null);

        result.Succeeded.Should().BeTrue();
        result.RecordsAnonymized.Should().Be(0);
    }

    [Fact]
    public async Task AnonymizeAsync_AlreadyAnonymized_SkipsUpdateAndReturnsOkWithZero()
    {
        var user = new ApplicationUser
        {
            Id = SubjectId,
            Email = $"anonymized_abc123@deleted.local"
        };
        _userManager.FindByIdAsync(SubjectId.ToString()).Returns(user);

        var result = await _sut.AnonymizeAsync(SubjectId, TenantId, null);

        result.Succeeded.Should().BeTrue();
        result.RecordsAnonymized.Should().Be(0);
        await _userManager.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task AnonymizeAsync_HappyPath_AnonymizesAllPiiFieldsAndReturnsOkWithOne()
    {
        var user = new ApplicationUser
        {
            Id = SubjectId,
            FullName = "Nguyen Van A",
            UserName = "nguyen.van.a",
            Email = "nguyen.van.a@agency.gov.vn",
            PhoneNumber = "0901234567",
            DepartmentCode = "IT",
            IsActive = true
        };
        _userManager.FindByIdAsync(SubjectId.ToString()).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        var result = await _sut.AnonymizeAsync(SubjectId, TenantId, null);

        result.Succeeded.Should().BeTrue();
        result.RecordsAnonymized.Should().Be(1);

        // Verify PII fields wiped
        user.FullName.Should().Be("[DA AN DANH]");
        user.Email.Should().EndWith("@deleted.local");
        user.UserName.Should().StartWith("deleted_");
        user.PhoneNumber.Should().BeNull();
        user.DepartmentCode.Should().BeNull();
        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task AnonymizeAsync_UpdateFails_ReturnsFailWithErrorMessage()
    {
        var user = new ApplicationUser
        {
            Id = SubjectId,
            Email = "active@test.gov.vn"
        };
        _userManager.FindByIdAsync(SubjectId.ToString()).Returns(user);
        _userManager.UpdateAsync(user).Returns(
            IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure", Description = "Concurrency stamp mismatch" }));

        var result = await _sut.AnonymizeAsync(SubjectId, TenantId, null);

        result.Succeeded.Should().BeFalse();
        result.ErrorMessage.Should().Contain("Concurrency stamp mismatch");
    }

    [Fact]
    public async Task AnonymizeAsync_PlaceholderEmailUsesSubjectIdHex()
    {
        var user = new ApplicationUser { Id = SubjectId, Email = "active@test.gov.vn" };
        _userManager.FindByIdAsync(SubjectId.ToString()).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        await _sut.AnonymizeAsync(SubjectId, TenantId, null);

        var hex8 = SubjectId.ToString("N")[..8];
        user.Email.Should().Be($"anonymized_{hex8}@deleted.local");
        user.UserName.Should().Be($"deleted_{hex8}");
    }
}
