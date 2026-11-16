using YFinance.NET.Interfaces.Utils;

namespace YFinance.NET.Implementation.Utils;

/// <summary>
/// Advanced price repair implementation for Yahoo Finance anomalies.
/// Handles 100x currency errors, missing data, bad splits, and outliers.
/// Based on Python yfinance repair algorithms.
/// </summary>
public class PriceRepair : IPriceRepair
{
    private const decimal OutlierThreshold = 5m; // 5x deviation from median
    private const int MedianWindowSize = 5; // Window size for median filter

    /// <summary>
    /// Detects and repairs 100x currency conversion errors in price data.
    /// </summary>
    /// <remarks>
    /// Runs of any length that are 100x off (e.g. a listing switching between pounds and pence) are
    /// rescaled to the level of the most recent data, which is assumed to be correct. Remaining isolated
    /// 100x spikes or dips are then fixed against a rolling median.
    /// </remarks>
    /// <param name="prices">Array of prices to repair.</param>
    /// <returns>Repaired prices with 100x errors corrected.</returns>
    public decimal[] Repair100xErrors(decimal[] prices)
    {
        ArgumentNullException.ThrowIfNull(prices);

        if (prices.Length == 0)
            return prices;

        var repaired = FixUnitSwitches(prices);
        return FixSporadic100xErrors(repaired);
    }

    /// <summary>
    /// Detects and fixes random sporadic 100x errors using median filtering.
    /// Mirrors Python yfinance's _fix_unit_random_mixups method.
    /// </summary>
    private static decimal[] FixSporadic100xErrors(decimal[] prices)
    {
        if (prices.Length < MedianWindowSize)
            return prices;

        var repaired = (decimal[])prices.Clone();
        var medianFiltered = CalculateMedianFilter(prices, MedianWindowSize);

        for (int i = 0; i < repaired.Length; i++)
        {
            if (repaired[i] <= 0m || medianFiltered[i] <= 0m)
                continue;

            repaired[i] *= GetHundredXCorrection(repaired[i] / medianFiltered[i]);
        }

        return repaired;
    }

    /// <summary>
    /// Detects and fixes block 100x errors (currency unit switches), including multiple switches and
    /// runs of any length. Each bar-to-bar jump of ~100x (or ~1/100x) moves the "unit level"; every bar
    /// is then rescaled to the level of the last bar. Mirrors Python yfinance's _fix_unit_switch method.
    /// </summary>
    private static decimal[] FixUnitSwitches(decimal[] prices)
    {
        var repaired = (decimal[])prices.Clone();
        if (prices.Length < 2)
            return repaired;

        // level[i]: how many factors of 100 bar i sits above the first valid bar.
        var levels = new int[prices.Length];
        var currentLevel = 0;
        var lastValidIndex = -1;
        var anySwitch = false;

        for (int i = 0; i < prices.Length; i++)
        {
            if (prices[i] > 0m && lastValidIndex >= 0)
            {
                var correction = GetHundredXCorrection(prices[i] / prices[lastValidIndex]);
                if (correction < 1m)
                {
                    currentLevel++;
                    anySwitch = true;
                }
                else if (correction > 1m)
                {
                    currentLevel--;
                    anySwitch = true;
                }
            }

            if (prices[i] > 0m)
                lastValidIndex = i;

            levels[i] = currentLevel;
        }

        if (!anySwitch)
            return repaired;

        var referenceLevel = currentLevel;
        for (int i = 0; i < repaired.Length; i++)
        {
            if (repaired[i] <= 0m)
                continue;

            var diff = levels[i] - referenceLevel;
            for (int k = 0; k < Math.Abs(diff); k++)
            {
                repaired[i] = diff > 0 ? repaired[i] / 100m : repaired[i] * 100m;
            }
        }

        return repaired;
    }

