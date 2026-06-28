using System;
using System.Collections.Generic;

namespace ETL_SQL.Core;

public class StatementSqlEqualityComparer : IEqualityComparer<Statement>
{
    public bool Equals(Statement? x, Statement? y)
    {
        if (ReferenceEquals(x, y)) return true;
        if (x == null || y == null) return false;

        // Basic optimization: compare SQL strings
        // Note: ToSql() might include CTEs and other context, which is good for subqueries.
        return string.Equals(x.ToSql(), y.ToSql(), StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(Statement obj)
    {
        if (obj == null) return 0;
        // Use ToSql() for hash code calculation
        return obj.ToSql().ToLowerInvariant().GetHashCode();
    }
}
