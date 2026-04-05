using System;
using ETL_SQL.Core.Parser;

class Program {
    static void Main() {
        var lexer = new Lexer(\"DECLARE @id int;  ,@name varchar(100);\");
        var parser = new Parser(lexer.Tokenize());
        var script = parser.Parse();
        foreach (var diag in script.Diagnostics) {
            Console.WriteLine($\"{diag.Severity}: {diag.Message} at {diag.Line}:{diag.Column}\");
        }
    }
}
