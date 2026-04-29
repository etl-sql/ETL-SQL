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
        private string? _sql;
        private string Sql => _sql ??= Query.ToSql();

        public virtual bool Equals(SubqueryCacheKey? other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return ResultType == other.ResultType && Sql == other.Sql && CapturedValues.Equals(other.CapturedValues);
        }

        public override int GetHashCode()
        {
            var hash = new HashCode();
            hash.Add(ResultType);
            hash.Add(Sql);
            hash.Add(CapturedValues);
            return hash.ToHashCode();
        }
    }
}
