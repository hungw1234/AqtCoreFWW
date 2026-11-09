using FluentAssertions;
using AqtCoreFW.SharedKernel.Domain;

namespace AqtCoreFW.SharedKernel.Tests.Domain;

/// <summary>
/// Tests ValueObject structural equality.
/// TC-SK-A008
/// </summary>
public sealed class ValueObjectTests
{
    // Concrete value object for testing
    private sealed class MoneyValue(decimal amount, string currency) : ValueObject
    {
        public decimal Amount { get; } = amount;
        public string Currency { get; } = currency;

        protected override IEnumerable<object?> GetEqualityComponents()
        {
            yield return Amount;
            yield return Currency;
        }
    }

    [Fact]
    public void ValueObject_SameComponents_AreEqual()
    {
        // TC-SK-A008: ValueObject equality comparison — same components → equal
        var a = new MoneyValue(100m, "VND");
        var b = new MoneyValue(100m, "VND");

        a.Should().Be(b);
        (a == b).Should().BeTrue();
    }

    [Fact]
    public void ValueObject_DifferentComponents_AreNotEqual()
    {
        var a = new MoneyValue(100m, "VND");
        var b = new MoneyValue(200m, "VND");

        a.Should().NotBe(b);
        (a != b).Should().BeTrue();
    }

    [Fact]
    public void ValueObject_DifferentCurrency_AreNotEqual()
    {
        var a = new MoneyValue(100m, "VND");
        var b = new MoneyValue(100m, "USD");

        a.Should().NotBe(b);
    }

    [Fact]
    public void ValueObject_SameComponents_HaveSameHashCode()
    {
        var a = new MoneyValue(100m, "VND");
        var b = new MoneyValue(100m, "VND");

        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [Fact]
    public void ValueObject_CompareWithNull_ReturnsFalse()
    {
        var a = new MoneyValue(100m, "VND");
        MoneyValue? nullValue = null;

        (a == nullValue).Should().BeFalse();
        a.Equals(null).Should().BeFalse();
    }

    [Fact]
    public void ValueObject_CompareDifferentTypes_ReturnsFalse()
    {
        var a = new MoneyValue(100m, "VND");

        a.Equals("not a money value").Should().BeFalse();
    }
}
