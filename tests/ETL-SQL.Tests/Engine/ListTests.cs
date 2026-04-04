using Xunit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;
using ETL_SQL.App;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace ETL_SQL.Tests
{
    public class ListTests
    {

        [Fact]
        public async Task TestListBasicFunctions()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                DECLARE @mylist LIST = [1, 2, 3];
                PRINT 'Initial Length: ' + CAST(LENGTH(@mylist) AS VARCHAR);
                
                SET @mylist = APPEND_TO_LIST(@mylist, 4);
                PRINT 'After Append Length: ' + CAST(LENGTH(@mylist) AS VARCHAR);
                
                SET @mylist = REMOVE_FROM_LIST(@mylist, 2);
                PRINT 'After Remove Length: ' + CAST(LENGTH(@mylist) AS VARCHAR);
                
                DECLARE @sorted LIST = SORT_LIST([3, 1, 4, 1, 5]);
            ";
            await eval.Evaluate(new Lexer(script).TokenizeToScript());

            var mylist = eval.Variables["@mylist"] as List<object?>;
            Assert.NotNull(mylist);
            Assert.Equal(3, mylist.Count);
            Assert.DoesNotContain(mylist, x => x?.ToString() == "2");
            Assert.Contains(mylist, x => x?.ToString() == "4");

            var sorted = eval.Variables["@sorted"] as List<object?>;
            Assert.NotNull(sorted);
            Assert.Equal(5, sorted.Count);
            Assert.Equal("1", sorted[0]?.ToString());
            Assert.Equal("5", sorted[4]?.ToString());
        }

        [Fact]
        public async Task TestListIterationComplex()
        {
            var eval = DependencyInjectionSetup.BuildServiceProvider().GetRequiredService<Evaluator>();
            string script = @"
                DECLARE @names LIST = ['Alice', 'Bob', 'Charlie'];
                DECLARE @msg VARCHAR = '';
                FOREACH @n IN @names
                BEGIN
                    SET @msg = @msg + @n + ';';
                END
            ";
            await eval.Evaluate(new Lexer(script).TokenizeToScript());

            Assert.Equal("Alice;Bob;Charlie;", eval.Variables["@msg"]?.ToString());
        }
    }
}
