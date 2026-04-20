using System;

namespace ETL_SQL.Common
{
    /// <summary>
    /// Represents a range with a minimum and maximum value.
    /// Used for the MINMAX data type in ETL-SQL.
    /// </summary>
    public record MinMaxValue(object? Min = null, object? Max = null)
    {
        public override string ToString() => $"({Min?.ToString() ?? "NULL"}, {Max?.ToString() ?? "NULL"})";
    }
}
