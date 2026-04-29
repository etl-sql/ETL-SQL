using System;
using ETL_SQL.Data;

namespace ETL_SQL.Core.Data
{
    public enum SubqueryResultType
    {
        Scalar,
        Stream,
        Exists
    }

    /// <summary>
    /// Represents a unique key for the subquery cache, combining the statement 
    /// with the specific values of any captured outer references (correlation).
    /// </summary>
    public record SubqueryCacheKey(Statement Query, CompoundKey CapturedValues, SubqueryResultType ResultType = SubqueryResultType.Scalar)
    {
        private static readonly StatementSqlEqualityComparer _stmtComparer = new();

        public virtual bool Equals(SubqueryCacheKey? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return ResultType == other.ResultType && _stmtComparer.Equals(Query, other.Query) && CapturedValues.Equals(other.CapturedValues);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ResultType);
            hash.Add(Query, _stmtComparer);
            hash.Add(CapturedValues);
            return hash.ToHashCode();
        }
    }
}
