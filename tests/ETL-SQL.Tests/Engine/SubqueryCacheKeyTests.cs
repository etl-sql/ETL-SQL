using System.Collections.Generic;
using ETL_SQL.Core;
using ETL_SQL.Core.Data;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Engine
{
    public class SubqueryCacheKeyTests
    {
        [Fact]
        public void EqualityDependsOnQueryAndValues()
        {
            var stmt1 = new SelectStatement(
                new List<SelectColumn> { new SelectColumn(new IdentifierExpression("col"), null) },
                null,
                new TableReference("T"),
                new List<JoinClause>(),
                null
            );
            var stmt2 = new SelectStatement(
                new List<SelectColumn> { new SelectColumn(new IdentifierExpression("col"), null) },
                null,
                new TableReference("T"),
                new List<JoinClause>(),
                null
            );

            var key1 = new SubqueryCacheKey(stmt1, new CompoundKey(1));
            var key2 = new SubqueryCacheKey(stmt2, new CompoundKey(1));
            var key3 = new SubqueryCacheKey(stmt1, new CompoundKey(2));
            var key4 = new SubqueryCacheKey(
                new SelectStatement(
                    new List<SelectColumn> { new SelectColumn(new IdentifierExpression("col"), null) },
                    null,
                    new TableReference("Other"),
                    new List<JoinClause>(),
                    null
                ),
                new CompoundKey(1));

            Assert.Equal(key1, key2);
            Assert.NotEqual(key1, key3);
            Assert.NotEqual(key1, key4);
        }

        [Fact]
        public void HashCodeIsConsistent()
        {
            var stmt = new SelectStatement(
                new List<SelectColumn> { new SelectColumn(new IdentifierExpression("col"), null) },
                null,
                new TableReference("T"),
                new List<JoinClause>(),
                null
            );
            var key1 = new SubqueryCacheKey(stmt, new CompoundKey("A", 10));
            var key2 = new SubqueryCacheKey(stmt, new CompoundKey("A", 10));

            Assert.Equal(key1.GetHashCode(), key2.GetHashCode());
        }
    }
}
