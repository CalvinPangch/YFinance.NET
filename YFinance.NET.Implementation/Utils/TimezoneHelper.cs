using NodaTime;
using NodaTime.TimeZones;
using YFinance.NET.Interfaces.Utils;

namespace YFinance.NET.Implementation.Utils;

/// <summary>
/// Timezone helper for exchange-time conversions and Yahoo Finance DST fixes, using NodaTime.
/// Based on Python yfinance timezone handling.
/// </summary>
public class TimezoneHelper : ITimezoneHelper
{
    private static readonly IDateTimeZoneProvider TimeZoneProvider = DateTimeZoneProviders.Tzdb;

    /// <summary>
    /// Converts a UTC datetime to the specified timezone.
    /// </summary>
    /// <param name="utcDateTime">
    /// The datetime to convert. <see cref="DateTimeKind.Local"/> values are converted to UTC first;
    /// <see cref="DateTimeKind.Unspecified"/> values are treated as UTC.
    /// </param>
    /// <param name="timeZoneId">The IANA timezone ID (e.g., "America/New_York") or a Windows timezone ID.</param>
    /// <returns>The datetime in the exchange timezone, with <see cref="DateTimeKind.Unspecified"/> kind.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="timeZoneId"/> is not a recognised timezone.</exception>
    public DateTime ConvertToExchangeTime(DateTime utcDateTime, string timeZoneId)
    {
        var dateTimeZone = GetDateTimeZone(timeZoneId);
        return ToInstant(utcDateTime).InZone(dateTimeZone).ToDateTimeUnspecified();
    }

    /// <summary>
    /// Fixes Yahoo Finance DST issues in daily (or longer) bar timestamps.
    /// </summary>
    /// <remarks>
    /// Daily bars should start at local midnight or at the market open. Around DST transitions Yahoo
    /// sometimes reports them an hour or two before midnight of the intended day (e.g. 23:00 the
    /// previous day), which shifts the bar onto the wrong date. Such timestamps are moved forward to
    /// local midnight of the next day. Mirrors Python yfinance's <c>_fix_Yahoo_dst_issue</c>.
    /// This fix must only be applied to daily or longer intervals: intraday bars are left untouched
    /// by callers because a 22:00/23:00 intraday bar is legitimate.
    /// </remarks>
    /// <param name="timestamps">Array of UTC timestamps to fix.</param>
    /// <param name="timeZoneId">The IANA timezone ID.</param>
    /// <returns>Timestamps with DST issues corrected, as UTC.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="timeZoneId"/> is not a recognised timezone.</exception>
    public DateTime[] FixDstIssues(DateTime[] timestamps, string timeZoneId)
    {
        ArgumentNullException.ThrowIfNull(timestamps);

        if (timestamps.Length == 0)
            return timestamps;

        var dateTimeZone = GetDateTimeZone(timeZoneId);
        var fixedTimestamps = new DateTime[timestamps.Length];

        for (int i = 0; i < timestamps.Length; i++)
        {
            fixedTimestamps[i] = FixSingleDstIssue(timestamps[i], dateTimeZone);
        }

        return fixedTimestamps;
    }

    private static DateTime FixSingleDstIssue(DateTime timestamp, DateTimeZone dateTimeZone)
    {
        var instant = ToInstant(timestamp);
        var local = instant.InZone(dateTimeZone).LocalDateTime;

        var isPreMidnightError = local.Minute == 0 && local.Second == 0 && (local.Hour == 22 || local.Hour == 23);
        if (!isPreMidnightError)
            return instant.ToDateTimeUtc();

        // Move to the start of the next local day. AtStartOfDay handles zones where midnight is skipped.
        var nextDay = local.Date.PlusDays(1);
        return dateTimeZone.AtStartOfDay(nextDay).ToDateTimeUtc();
    }

    /// <summary>
    /// Gets the timezone offset (UTC difference) for a specific datetime in the specified timezone.
    /// </summary>
    /// <param name="dateTime">
    /// The instant to get the offset for. <see cref="DateTimeKind.Local"/> values are converted to UTC first;
    /// <see cref="DateTimeKind.Unspecified"/> values are treated as UTC.
    /// </param>
    /// <param name="timeZoneId">The IANA timezone ID.</param>
    /// <returns>The UTC offset as a TimeSpan.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="timeZoneId"/> is not a recognised timezone.</exception>
    public TimeSpan GetTimezoneOffset(DateTime dateTime, string timeZoneId)
    {
        var dateTimeZone = GetDateTimeZone(timeZoneId);
        return dateTimeZone.GetUtcOffset(ToInstant(dateTime)).ToTimeSpan();
    }

    private static Instant ToInstant(DateTime dateTime)
    {
        var utc = dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
        };

        return Instant.FromDateTimeUtc(utc);
    }

    /// <summary>
    /// Resolves an IANA or Windows timezone ID to a NodaTime zone.
    /// Empty IDs resolve to UTC; unrecognised IDs throw rather than silently falling back to UTC.
    /// </summary>
    private static DateTimeZone GetDateTimeZone(string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
            return DateTimeZone.Utc;

        var zone = TimeZoneProvider.GetZoneOrNull(timeZoneId);
        if (zone != null)
            return zone;

        var windowsMapping = TzdbDateTimeZoneSource.Default.WindowsMapping.PrimaryMapping;
        if (windowsMapping.TryGetValue(timeZoneId, out var ianaId))
        {
            zone = TimeZoneProvider.GetZoneOrNull(ianaId);
            if (zone != null)
                return zone;
        }

        throw new ArgumentException($"Unrecognised timezone ID '{timeZoneId}'.", nameof(timeZoneId));
    }
}
