using FluentAssertions;
using Xunit;
using YFinance.NET.Implementation.Utils;

namespace YFinance.NET.Tests.Unit.Utils;

public class PriceRepairTests
{
    private readonly PriceRepair _repair = new();

    [Fact]
    public void Repair100xErrors_SporadicSpike_IsDividedBy100()
    {
        var prices = new[] { 100m, 101m, 10200m, 103m, 104m, 105m };

        _repair.Repair100xErrors(prices).Should().Equal(100m, 101m, 102m, 103m, 104m, 105m);
    }

    [Fact]
    public void Repair100xErrors_SporadicDip_IsMultipliedBy100()
    {
        var prices = new[] { 100m, 101m, 1.02m, 103m, 104m, 105m };

        _repair.Repair100xErrors(prices).Should().Equal(100m, 101m, 102m, 103m, 104m, 105m);
    }

    [Fact]
    public void Repair100xErrors_UnitSwitchAwayFromMidpoint_RescalesOnlyTheWrongSegment()
    {
        // 3 bars in pence, then 7 bars in pounds (switch at index 3, not the midpoint)
        var prices = new[] { 10000m, 10100m, 10200m, 103m, 104m, 105m, 106m, 107m, 108m, 109m };

        var result = _repair.Repair100xErrors(prices);

        result.Should().Equal(100m, 101m, 102m, 103m, 104m, 105m, 106m, 107m, 108m, 109m);
    }

    [Fact]
    public void Repair100xErrors_LongBlockInTheMiddle_IsRescaled()
    {
        var prices = new[] { 100m, 101m, 10200m, 10300m, 10400m, 10500m, 106m, 107m };

        _repair.Repair100xErrors(prices).Should().Equal(100m, 101m, 102m, 103m, 104m, 105m, 106m, 107m);
    }

    [Fact]
    public void Repair100xErrors_NormalVolatility_IsUnchanged()
    {
        var prices = new[] { 100m, 120m, 90m, 150m, 80m, 110m };

        _repair.Repair100xErrors(prices).Should().Equal(prices);
    }

    [Fact]
    public void Repair100xErrors_DoesNotMutateInput()
    {
        var prices = new[] { 100m, 101m, 10200m, 103m, 104m };
        var copy = (decimal[])prices.Clone();

        _repair.Repair100xErrors(prices);

        prices.Should().Equal(copy);
    }

    [Fact]
    public void RepairZeroValues_FillsForwardAndBackward()
    {
        _repair.RepairZeroValues(new[] { 0m, 10m, 0m, 12m }).Should().Equal(10m, 10m, 10m, 12m);
    }

    [Fact]
    public void RepairPrices_KnownSplitOnNonTradingDay_AdjustsAtFirstBarAfterSplit()
    {
        // Split dated Saturday; first post-split bar is Monday.
        var dates = new[]
        {
            new DateTime(2024, 1, 4), new DateTime(2024, 1, 5),   // Thu, Fri
            new DateTime(2024, 1, 8), new DateTime(2024, 1, 9)    // Mon, Tue
        };
        var prices = new[] { 200m, 202m, 101m, 102m };
        var splits = new Dictionary<DateTime, decimal> { { new DateTime(2024, 1, 6), 2m } };

        var result = _repair.RepairPrices(prices, dates, splits);

        result.Should().Equal(100m, 101m, 101m, 102m);
    }

    [Fact]
    public void RepairPrices_Known100To1ReverseSplit_IsNotTreatedAsCurrencySwitch()
    {
        // 1:100 reverse split: price goes from ~1 to ~100. Ratio = numerator/denominator = 0.01.
        var dates = Enumerable.Range(0, 6).Select(i => new DateTime(2024, 1, 1).AddDays(i)).ToArray();
        var prices = new[] { 1.00m, 1.01m, 1.02m, 103m, 104m, 105m };
        var splits = new Dictionary<DateTime, decimal> { { dates[3], 0.01m } };

        var result = _repair.RepairPrices(prices, dates, splits);

        // Pre-split prices are restated in post-split terms; post-split prices untouched.
        result.Should().Equal(100m, 101m, 102m, 103m, 104m, 105m);
    }
}