    /// <summary>
    /// Returns the multiplier that corrects a value whose ratio to its reference is ~100x (0.01)
    /// or ~1/100x (100), or 1 when the ratio does not indicate a 100x error.
    /// The ratio is rounded to the nearest 20 to classify it (mirrors the Python approach).
    /// </summary>
    private static decimal GetHundredXCorrection(decimal ratio)
    {
        if (ratio <= 0m)
            return 1m;

        if (RoundToNearest20(ratio) == 100m)
            return 0.01m;

        if (RoundToNearest20(1m / ratio) == 100m)
            return 100m;

        return 1m;
    }

    /// <summary>
    /// Rounds ratio to nearest 20 for classification (mirrors Python approach).
    /// </summary>
    private static decimal RoundToNearest20(decimal value)
    {
        return Math.Round(value / 20m) * 20m;
    }

    /// <summary>
    /// Calculates median filter output for price series.
    /// </summary>
    private static decimal[] CalculateMedianFilter(decimal[] prices, int windowSize)
    {
        var result = new decimal[prices.Length];
        int halfWindow = windowSize / 2;

        for (int i = 0; i < prices.Length; i++)
        {
            int start = Math.Max(0, i - halfWindow);
            int end = Math.Min(prices.Length - 1, i + halfWindow);
            var window = prices.Skip(start).Take(end - start + 1).Where(p => p > 0m).ToArray();

            result[i] = window.Length > 0 ? CalculateMedian(window) : prices[i];
        }

        return result;
    }

    /// <summary>
    /// Repairs zero or negative prices by forward and backward filling with the last good value.
    /// </summary>
    /// <param name="prices">Array of prices to repair.</param>
    /// <returns>Repaired prices with zeros filled.</returns>
    public decimal[] RepairZeroValues(decimal[] prices)
    {
        ArgumentNullException.ThrowIfNull(prices);

        if (prices.Length == 0)
            return prices;

        var repaired = (decimal[])prices.Clone();

        // Forward fill: Use last known good value
        decimal lastGood = 0m;
        for (int i = 0; i < repaired.Length; i++)
        {
            if (repaired[i] > 0m)
            {
                lastGood = repaired[i];
            }
            else if (repaired[i] <= 0m && lastGood > 0m)
            {
                repaired[i] = lastGood;
            }
        }

        // Backward fill: For any remaining zeros at the start
        decimal firstGood = 0m;
        for (int i = repaired.Length - 1; i >= 0; i--)
        {
            if (repaired[i] > 0m)
            {
                firstGood = repaired[i];
            }
            else if (repaired[i] <= 0m && firstGood > 0m)
            {
                repaired[i] = firstGood;
            }
        }

        return repaired;
    }

    /// <summary>
    /// Detects and repairs errors related to stock splits.
    /// </summary>
    /// <param name="prices">Array of prices to repair.</param>
    /// <param name="splitDates">Dictionary mapping split dates to split ratios.</param>
    /// <returns>Repaired prices with split errors corrected.</returns>
    public decimal[] RepairBadSplits(decimal[] prices, Dictionary<DateTime, decimal> splitDates)
    {
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(splitDates);

        if (prices.Length == 0 || splitDates.Count == 0)
            return prices;

        var repaired = (decimal[])prices.Clone();
        var ratios = splitDates.Values.Where(r => r > 0m).Distinct().ToArray();

        if (ratios.Length == 0)
            return repaired;

        for (int i = 1; i < repaired.Length; i++)
        {
            var prev = repaired[i - 1];
            var current = repaired[i];

            if (prev <= 0m || current <= 0m)
                continue;

            var ratio = prev / current;
            foreach (var splitRatio in ratios)
            {
                if (IsClose(ratio, splitRatio) || IsClose(ratio, 1m / splitRatio))
                {
                    var adjustment = IsClose(ratio, splitRatio) ? splitRatio : 1m / splitRatio;
                    for (int j = 0; j < i; j++)
                    {
                        repaired[j] /= adjustment;
                    }
                    break;
                }
            }
        }

        return repaired;
    }

