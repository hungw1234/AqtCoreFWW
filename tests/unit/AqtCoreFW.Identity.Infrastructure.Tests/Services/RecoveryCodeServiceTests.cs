using AqtCoreFW.Identity.Domain.Entities;
using AqtCoreFW.Identity.Infrastructure.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using NSubstitute;
using System.Text.Json;
using Xunit;

namespace AqtCoreFW.Identity.Infrastructure.Tests.Services;

/// <summary>
/// TDD-first unit tests for RecoveryCodeService (QĐ742 MFA recovery codes).
/// No real DB — uses mocked UserManager, verifies entropy + BCrypt contract.
/// </summary>
public sealed class RecoveryCodeServiceTests
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<RecoveryCodeService> _logger;
    private readonly RecoveryCodeService _sut;

    public RecoveryCodeServiceTests()
    {
        var store = Substitute.For<IUserStore<ApplicationUser>, IUserEmailStore<ApplicationUser>>();

        _userManager = Substitute.For<UserManager<ApplicationUser>>(
            store,
            Substitute.For<Microsoft.Extensions.Options.IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<ApplicationUser>>(),
            Array.Empty<IUserValidator<ApplicationUser>>(),
            Array.Empty<IPasswordValidator<ApplicationUser>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<ApplicationUser>>>());

        _logger = Substitute.For<ILogger<RecoveryCodeService>>();
        _sut = new RecoveryCodeService(_userManager, _logger);
    }

    // ── GenerateAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GenerateAsync_Returns16PlaintextCodes()
    {
        var (codes, _) = await _sut.GenerateAsync();
        codes.Should().HaveCount(16);
    }

    [Fact]
    public async Task GenerateAsync_EachCodeIs8CharAlphanumericUppercase()
    {
        var (codes, _) = await _sut.GenerateAsync();

        foreach (var code in codes)
        {
            code.Should().HaveLength(8);
            code.Should().MatchRegex("^[A-Z2-9]{8}$",
                "codes use uppercase + digits 2-9, avoiding ambiguous 0/1/O/I");
        }
    }

    [Fact]
    public async Task GenerateAsync_NoDuplicatesWithinBatch()
    {
        var (codes, _) = await _sut.GenerateAsync();
        codes.Should().OnlyHaveUniqueItems("each code in a batch must be unique");
    }

    [Fact]
    public void GenerateCodes_NoDuplicatesAcross1000Batches_ViaReflection()
    {
        // Access the internal GenerateCode via reflection to test entropy without BCrypt cost.
        // BCrypt work-factor 11 × 16 codes × 1000 iterations = infeasibly slow in CI.
        // The entropy test here validates: 32^8 ≈ 1.1e12 code space → birthday collision
        // probability at 16k draws ≈ negligible (< 1e-7). Hard fail = RNG or alphabet bug.
        var generateCode = typeof(RecoveryCodeService)
            .GetMethod("GenerateCode",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var all = new List<string>(16_000);
        for (var i = 0; i < 1000; i++)
        {
            for (var j = 0; j < 16; j++)
            {
                var code = (string)generateCode.Invoke(null, null)!;
                all.Add(code);
            }
        }

        var collisions = all.Count - all.Distinct().Count();
        collisions.Should().Be(0, "no collisions across 16,000 codes from 32^8 space");
    }

    [Fact]
    public async Task GenerateAsync_HashedJsonArrayIsBCryptFormat()
    {
        var (codes, hashedJson) = await _sut.GenerateAsync();

        var hashes = JsonSerializer.Deserialize<string[]>(hashedJson);
        hashes.Should().NotBeNull().And.HaveCount(16);

        foreach (var hash in hashes!)
        {
            hash.Should().StartWith("$2a$11$",
                "BCrypt work-factor 11, version 2a per amendment §1");
        }
    }

    [Fact]
    public async Task GenerateAsync_HashesAreOneWay_PlaintextNotRecoverable()
    {
        var (codes, hashedJson) = await _sut.GenerateAsync();
        var hashes = JsonSerializer.Deserialize<string[]>(hashedJson)!;

        // Verify hash matches corresponding plaintext (not just random bytes)
        for (var i = 0; i < codes.Length; i++)
        {
            BCrypt.Net.BCrypt.Verify(codes[i], hashes[i]).Should().BeTrue(
                $"hash[{i}] must verify against plaintext[{i}]");
        }

        // Verify hash does NOT match a different plaintext
        BCrypt.Net.BCrypt.Verify("XXXXXXXX", hashes[0]).Should().BeFalse(
            "hash must not verify against arbitrary input");
    }

    // ── ValidateAndConsumeAsync ──────────────────────────────────────────────

    [Fact]
    public async Task ValidateAndConsumeAsync_ValidCode_ReturnsTrueAndRemovesHash()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var (codes, hashedJson) = await _sut.GenerateAsync();
        var user = new ApplicationUser { Id = userId, RecoveryCodesHash = hashedJson };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        // Act
        var result = await _sut.ValidateAndConsumeAsync(userId, codes[0]);

        // Assert
        result.Should().BeTrue("a matching code must validate");
        var remaining = JsonSerializer.Deserialize<string[]>(user.RecoveryCodesHash!)!;
        remaining.Should().HaveCount(15, "consumed hash is removed from the list");
        await _userManager.Received(1).UpdateAsync(user);
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_SameCodeSecondTime_ReturnsFalse()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var (codes, hashedJson) = await _sut.GenerateAsync();
        var user = new ApplicationUser { Id = userId, RecoveryCodesHash = hashedJson };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);
        _userManager.UpdateAsync(user).Returns(IdentityResult.Success);

        // First consume
        await _sut.ValidateAndConsumeAsync(userId, codes[0]);

        // Act — second attempt with same code
        var second = await _sut.ValidateAndConsumeAsync(userId, codes[0]);

        // Assert
        second.Should().BeFalse("code is one-time use");
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_InvalidCode_ReturnsFalseWithoutThrowing()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var (_, hashedJson) = await _sut.GenerateAsync();
        var user = new ApplicationUser { Id = userId, RecoveryCodesHash = hashedJson };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);

        // Act
        var result = await _sut.ValidateAndConsumeAsync(userId, "BADCODE1");

        // Assert
        result.Should().BeFalse("invalid code must not match");
        await _userManager.DidNotReceive().UpdateAsync(Arg.Any<ApplicationUser>());
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_UserNotFound_ReturnsFalse()
    {
        _userManager.FindByIdAsync(Arg.Any<string>()).Returns((ApplicationUser?)null);

        var result = await _sut.ValidateAndConsumeAsync(Guid.NewGuid(), "ANYCODE1");

        result.Should().BeFalse("missing user → false, no throw");
    }

    [Fact]
    public async Task ValidateAndConsumeAsync_NullRecoveryCodesHash_ReturnsFalse()
    {
        var userId = Guid.NewGuid();
        var user = new ApplicationUser { Id = userId, RecoveryCodesHash = null };
        _userManager.FindByIdAsync(userId.ToString()).Returns(user);

        var result = await _sut.ValidateAndConsumeAsync(userId, "ANYCODE1");

        result.Should().BeFalse("no codes stored → always false");
    }
}
