using System;

namespace ETL_SQL.Core.Data;
/// <summary>
/// Unified extensions for handling null and DBNull.Value consistently across the engine.
/// </summary>
public static class NullExtensions
{
    /// <summary>
    /// Returns true if the value is null or DBNull.Value.
    /// </summary>
    public static bool IsNull(this object? value)
    {
        return value == null || value == DBNull.Value;
    }

    /// <summary>
    /// Normalizes a value by converting DBNull.Value to null.
    /// </summary>
    public static object? OrNull(this object? value)
    {
        return value == DBNull.Value ? null : value;
    }

    /// <summary>
    /// Convers a null value to DBNull.Value for database providers.
    /// </summary>
    public static object ToDbNull(this object? value)
    {
        return value ?? DBNull.Value;
    }
}
