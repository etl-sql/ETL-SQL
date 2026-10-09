namespace ETL_SQL.Common;

/// <summary>
/// Canonical naming convention helper for ETL-SQL internal database schemas.
/// All internal SQL tables, columns, indexes, and constraints across ETL-SQL subsystems
/// (Portal, Orchestrator, session state, virtual tables) standardize on snake_case.
/// </summary>
public static class DatabaseNamingConvention
{
    /// <summary>
    /// Converts a PascalCase or camelCase identifier to snake_case.
    /// Preserves existing underscores and handles acronyms and numbers consistently.
    /// </summary>
    public static string ToSnakeCase(string? input)
    {
        if (string.IsNullOrEmpty(input)) return input ?? string.Empty;
        var sb = new System.Text.StringBuilder(input.Length + 10);
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (c is '_' or '-')
            {
                sb.Append('_');
                continue;
            }
            if (char.IsUpper(c))
            {
                if (i > 0)
                {
                    char prev = input[i - 1];
                    if (char.IsLower(prev) || char.IsDigit(prev))
                    {
                        sb.Append('_');
                    }
                    else if (char.IsUpper(prev) && i + 1 < input.Length && char.IsLower(input[i + 1]))
                    {
                        sb.Append('_');
                    }
                }
                sb.Append(char.ToLowerInvariant(c));
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }
}
