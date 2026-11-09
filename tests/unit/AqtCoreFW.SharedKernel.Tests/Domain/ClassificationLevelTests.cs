using AqtCoreFW.SharedKernel.Domain;
using FluentAssertions;

namespace AqtCoreFW.SharedKernel.Tests.Domain;

/// <summary>
/// Verifies ClassificationLevel enum ordering matches QĐ742 cấp 4 security hierarchy.
/// Ordering is load-bearing: ABAC clearance checks use integer comparison (user.ClearanceLevel >= resource.ClassificationLevel).
/// </summary>
public sealed class ClassificationLevelTests
{
    [Fact]
    public void ClassificationLevel_Ordering_Matches_QD742_Security_Hierarchy()
    {
        // Each level must be strictly greater than the level below it
        ((int)ClassificationLevel.Public).Should().BeLessThan((int)ClassificationLevel.Internal);
        ((int)ClassificationLevel.Internal).Should().BeLessThan((int)ClassificationLevel.Confidential);
        ((int)ClassificationLevel.Confidential).Should().BeLessThan((int)ClassificationLevel.Secret);
        ((int)ClassificationLevel.Secret).Should().BeLessThan((int)ClassificationLevel.TopSecret);
    }

    [Fact]
    public void ClassificationLevel_Public_Has_Lowest_Value()
    {
        var allValues = Enum.GetValues<ClassificationLevel>();
        ((int)ClassificationLevel.Public).Should().Be(allValues.Cast<int>().Min());
    }

    [Fact]
    public void ClassificationLevel_TopSecret_Has_Highest_Value()
    {
        var allValues = Enum.GetValues<ClassificationLevel>();
        ((int)ClassificationLevel.TopSecret).Should().Be(allValues.Cast<int>().Max());
    }

    [Theory]
    [InlineData(ClassificationLevel.Public, 0)]
    [InlineData(ClassificationLevel.Internal, 1)]
    [InlineData(ClassificationLevel.Confidential, 2)]
    [InlineData(ClassificationLevel.Secret, 3)]
    [InlineData(ClassificationLevel.TopSecret, 4)]
    public void ClassificationLevel_ExactIntegerValues_AreStable(ClassificationLevel level, int expectedValue)
    {
        // Integer values are persisted in DB — must not be renumbered
        ((int)level).Should().Be(expectedValue);
    }

    [Fact]
    public void ClassificationLevel_HasExactlyFiveLevels()
    {
        // Guard against accidental additions that would break clearance checks
        Enum.GetValues<ClassificationLevel>().Should().HaveCount(5);
    }

    [Fact]
    public void ClearanceCheck_UserWithHigherLevel_CanAccessLowerLevelResource()
    {
        // Simulates the ABAC clearance pattern used throughout the codebase
        var userClearance = ClassificationLevel.Secret;
        var resourceClassification = ClassificationLevel.Confidential;

        (userClearance >= resourceClassification).Should().BeTrue();
    }

    [Fact]
    public void ClearanceCheck_UserWithLowerLevel_CannotAccessHigherLevelResource()
    {
        var userClearance = ClassificationLevel.Internal;
        var resourceClassification = ClassificationLevel.Confidential;

        (userClearance >= resourceClassification).Should().BeFalse();
    }
}