    /// <summary>
    /// Applies comprehensive price repairs including zero values, 100x errors, outliers, and splits.
    /// </summary>
    /// <param name="prices">Array of prices to repair.</param>
    /// <param name="dates">Bar dates in the exchange's local timezone, corresponding to prices.</param>
    /// <param name="splitDates">Optional dictionary mapping exchange-local split dates to split ratios.</param>
    /// <returns>Fully repaired prices.</returns>
    public decimal[] RepairPrices(decimal[] prices, DateTime[] dates, Dictionary<DateTime, decimal>? splitDates = null)
    {
        ArgumentNullException.ThrowIfNull(prices);
        ArgumentNullException.ThrowIfNull(dates);

        var repaired = RepairZeroValues(prices);

        // Undo known splits first so a genuine 100:1 (reverse) split is not mistaken for a currency switch
        // and the outlier filter does not see split steps.
        if (splitDates != null && splitDates.Count > 0)
            repaired = AdjustForKnownSplits(repaired, dates, splitDates);

        repaired = Repair100xErrors(repaired);
        repaired = RepairOutliers(repaired);

        if (splitDates != null && splitDates.Count > 0)
            repaired = RepairBadSplits(repaired, splitDates);

        return repaired;
    }

    /// <summary>
    /// Detects and repairs outliers using median filtering.
    /// Uses a rolling window approach similar to Python yfinance.
    /// </summary>
    private static decimal[] RepairOutliers(decimal[] prices)
    {
        if (prices.Length < MedianWindowSize)
            return prices;

        var repaired = (decimal[])prices.Clone();
        var medianFiltered = CalculateMedianFilter(prices, MedianWindowSize);

        for (int i = 0; i < repaired.Length; i++)
        {
            if (repaired[i] <= 0m || medianFiltered[i] <= 0m)
                continue;

            var ratio = repaired[i] / medianFiltered[i];

            // If price deviates more than OutlierThreshold (5x) from local median, replace with median
            if (ratio > OutlierThreshold || ratio < 1m / OutlierThreshold)
            {
                repaired[i] = medianFiltered[i];
            }
        }

        return repaired;
    }

    /// <summary>
    /// Calculates the median of an array of decimals.
    /// </summary>
    private static decimal CalculateMedian(decimal[] values)
    {
        if (values.Length == 0)
            return 0m;

        var sorted = (decimal[])values.Clone();
        Array.Sort(sorted);

        int mid = sorted.Length / 2;
        if (sorted.Length % 2 == 0)
            return (sorted[mid - 1] + sorted[mid]) / 2m;
        else
            return sorted[mid];
    }

    private static decimal[] AdjustForKnownSplits(decimal[] prices, DateTime[] dates, Dictionary<DateTime, decimal> splitDates)
    {
        if (prices.Length == 0 || dates.Length == 0 || splitDates.Count == 0)
            return prices;

        var repaired = (decimal[])prices.Clone();

        foreach (var split in splitDates.OrderBy(kvp => kvp.Key))
        {
            var splitDate = split.Key.Date;
            var splitRatio = split.Value;
            if (splitRatio <= 0m)
                continue;

            // First bar trading on or after the split date (the split may fall on a non-trading day).
            var index = Array.FindIndex(dates, date => date.Date >= splitDate);
            if (index <= 0 || index >= repaired.Length)
                continue;

            var prev = repaired[index - 1];
            var current = repaired[index];
            if (prev <= 0m || current <= 0m)
                continue;

            var ratio = prev / current;
            if (!IsClose(ratio, splitRatio) && !IsClose(ratio, 1m / splitRatio))
                continue;

            var adjustment = IsClose(ratio, splitRatio) ? splitRatio : 1m / splitRatio;
            for (int i = 0; i < index; i++)
            {
                repaired[i] /= adjustment;
            }
        }

        return repaired;
    }

    private static bool IsClose(decimal left, decimal right)
    {
        var diff = Math.Abs(left - right);
        return diff <= right * 0.05m;
    }
}
