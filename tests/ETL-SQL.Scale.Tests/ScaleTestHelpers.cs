using ETL_SQL.Core;
using ETL_SQL.Core.Parser;

namespace ETL_SQL.Tests.Core;

internal static class TestHelpers
{
    public static Script Parse(string sql) => new Parser(new Lexer(sql).Tokenize(), sql).Parse();
}
