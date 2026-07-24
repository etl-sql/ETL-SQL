using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace ETL_SQL.Core.Quality;

/// <summary>Time units supported by <c>WITH (RETENTION = '…')</c> on quarantine/warn targets.
/// Calendar-fuzzy units (months, years) are deliberately excluded — pruning compares against a
/// <see cref="TimeSpan"/> cutoff.</summary>
public enum RetentionUnit { Minutes, Hours, Days, Weeks }

/// <summary>
/// A parsed retention window such as <c>'30 DAYS'</c>. The engine prunes quarantine/warn rows
/// older than this interval at the end of each run.
/// </summary>
public sealed partial record RetentionInterval(int Amount, RetentionUnit Unit)
{
    public TimeSpan ToTimeSpan() => Unit switch
    {
        RetentionUnit.Minutes => TimeSpan.FromMinutes(Amount),
        RetentionUnit.Hours => TimeSpan.FromHours(Amount),
        RetentionUnit.Days => TimeSpan.FromDays(Amount),
        _ => TimeSpan.FromDays(7d * Amount)
    };

    /// <summary>Parses <c>'&lt;n&gt; MINUTES|HOURS|DAYS|WEEKS'</c> (singular accepted, any case).</summary>
    public static bool TryParse(string? text, out RetentionInterval? interval)
    {
        interval = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var match = IntervalRegex().Match(text.Trim());
        if (!match.Success) return false;

        var amount = int.Parse(match.Groups["amount"].Value, CultureInfo.InvariantCulture);
        if (amount <= 0) return false;

        var unit = match.Groups["unit"].Value.ToUpperInvariant() switch
        {
            "MINUTE" or "MINUTES" => RetentionUnit.Minutes,
            "HOUR" or "HOURS" => RetentionUnit.Hours,
            "DAY" or "DAYS" => RetentionUnit.Days,
            _ => RetentionUnit.Weeks
        };
        interval = new RetentionInterval(amount, unit);
        return true;
    }

    public override string ToString() => $"{Amount} {Unit.ToString().ToUpperInvariant()}";

    [GeneratedRegex(@"^(?<amount>\d{1,9})\s+(?<unit>MINUTES?|HOURS?|DAYS?|WEEKS?)$", RegexOptions.IgnoreCase)]
    private static partial Regex IntervalRegex();
}
