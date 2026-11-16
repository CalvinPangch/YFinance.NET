using FluentAssertions;
using Xunit;
using YFinance.NET.Implementation.Utils;

namespace YFinance.NET.Tests.Unit.Utils;

public class TimezoneHelperTests
{
    private const string NewYork = "America/New_York";
    private readonly TimezoneHelper _helper = new();

    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0) =>
        new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    [Fact]
    public void FixDstIssues_IntradayBarsOnTheHour_AreNotMovedToMidnight()
    {
        // 10:00 and 11:00 New York time (EST, UTC-5)
        var timestamps = new[] { Utc(2024, 1, 10, 15), Utc(2024, 1, 10, 15, 5), Utc(2024, 1, 10, 16) };

        var result = _helper.FixDstIssues(timestamps, NewYork);

        result.Should().Equal(timestamps);
    }

    [Fact]
    public void FixDstIssues_RepeatedFallBackHour_KeepsBothOccurrences()
    {
        // 2024-11-03 01:30 EDT (05:30 UTC) and 01:30 EST (06:30 UTC)
        var timestamps = new[] { Utc(2024, 11, 3, 5, 30), Utc(2024, 11, 3, 6, 30) };

        var result = _helper.FixDstIssues(timestamps, NewYork);

        result.Should().Equal(timestamps);
        result.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void FixDstIssues_DailyBarJustBeforeLocalMidnight_MovesToNextLocalMidnight()
    {
        // Sao Paulo (UTC-3): 23:00 local on Jan 9 should be 00:00 local on Jan 10 (03:00 UTC)
        var timestamps = new[] { Utc(2022, 1, 10, 2) };

        var result = _helper.FixDstIssues(timestamps, "America/Sao_Paulo");

        result[0].Should().Be(Utc(2022, 1, 10, 3));
        result[0].Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public void FixDstIssues_DailyBarAtMarketOpen_IsUnchanged()
    {
        var timestamps = new[] { Utc(2024, 1, 10, 14, 30), Utc(2024, 1, 11, 8) };

        _helper.FixDstIssues(timestamps, NewYork).Should().Equal(timestamps);
        _helper.FixDstIssues(new[] { Utc(2024, 1, 11, 8) }, "Europe/London")
            .Should().Equal(Utc(2024, 1, 11, 8));
    }

    [Fact]
    public void ConvertToExchangeTime_UtcInput_ConvertsWithDst()
    {
        _helper.ConvertToExchangeTime(Utc(2024, 1, 10, 15), NewYork).Should().Be(new DateTime(2024, 1, 10, 10, 0, 0));
        _helper.ConvertToExchangeTime(Utc(2024, 7, 10, 15), NewYork).Should().Be(new DateTime(2024, 7, 10, 11, 0, 0));
    }

    [Fact]
    public void ConvertToExchangeTime_LocalKindInput_IsConvertedToUtcFirst()
    {
        var utc = Utc(2024, 1, 10, 15);
        var local = utc.ToLocalTime();

        _helper.ConvertToExchangeTime(local, NewYork).Should().Be(_helper.ConvertToExchangeTime(utc, NewYork));
    }

    [Fact]
    public void GetTimezoneOffset_LocalKindInput_MatchesUtcInstant()
    {
        // Choose an instant just after the US spring-forward so a wrong reinterpretation would change the offset
        var utc = Utc(2024, 3, 10, 7, 30);

        _helper.GetTimezoneOffset(utc.ToLocalTime(), NewYork).Should().Be(_helper.GetTimezoneOffset(utc, NewYork));
        _helper.GetTimezoneOffset(utc, NewYork).Should().Be(TimeSpan.FromHours(-4));
    }

    [Fact]
    public void ConvertToExchangeTime_WindowsTimezoneId_IsMapped()
    {
        _helper.ConvertToExchangeTime(Utc(2024, 1, 10, 15), "Eastern Standard Time")
            .Should().Be(new DateTime(2024, 1, 10, 10, 0, 0));
        _helper.GetTimezoneOffset(Utc(2024, 1, 10), "Central Europe Standard Time")
            .Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void ConvertToExchangeTime_UnknownTimezone_Throws()
    {
        var act = () => _helper.ConvertToExchangeTime(Utc(2024, 1, 10), "Not/AZone");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ConvertToExchangeTime_EmptyTimezone_UsesUtc()
    {
        _helper.ConvertToExchangeTime(Utc(2024, 1, 10, 15), "").Should().Be(new DateTime(2024, 1, 10, 15, 0, 0));
    }
}
