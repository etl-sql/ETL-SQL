using System.Collections.Generic;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    public class DataTableTests
    {
        [Fact]
        public void AddRow_WithAsyncConstraintValidation_ThrowsInsteadOfBlocking()
        {
            var table = new DataTable
            {
                Validator = new PassingValidator()
            };
            table.SetColumns(
                new[] { "id" },
                new[]
                {
                    new TableConstraintInfo
                    {
                        Name = "ck_id",
                        Type = ConstraintType.Check,
                        Expression = new LiteralExpression(true, TokenType.TRUE)
                    }
                });

#pragma warning disable CS0618
            var ex = Assert.Throws<ExecutionException>(() => table.AddRow(new Row { ["id"] = 1 }));
#pragma warning restore CS0618

            Assert.Contains("Use AddRowAsync", ex.Message);
            Assert.Empty(table.Rows);
        }

        private sealed class PassingValidator : IDataValidator
        {
            public Task<bool> ValidateCheckConstraint(Expression expression, Row row) =>
                Task.FromResult(true);

            public Task<bool> ValidateForeignKey(ForeignKeyReference reference, List<string> sourceColumns, Row row) =>
                Task.FromResult(true);
        }
    }
}
