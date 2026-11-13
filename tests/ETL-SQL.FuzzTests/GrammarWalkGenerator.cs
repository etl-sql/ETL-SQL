using System;
using System.Collections.Generic;
using System.Linq;
using ETL_SQL.Analysis.Linting.Grammar;
using ETL_SQL.Core.Parser;
using ETL_SQL.Core.Services;

namespace ETL_SQL.FuzzTests
{
    public class GrammarWalkGenerator
    {
        private readonly GrammarStateTree _tree;
        private readonly Random _rng;
        private readonly Queue<Token> _tokenQueue = new();
        private int _blockDepth = 0;

        /// <summary>Distinct grammar states the generator has walked into (grammar coverage).</summary>
        public HashSet<StateNode> VisitedStates { get; } = new();

        /// <summary>Distinct grammar transitions the generator has taken (grammar coverage).</summary>
        public HashSet<StateTransition> VisitedTransitions { get; } = new();

        private readonly List<string> _customTables = new() { "Users", "Products", "Sales", "Employees" };
        private readonly List<string> _customColumns = new() { "UserID", "UserName", "Email", "ProductID", "Price", "Quantity", "Total", "EmpID", "Salary", "ProductName" };
        private readonly Dictionary<string, string[]> _schemaColumns = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Users"] = new[] { "UserID", "UserName", "Email" },
            ["Products"] = new[] { "ProductID", "ProductName", "Price" },
            ["Sales"] = new[] { "ProductID", "Quantity", "Total" },
            ["Employees"] = new[] { "EmpID", "Salary", "UserName" }
        };
        private string? _preferredExecutionTable;
        private static readonly string[] MockVariables = { "@myVar", "@id", "@name", "@price" };

        // Covers the statement families registered as grammar start nodes. Previously only 16
        // starters were fuzzed, which left ~half the language (CREATE/ALTER/DROP/EXPORT/COPY/RUN/
        // EXECUTE/…) untouched at the top level and left the DDL body generators in
        // TryGenerateForTransition (the lastKeyword == "CREATE" branch, SHOW bodies) as dead code.
        private static readonly HashSet<string> AllowedStatementStarters = new(StringComparer.OrdinalIgnoreCase)
        {
            "SELECT", "INSERT", "UPDATE", "DELETE", "MERGE", "DECLARE", "SET", "BEGIN",
            "IF", "WHILE", "FOR", "FOREACH", "COMMIT", "ROLLBACK", "PARALLEL", "SHOW",
            "CREATE", "ALTER", "DROP", "REPLACE", "EXPORT", "COPY", "MOVE", "RUN",
            "EXECUTE", "EXEC", "TRUNCATE", "PRINT", "THROW", "TAG",
            "ENCRYPT", "DECRYPT", "COMPRESS", "WAITFOR", "SEND", "RECEIVE"
        };

        public GrammarWalkGenerator(GrammarStateTree tree, Random rng)
        {
            _tree = tree;
            _rng = rng;
        }

        public void AddCustomSchema(string table, string[] columns)
        {
            _customTables.Add(table);
            _customColumns.AddRange(columns);
            _schemaColumns[table] = columns;
            _preferredExecutionTable = table;
        }

        private string GetRandomTable()
        {
            return _customTables[_rng.Next(_customTables.Count)];
        }

        private string GetRandomColumn()
        {
            return _customColumns[_rng.Next(_customColumns.Count)];
        }

        private string GetExecutionTable()
        {
            return _preferredExecutionTable ?? "Users";
        }

        private string GetExecutionColumn()
        {
            var table = GetExecutionTable();
            if (_schemaColumns.TryGetValue(table, out var columns) && columns.Length > 0)
            {
                return columns[_rng.Next(columns.Length)];
            }

            return GetRandomColumn();
        }

        public void CorruptQuery(List<Token> tokens)
        {
            if (tokens.Count <= 2) return;
            int idx = _rng.Next(tokens.Count - 1); // Avoid the trailing EOF
            var original = tokens[idx];

            switch (_rng.Next(5))
            {
                case 0: // Delete a token
                    tokens.RemoveAt(idx);
                    break;

                case 1: // Replace with a random structural/operator token
                    tokens[idx] = _rng.Next(5) switch
                    {
                        0 => new Token(TokenType.LPAREN, "(", original.Line, original.Column, original.EndLine, original.EndColumn),
                        1 => new Token(TokenType.RPAREN, ")", original.Line, original.Column, original.EndLine, original.EndColumn),
                        2 => new Token(TokenType.PLUS, "+", original.Line, original.Column, original.EndLine, original.EndColumn),
                        3 => new Token(TokenType.EQUALS, "=", original.Line, original.Column, original.EndLine, original.EndColumn),
                        _ => new Token(TokenType.IDENTIFIER, "CorruptCol", original.Line, original.Column, original.EndLine, original.EndColumn)
                    };
                    break;

                case 2: // Duplicate a token
                    tokens.Insert(idx, original);
                    break;

                case 3: // Truncate the tail (drop everything after idx, keeping the trailing EOF)
                    if (idx + 1 < tokens.Count - 1)
                    {
                        tokens.RemoveRange(idx + 1, tokens.Count - 1 - (idx + 1));
                    }
                    break;

                default: // Inject an unbalanced parenthesis
                    var paren = _rng.Next(2) == 0
                        ? new Token(TokenType.LPAREN, "(", original.Line, original.Column, original.EndLine, original.EndColumn)
                        : new Token(TokenType.RPAREN, ")", original.Line, original.Column, original.EndLine, original.EndColumn);
                    tokens.Insert(idx, paren);
                    break;
            }
        }

        /// <summary>
        /// True when the last query came from the statement templates, which are valid by
        /// construction against the fuzz schema; false for a grammar walk, whose identifiers are
        /// random and whose rejections are expected.
        /// </summary>
        public bool LastQueryIsValidByConstruction { get; private set; }

        public List<Token> GenerateQuery()
        {
            // Prefer complete statement skeletons most of the time. The raw grammar walker is useful
            // for coverage, but it can stop on parser-incomplete paths; these skeletons keep the
            // default fuzzer spending more iterations on parser-accepted SQL before retry kicks in.
            if (_rng.Next(100) < 90)
            {
                var generated = GenerateParserValidQuery();
                if (generated.Count > 1)
                {
                    LastQueryIsValidByConstruction = true;
                    return generated;
                }
            }

            LastQueryIsValidByConstruction = false;
            return GenerateGrammarWalkQuery();
        }

        private List<Token> GenerateGrammarWalkQuery()
        {
            var tokens = new List<Token>();
            var currentState = _tree.Root;
            int stepCount = 0;
            const int maxSteps = 250; // Higher limit for multi-statement scripts

            _tokenQueue.Clear();
            _blockDepth = 0;

            while (stepCount++ < maxSteps)
            {
                if (_tokenQueue.Count > 0)
                {
                    var token = _tokenQueue.Dequeue();
                    tokens.Add(token with { Line = 1, Column = tokens.Count + 1, EndLine = 1, EndColumn = tokens.Count + 1 + token.Value.Length });
                    continue;
                }

                if (currentState.Transitions.Count == 0)
                {
                    break;
                }

                // If in a block and at Root, allow closing block randomly
                if (currentState == _tree.Root && _blockDepth > 0 && _rng.Next(10) < 3)
                {
                    var endTokens = TokenizeBody("END;");
                    foreach (var t in endTokens) _tokenQueue.Enqueue(t);
                    _blockDepth--;
                    continue;
                }

                // Escape hatch to end statements and avoid self-loop traps
                if (currentState != _tree.Root && tokens.Count > 4 && _rng.Next(10) < 3)
                {
                    // Inject query suffixes (WINDOW, QUALIFY) before ending SELECT statements
                    var lastKeyword = GetLastStatementKeyword(tokens, out _);
                    if (lastKeyword == "SELECT")
                    {
                        if (_rng.Next(10) < 3) // 30% chance of appending QUALIFY
                        {
                            var qualifyTokens = TokenizeBody("QUALIFY UserID > 5");
                            tokens.AddRange(qualifyTokens);
                        }
                        if (_rng.Next(10) < 3) // 30% chance of appending WINDOW
                        {
                            var windowTokens = TokenizeBody("WINDOW w AS (PARTITION BY Department)");
                            tokens.AddRange(windowTokens);
                        }
                    }

                    var semi = new Token(TokenType.SEMICOLON, ";", 1, tokens.Count + 1, 1, tokens.Count + 2);
                    tokens.Add(semi);
                    currentState = _tree.Root;
                    continue;
                }

                // Pick a transition randomly, filtering statement starters at Root
                List<StateTransition> transitions;
                if (currentState == _tree.Root)
                {
                    transitions = currentState.Transitions
                        .Where(t => t.Label != null && AllowedStatementStarters.Contains(t.Label))
                        .ToList();

                    if (transitions.Count == 0)
                    {
                        transitions = currentState.Transitions;
                    }
                }
                else
                {
                    transitions = currentState.Transitions;
                }

                var transition = transitions[_rng.Next(transitions.Count)];
                VisitedStates.Add(currentState);
                VisitedTransitions.Add(transition);

                // Try generating tokens for this transition
                bool generated = TryGenerateForTransition(transition, tokens);
                if (generated)
                {
                    if (_tokenQueue.Count > 0)
                    {
                        var token = _tokenQueue.Dequeue();
                        tokens.Add(token with { Line = 1, Column = tokens.Count + 1, EndLine = 1, EndColumn = tokens.Count + 1 + token.Value.Length });

                        // Track block depth if we generated BEGIN
                        if (token.Type == TokenType.BEGIN)
                        {
                            _blockDepth++;
                        }
                    }
                    currentState = transition.Target;

                    if (currentState == _tree.Root && tokens.Count > 15 && _rng.Next(2) == 0)
                    {
                        break;
                    }
                }
                else
                {
                    // Try alternative transitions
                    var shuffledTransitions = transitions.OrderBy(_ => _rng.Next()).ToList();
                    bool found = false;
                    foreach (var altTransition in shuffledTransitions)
                    {
                        if (TryGenerateForTransition(altTransition, tokens))
                        {
                            VisitedTransitions.Add(altTransition);
                            if (_tokenQueue.Count > 0)
                            {
                                var token = _tokenQueue.Dequeue();
                                tokens.Add(token with { Line = 1, Column = tokens.Count + 1, EndLine = 1, EndColumn = tokens.Count + 1 + token.Value.Length });

                                if (token.Type == TokenType.BEGIN)
                                {
                                    _blockDepth++;
                                }
                            }
                            currentState = altTransition.Target;
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        break;
                    }
                }
            }

            // Flush remaining queue
            while (_tokenQueue.Count > 0)
            {
                var token = _tokenQueue.Dequeue();
                tokens.Add(token with { Line = 1, Column = tokens.Count + 1, EndLine = 1, EndColumn = tokens.Count + 1 + token.Value.Length });
            }

            // Cleanly close remaining blocks
            while (_blockDepth > 0)
            {
                var endTokens = TokenizeBody("; END;");
                foreach (var t in endTokens)
                {
                    tokens.Add(t with { Line = 1, Column = tokens.Count + 1, EndLine = 1, EndColumn = tokens.Count + 1 + t.Value.Length });
                }
                _blockDepth--;
            }

            tokens.Add(new Token(TokenType.EOF, "", 1, tokens.Count + 1, 1, tokens.Count + 1));
            return tokens;
        }

        private List<Token> GenerateParserValidQuery()
        {
            var statements = new List<string> { GenerateCompleteStatement() };

            if (_rng.Next(10) < 2)
            {
                statements.Add(GenerateCompleteStatement());
            }

            return TokenizeQuery(string.Join(" ", statements));
        }

        private string GenerateCompleteStatement()
        {
            return _rng.Next(18) switch
            {
                0 or 1 or 2 or 3 or 4 => GenerateSelectStatement(),
                5 => $"DECLARE {RandomVariable()} INT = {_rng.Next(1, 100)};",
                6 => $"DECLARE @fuzzValue INT = {_rng.Next(1, 100)}; SET @fuzzValue = {_rng.Next(1, 100)};",
                7 => $"INSERT INTO src.{GetExecutionTable()} (ID, Name) VALUES ({_rng.Next(10, 1000)}, {GenerateStringLiteralSql()});",
                8 => $"UPDATE src.{GetExecutionTable()} SET Price = {_rng.Next(1, 100)} WHERE ID = {_rng.Next(1, 4)};",
                9 => $"DELETE FROM src.{GetExecutionTable()} WHERE ID = -1;",
                // SHOW VARIABLES and SHOW CONNECTIONS are retired; these are their replacements.
                10 => "SELECT * FROM eng.variables;",
                11 => "SELECT * FROM eng.connections;",
                12 or 13 or 14 => GenerateSelectStatement(),
                15 => $"PRINT {GenerateStringLiteralSql()};",
                16 => $"DECLARE @fuzzText VARCHAR(50) = {GenerateStringLiteralSql()};",
                _ => $"PRINT {GenerateLiteralSql()};"
            };
        }

        // Kept for parser-fidelity experiments; the strict execution lane avoids persistent report
        // objects so long fuzz runs do not exhaust per-script object limits.
        private string GenerateVisualAndPageStatement()
        {
            var visualName = $"visual_{_rng.Next(1, 1000)}";
            return $"CREATE VISUAL {visualName} AS BAR (SOURCE = src.{GetExecutionTable()}, MAPPINGS (X = {GetExecutionColumn()}, Y = {GetExecutionColumn()})); " +
                   $"CREATE PAGE page_{_rng.Next(1, 1000)} AS DASHBOARD (STRUCTURE = 'A', MAP ('A' = {visualName}));";
        }

        // Every query below is valid by construction against the fuzz table, so an engine rejection
        // is a finding, not generator noise: grouped queries project only grouped columns and
        // aggregates, ORDER BY names a projected column (or any column when nothing is grouped or
        // distinct), and set-operation branches project the same columns.
        private string GenerateSelectStatement() => _rng.Next(10) switch
        {
            0 or 1 or 2 => GenerateRowQuery(),
            3 or 4 => GenerateAggregateQuery(),
            5 => GenerateJoinQuery(),
            6 => GenerateSetOperationQuery(),
            7 => GenerateCteQuery(),
            _ => GenerateSubqueryQuery(),
        };

        private string ExecutionSource => $"src.{GetExecutionTable()}";

        private string GenerateRowQuery()
        {
            bool distinct = _rng.Next(10) < 2;
            bool top = !distinct && _rng.Next(10) < 2;

            string orderKey;
            string projections;
            switch (_rng.Next(4))
            {
                case 0:
                    projections = "*";
                    orderKey = GetExecutionColumn();
                    break;
                case 1:
                    orderKey = GetExecutionColumn();
                    projections = $"{orderKey}, {GetExecutionColumn()}";
                    break;
                case 2:
                    projections = $"{GenerateExpressionSql(2)} AS ExprValue";
                    orderKey = "ExprValue";
                    break;
                default:
                    projections = $"{GenerateWindowFunctionSql()} AS WindowValue";
                    orderKey = "WindowValue";
                    break;
            }

            var sql = "SELECT " + (distinct ? "DISTINCT " : "") + (top ? $"TOP {_rng.Next(1, 20)} " : "")
                + $"{projections} FROM {ExecutionSource}";
            if (_rng.Next(10) < 6) sql += $" WHERE {GeneratePredicateSql()}";
            if (!distinct && _rng.Next(10) < 2) sql += $" QUALIFY ROW_NUMBER() OVER (ORDER BY {GetExecutionColumn()}) <= {_rng.Next(1, 20)}";

            bool ordered = _rng.Next(10) < 5;
            if (ordered) sql += $" ORDER BY {orderKey} {(_rng.Next(2) == 0 ? "ASC" : "DESC")}";
            if (!top) sql += GenerateRowLimit(ordered);
            return sql + ";";
        }

        private string GenerateRowLimit(bool ordered) => _rng.Next(10) switch
        {
            0 or 1 => $" LIMIT {_rng.Next(1, 50)}",
            2 => $" LIMIT {_rng.Next(1, 50)} OFFSET {_rng.Next(0, 10)}",
            3 when ordered => $" OFFSET {_rng.Next(0, 10)} ROWS FETCH NEXT {_rng.Next(1, 20)} ROWS ONLY",
            4 when ordered => $" OFFSET {_rng.Next(0, 10)} ROWS",
            _ => "",
        };

        private string GenerateAggregateQuery()
        {
            var key = GetExecutionColumn();
            var measure = NumericExecutionColumn();
            var aggregates = $"COUNT(*) AS RowCount, SUM({measure}) AS Total, MIN({key}) AS Low";

            string groupBy;
            string projections;
            switch (_rng.Next(5))
            {
                case 0:
                    // A whole-table aggregate: no key to project.
                    projections = aggregates;
                    groupBy = "";
                    break;
                case 1:
                    var second = GetExecutionColumn();
                    projections = $"{key}, {second}, {aggregates}";
                    groupBy = $" GROUP BY GROUPING SETS (({key}, {second}), ({key}), ())";
                    break;
                case 2:
                    projections = $"{key}, {aggregates}";
                    groupBy = $" GROUP BY ROLLUP ({key})";
                    break;
                default:
                    projections = $"{key}, {aggregates}";
                    groupBy = $" GROUP BY {key}";
                    break;
            }

            var sql = $"SELECT {projections} FROM {ExecutionSource}";
            if (_rng.Next(10) < 5) sql += $" WHERE {GeneratePredicateSql()}";
            sql += groupBy;
            if (_rng.Next(10) < 3) sql += $" HAVING COUNT(*) > {_rng.Next(0, 5)}";
            if (_rng.Next(10) < 5) sql += $" ORDER BY RowCount {(_rng.Next(2) == 0 ? "ASC" : "DESC")}";
            return sql + ";";
        }

        private string GenerateJoinQuery()
        {
            var join = _rng.Next(3) switch
            {
                0 => "JOIN",
                1 => "LEFT JOIN",
                _ => "INNER JOIN",
            };
            var on = _rng.Next(3) == 0
                ? $"a.ID = b.ID AND b.{NumericExecutionColumn()} IS NOT NULL"
                : "a.ID = b.ID";
            var sql = $"SELECT a.ID, a.{GetExecutionColumn()} AS LeftValue, b.{GetExecutionColumn()} AS RightValue "
                + $"FROM {ExecutionSource} a {join} {ExecutionSource} b ON {on}";
            if (_rng.Next(10) < 6) sql += $" WHERE {GeneratePredicateSql("a.")}";
            if (_rng.Next(10) < 4) sql += $" ORDER BY a.ID{GenerateRowLimit(ordered: true)}";
            return sql + ";";
        }

        private string GenerateSetOperationQuery()
        {
            var columns = $"ID, {GetExecutionColumn()}";
            var op = _rng.Next(5) switch
            {
                0 => "UNION",
                1 => "UNION ALL",
                2 => "INTERSECT",
                3 => "EXCEPT",
                _ => "MINUS",
            };
            return $"SELECT {columns} FROM {ExecutionSource} WHERE {GeneratePredicateSql()} {op} "
                + $"SELECT {columns} FROM {ExecutionSource} WHERE {GeneratePredicateSql()};";
        }

        private string GenerateCteQuery()
        {
            var measure = NumericExecutionColumn();
            var body = $"SELECT ID, Name, {measure} AS Measure FROM {ExecutionSource}";
            if (_rng.Next(2) == 0) body += $" WHERE {GeneratePredicateSql()}";

            var outer = _rng.Next(3) switch
            {
                0 => "SELECT COUNT(*) AS RowCount, SUM(Measure) AS Total FROM c",
                1 => $"SELECT ID, Measure FROM c WHERE Measure > {_rng.Next(0, 100)} OR Measure IS NULL",
                _ => "SELECT c.ID, d.Measure FROM c JOIN c d ON c.ID = d.ID",
            };
            return $"WITH c AS ({body}) {outer};";
        }

        private string GenerateSubqueryQuery()
        {
            var inner = $"SELECT ID FROM {ExecutionSource} WHERE {GeneratePredicateSql()}";
            return _rng.Next(4) switch
            {
                0 => $"SELECT ID, Name FROM {ExecutionSource} WHERE ID {(_rng.Next(2) == 0 ? "IN" : "NOT IN")} ({inner});",
                1 => $"SELECT ID FROM {ExecutionSource} o WHERE {(_rng.Next(2) == 0 ? "" : "NOT ")}EXISTS "
                    + $"(SELECT 1 FROM {ExecutionSource} i WHERE i.ID = o.ID AND {GeneratePredicateSql("i.")});",
                2 => $"SELECT ID, (SELECT MAX({NumericExecutionColumn()}) FROM {ExecutionSource}) AS Peak FROM {ExecutionSource};",
                _ => $"SELECT d.ID FROM (SELECT ID, {GetExecutionColumn()} AS V FROM {ExecutionSource}) d WHERE d.V IS NOT NULL;",
            };
        }

        private string NumericExecutionColumn()
        {
            var numeric = (_schemaColumns.TryGetValue(GetExecutionTable(), out var columns) ? columns : Array.Empty<string>())
                .Where(c => !c.Equals("Name", StringComparison.OrdinalIgnoreCase)
                    && !c.EndsWith("Name", StringComparison.OrdinalIgnoreCase)
                    && !c.Equals("Email", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            return numeric.Length > 0 ? numeric[_rng.Next(numeric.Length)] : GetExecutionColumn();
        }

        private string GenerateExpressionSql(int depth)
        {
            if (depth <= 0 || _rng.Next(3) == 0)
            {
                return _rng.Next(3) switch
                {
                    0 => GetExecutionColumn(),
                    1 => _rng.Next(1, 100).ToString(),
                    _ => GenerateLiteralSql()
                };
            }

            return _rng.Next(5) switch
            {
                0 => $"({GenerateExpressionSql(depth - 1)} + {GenerateExpressionSql(depth - 1)})",
                1 => $"UPPER({GenerateExpressionSql(depth - 1)})",
                2 => $"LOWER({GenerateExpressionSql(depth - 1)})",
                3 => $"CONCAT({GenerateExpressionSql(depth - 1)}, {GenerateExpressionSql(depth - 1)})",
                _ => $"COALESCE({GenerateExpressionSql(depth - 1)}, {GenerateLiteralSql()})"
            };
        }

        private string GeneratePredicateSql(string prefix = "")
        {
            string Col() => prefix + GetExecutionColumn();
            return _rng.Next(6) switch
            {
                0 => $"{Col()} = {GenerateLiteralSql()}",
                1 => $"{Col()} > {_rng.Next(0, 100)}",
                2 => $"{Col()} IS NOT NULL",
                3 => $"{prefix}{NumericExecutionColumn()} BETWEEN {_rng.Next(0, 50)} AND {_rng.Next(50, 100)}",
                4 => $"{Col()} IS DISTINCT FROM {GenerateLiteralSql()}",
                _ => $"({Col()} < {_rng.Next(1, 100)} OR {Col()} IS NULL)"
            };
        }

        private string GenerateWindowFunctionSql()
        {
            return _rng.Next(3) switch
            {
                0 => $"ROW_NUMBER() OVER (ORDER BY {GetExecutionColumn()})",
                1 => $"SUM({NumericExecutionColumn()}) OVER (PARTITION BY {GetExecutionColumn()})",
                _ => $"COUNT(*) OVER ()"
            };
        }

        private string GenerateLiteralSql()
        {
            return _rng.Next(3) switch
            {
                0 => _rng.Next(1, 100).ToString(),
                1 => GenerateStringLiteralSql(),
                _ => "NULL"
            };
        }

        private string GenerateStringLiteralSql()
        {
            return $"'Val_{_rng.Next(1, 10)}'";
        }

        private string RandomVariable()
        {
            return MockVariables[_rng.Next(MockVariables.Length)];
        }

        private bool TryGenerateForTransition(StateTransition transition, List<Token> tokensSoFar)
        {
            var label = transition.Label;
            if (!string.IsNullOrEmpty(label))
            {
                if (label.Equals("<table_source>", StringComparison.OrdinalIgnoreCase) ||
                    label.Equals("<join_table>", StringComparison.OrdinalIgnoreCase))
                {
                    var table = "src." + GetRandomTable();
                    var tok = new Token(TokenType.IDENTIFIER, table, 0, 0, 0, 0);
                    if (transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
                }
                else if (label.Equals("<column_name>", StringComparison.OrdinalIgnoreCase))
                {
                    var col = GetRandomColumn();
                    var tok = new Token(TokenType.IDENTIFIER, col, 0, 0, 0, 0);
                    if (transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
                }
                else if (label.Equals("<connection_name>", StringComparison.OrdinalIgnoreCase))
                {
                    var tok = new Token(TokenType.IDENTIFIER, "src", 0, 0, 0, 0);
                    if (transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
                }
                else if (label.Equals("<variable_name>", StringComparison.OrdinalIgnoreCase))
                {
                    var val = MockVariables[_rng.Next(MockVariables.Length)];
                    var tok = new Token(TokenType.VARIABLE, val, 0, 0, 0, 0);
                    if (transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
                }
                else if (label.Equals("<group_by_token>", StringComparison.OrdinalIgnoreCase))
                {
                    int choice = _rng.Next(5);
                    string groupText = choice switch
                    {
                        0 => "ALL",
                        1 => $"ROLLUP ({GetRandomColumn()})",
                        2 => $"CUBE ({GetRandomColumn()}, {GetRandomColumn()})",
                        3 => $"GROUPING SETS (({GetRandomColumn()}), ({GetRandomColumn()}))",
                        _ => $"{GetRandomColumn()}, {GetRandomColumn()}"
                    };
                    var groupTokens = TokenizeBody(groupText);
                    if (groupTokens.Count > 0 && transition.Condition(groupTokens[0]))
                    {
                        foreach (var t in groupTokens) _tokenQueue.Enqueue(t);
                        return true;
                    }
                }
                else if (label.Equals("<order_by_token>", StringComparison.OrdinalIgnoreCase))
                {
                    string orderText = _rng.Next(2) switch
                    {
                        0 => "1, 2",
                        _ => $"{GetRandomColumn()} ASC, {GetRandomColumn()} DESC NULLS LAST"
                    };
                    var orderTokens = TokenizeBody(orderText);
                    if (orderTokens.Count > 0 && transition.Condition(orderTokens[0]))
                    {
                        foreach (var t in orderTokens) _tokenQueue.Enqueue(t);
                        return true;
                    }
                }
                else if (label.Equals("<expression>", StringComparison.OrdinalIgnoreCase) ||
                         label.Equals("<value>", StringComparison.OrdinalIgnoreCase) ||
                         label.Equals("<sets_assignment_token>", StringComparison.OrdinalIgnoreCase) ||
                         label.Equals("<declaration_token>", StringComparison.OrdinalIgnoreCase) ||
                         label.Equals("<expression_token>", StringComparison.OrdinalIgnoreCase))
                {
                    // Intercept wildcard loops to generate statement-appropriate structures
                    var lastKeyword = GetLastStatementKeyword(tokensSoFar, out string? ddlType);

                    if (lastKeyword == "CREATE" || lastKeyword == "ALTER" || lastKeyword == "REPLACE")
                    {
                        string? ddlBody = ddlType switch
                        {
                            "VISUAL" => "AS BAR (X = Region, Y = SUM(Sales)) STYLE (THEME = dark) TOOLTIP = 'Sales'",
                            "PAGE" => "AS (myVisual) STRUCTURE = 'A' MAP ('A' = myVisual)",
                            "DATASET" => "AS SELECT * FROM src.Sales ENCRYPT = MACHINE",
                            "CONTAINER" => "AS BOX (myVisual)",
                            _ => null
                        };

                        if (ddlBody != null)
                        {
                            var ddlTokens = TokenizeBody(ddlBody);
                            if (ddlTokens.Count > 0 && transition.Condition(ddlTokens[0]))
                            {
                                foreach (var t in ddlTokens) _tokenQueue.Enqueue(t);
                                return true;
                            }
                        }
                    }
                    else if (lastKeyword == "SHOW")
                    {
                        string showBody = _rng.Next(4) switch
                        {
                            0 => "PROFILE",
                            1 => "VARIABLES",
                            2 => "CONNECTIONS",
                            _ => "LOCKS"
                        };
                        var showTokens = TokenizeBody(showBody);
                        if (showTokens.Count > 0 && transition.Condition(showTokens[0]))
                        {
                            foreach (var t in showTokens) _tokenQueue.Enqueue(t);
                            return true;
                        }
                    }
                    else if (lastKeyword == "SET")
                    {
                        bool hasVariable = tokensSoFar.Any(t => t.Type == TokenType.VARIABLE);
                        if (!hasVariable)
                        {
                            string setBody = _rng.Next(3) switch
                            {
                                0 => "ALLOW_FILE_OPERATIONS = ON",
                                1 => "WHAT_IF = ON",
                                _ => "MAX_STRING_RESULT_SIZE = 50000"
                            };
                            var setTokens = TokenizeBody(setBody);
                            if (setTokens.Count > 0 && transition.Condition(setTokens[0]))
                            {
                                foreach (var t in setTokens) _tokenQueue.Enqueue(t);
                                return true;
                            }
                        }
                    }

                    // Default recursive expression
                    var expr = GenerateExpressionTokens(3);
                    if (expr.Count > 0 && transition.Condition(expr[0]))
                    {
                        foreach (var t in expr) _tokenQueue.Enqueue(t);
                        return true;
                    }
                }
                else if (label.Equals("<time_expression>", StringComparison.OrdinalIgnoreCase))
                {
                    var tok = new Token(TokenType.STRING_LITERAL, "'00:00:05'", 0, 0, 0, 0);
                    if (transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
                }
                else if (label.Equals("<transaction_name>", StringComparison.OrdinalIgnoreCase) ||
                         label.Equals("<transaction_token>", StringComparison.OrdinalIgnoreCase))
                {
                    var tok = new Token(TokenType.IDENTIFIER, "tx1", 0, 0, 0, 0);
                    if (transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
                }

                var resolvedType = ResolveTokenType(label);
                if (resolvedType != null)
                {
                    var tok = new Token(resolvedType.Value, label, 0, 0, 0, 0);
                    if (transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
                }
            }

            if (transition.SuggestType != null)
            {
                var tok = transition.SuggestType.Value switch
                {
                    SuggestionType.Table => new Token(TokenType.IDENTIFIER, "src." + GetRandomTable(), 0, 0, 0, 0),
                    SuggestionType.Column => new Token(TokenType.IDENTIFIER, GetRandomColumn(), 0, 0, 0, 0),
                    SuggestionType.Connection => new Token(TokenType.IDENTIFIER, "src", 0, 0, 0, 0),
                    SuggestionType.Variable => new Token(TokenType.VARIABLE, MockVariables[_rng.Next(MockVariables.Length)], 0, 0, 0, 0),
                    SuggestionType.OptionName => new Token(TokenType.IDENTIFIER, "ENCRYPT", 0, 0, 0, 0),
                    SuggestionType.OptionValue => new Token(TokenType.IDENTIFIER, "OFF", 0, 0, 0, 0),
                    _ => null
                };
                if (tok != null && transition.Condition(tok)) { _tokenQueue.Enqueue(tok); return true; }
            }

            var probes = new List<Token>
            {
                new Token(TokenType.IDENTIFIER, "src.Users", 0, 0, 0, 0),
                new Token(TokenType.IDENTIFIER, "UserID", 0, 0, 0, 0),
                new Token(TokenType.VARIABLE, "@id", 0, 0, 0, 0),
                new Token(TokenType.NUMBER, "1", 0, 0, 0, 0),
                new Token(TokenType.STRING_LITERAL, "'Alice'", 0, 0, 0, 0),
                new Token(TokenType.STAR, "*", 0, 0, 0, 0),
                new Token(TokenType.COMMA, ",", 0, 0, 0, 0),
                new Token(TokenType.LPAREN, "(", 0, 0, 0, 0),
                new Token(TokenType.RPAREN, ")", 0, 0, 0, 0),
                new Token(TokenType.DOT, ".", 0, 0, 0, 0),
                new Token(TokenType.EQUALS, "=", 0, 0, 0, 0),
                new Token(TokenType.PLUS, "+", 0, 0, 0, 0),
                new Token(TokenType.MINUS, "-", 0, 0, 0, 0),
                new Token(TokenType.BANG, "!", 0, 0, 0, 0),
                new Token(TokenType.SEMICOLON, ";", 0, 0, 0, 0),
            };

            foreach (var probe in probes)
            {
                if (transition.Condition(probe))
                {
                    _tokenQueue.Enqueue(probe);
                    return true;
                }
            }

            return false;
        }

        private List<Token> GenerateExpressionTokens(int depth)
        {
            var exprTokens = new List<Token>();

            if (depth <= 0 || _rng.Next(3) == 0)
            {
                exprTokens.Add(GenerateLeafToken());
                return exprTokens;
            }

            int choice = _rng.Next(5); // 5 choices to support Window Functions
            switch (choice)
            {
                case 0: // Binary expression: left OP right
                    exprTokens.AddRange(GenerateExpressionTokens(depth - 1));
                    exprTokens.Add(GenerateOperatorToken());
                    exprTokens.AddRange(GenerateExpressionTokens(depth - 1));
                    break;

                case 1: // Function call: FUNC(expr)
                    exprTokens.Add(GenerateFunctionToken());
                    exprTokens.Add(new Token(TokenType.LPAREN, "(", 0, 0, 0, 0));
                    exprTokens.AddRange(GenerateExpressionTokens(depth - 1));
                    exprTokens.Add(new Token(TokenType.RPAREN, ")", 0, 0, 0, 0));
                    break;

                case 2: // Parenthesized: ( expr )
                    exprTokens.Add(new Token(TokenType.LPAREN, "(", 0, 0, 0, 0));
                    exprTokens.AddRange(GenerateExpressionTokens(depth - 1));
                    exprTokens.Add(new Token(TokenType.RPAREN, ")", 0, 0, 0, 0));
                    break;

                case 3: // Window Function
                    exprTokens.AddRange(GenerateWindowFunctionTokens(depth - 1));
                    break;

                default: // Function call with 2 args: FUNC2(expr, expr)
                    exprTokens.Add(GenerateFunction2Token());
                    exprTokens.Add(new Token(TokenType.LPAREN, "(", 0, 0, 0, 0));
                    exprTokens.AddRange(GenerateExpressionTokens(depth - 1));
                    exprTokens.Add(new Token(TokenType.COMMA, ",", 0, 0, 0, 0));
                    exprTokens.AddRange(GenerateExpressionTokens(depth - 1));
                    exprTokens.Add(new Token(TokenType.RPAREN, ")", 0, 0, 0, 0));
                    break;
            }

            return exprTokens;
        }

        private List<Token> GenerateWindowFunctionTokens(int depth)
        {
            // 30% chance of referring to named window "w"
            if (_rng.Next(10) < 3)
            {
                return TokenizeBody("ROW_NUMBER() OVER w");
            }

            var winTokens = new List<Token>();

            string func = _rng.Next(4) switch
            {
                0 => "ROW_NUMBER",
                1 => "SUM",
                2 => "AVG",
                _ => "COUNT"
            };

            winTokens.Add(new Token(TokenType.IDENTIFIER, func, 0, 0, 0, 0));
            winTokens.Add(new Token(TokenType.LPAREN, "(", 0, 0, 0, 0));
            if (func != "ROW_NUMBER")
            {
                winTokens.Add(new Token(TokenType.IDENTIFIER, GetRandomColumn(), 0, 0, 0, 0));
            }
            winTokens.Add(new Token(TokenType.RPAREN, ")", 0, 0, 0, 0));

            // Append optional aggregate FILTER(WHERE ...) clause
            if (_rng.Next(3) == 0)
            {
                winTokens.AddRange(TokenizeBody($" FILTER(WHERE {GetRandomColumn()} > 10)"));
            }

            winTokens.Add(new Token(TokenType.OVER, "OVER", 0, 0, 0, 0));
            winTokens.Add(new Token(TokenType.LPAREN, "(", 0, 0, 0, 0));

            if (_rng.Next(2) == 0)
            {
                winTokens.Add(new Token(TokenType.IDENTIFIER, "PARTITION", 0, 0, 0, 0));
                winTokens.Add(new Token(TokenType.BY, "BY", 0, 0, 0, 0));
                winTokens.Add(new Token(TokenType.IDENTIFIER, GetRandomColumn(), 0, 0, 0, 0));
            }

            if (_rng.Next(2) == 0)
            {
                winTokens.Add(new Token(TokenType.ORDER, "ORDER", 0, 0, 0, 0));
                winTokens.Add(new Token(TokenType.BY, "BY", 0, 0, 0, 0));
                winTokens.Add(new Token(TokenType.IDENTIFIER, GetRandomColumn(), 0, 0, 0, 0));
                if (_rng.Next(2) == 0)
                {
                    winTokens.Add(new Token(TokenType.DESC, "DESC", 0, 0, 0, 0));
                }
            }

            winTokens.Add(new Token(TokenType.RPAREN, ")", 0, 0, 0, 0));
            return winTokens;
        }

        private Token GenerateLeafToken()
        {
            return _rng.Next(6) switch
            {
                0 => new Token(TokenType.IDENTIFIER, GetRandomColumn(), 0, 0, 0, 0),
                1 => new Token(TokenType.NUMBER, _rng.Next(1, 100).ToString(), 0, 0, 0, 0),
                2 => new Token(TokenType.STRING_LITERAL, $"'Val_{_rng.Next(1, 10)}'", 0, 0, 0, 0),
                3 => new Token(TokenType.TRUE, "TRUE", 0, 0, 0, 0),
                4 => new Token(TokenType.FALSE, "FALSE", 0, 0, 0, 0),
                _ => new Token(TokenType.IDENTIFIER, _rng.Next(4) switch
                {
                    0 => "@@TODAY",
                    1 => "@@NOW",
                    2 => "@@MAX_GROUPING_SETS",
                    _ => "@@SET_CUBE_LIMIT"
                }, 0, 0, 0, 0)
            };
        }

        private Token GenerateOperatorToken()
        {
            return _rng.Next(8) switch
            {
                0 => new Token(TokenType.PLUS, "+", 0, 0, 0, 0),
                1 => new Token(TokenType.MINUS, "-", 0, 0, 0, 0),
                2 => new Token(TokenType.STAR, "*", 0, 0, 0, 0),
                3 => new Token(TokenType.EQUALS, "=", 0, 0, 0, 0),
                4 => new Token(TokenType.GREATER_THAN, ">", 0, 0, 0, 0),
                5 => new Token(TokenType.LESS_THAN, "<", 0, 0, 0, 0),
                6 => new Token(TokenType.AND, "AND", 0, 0, 0, 0),
                _ => new Token(TokenType.OR, "OR", 0, 0, 0, 0)
            };
        }

        private Token GenerateFunctionToken()
        {
            return _rng.Next(3) switch
            {
                0 => new Token(TokenType.UPPER, "UPPER", 0, 0, 0, 0),
                1 => new Token(TokenType.LOWER, "LOWER", 0, 0, 0, 0),
                _ => new Token(TokenType.IDENTIFIER, "GROUPING", 0, 0, 0, 0)
            };
        }

        private Token GenerateFunction2Token()
        {
            return _rng.Next(2) switch
            {
                0 => new Token(TokenType.CONCAT, "CONCAT", 0, 0, 0, 0),
                _ => new Token(TokenType.IDENTIFIER, "ISNULL", 0, 0, 0, 0)
            };
        }

        private TokenType? ResolveTokenType(string value)
        {
            if (string.Equals(value, ";", StringComparison.Ordinal)) return TokenType.SEMICOLON;
            if (string.Equals(value, ",", StringComparison.Ordinal)) return TokenType.COMMA;
            if (string.Equals(value, ".", StringComparison.Ordinal)) return TokenType.DOT;
            if (string.Equals(value, "(", StringComparison.Ordinal)) return TokenType.LPAREN;
            if (string.Equals(value, ")", StringComparison.Ordinal)) return TokenType.RPAREN;
            if (string.Equals(value, "=", StringComparison.Ordinal)) return TokenType.EQUALS;
            if (string.Equals(value, "*", StringComparison.Ordinal)) return TokenType.STAR;
            if (string.Equals(value, "!", StringComparison.Ordinal)) return TokenType.BANG;

            if (Enum.TryParse<TokenType>(value.ToUpper(), out var type))
            {
                return type;
            }

            return null;
        }

        private string? GetLastStatementKeyword(List<Token> tokens, out string? ddlType)
        {
            ddlType = null;
            int start = tokens.Count - 1;
            while (start >= 0 && tokens[start].Type != TokenType.SEMICOLON)
            {
                start--;
            }
            start++; // Move to the first token of the current statement

            if (start < tokens.Count)
            {
                var first = tokens[start].Value;
                if (first.Equals("CREATE", StringComparison.OrdinalIgnoreCase) ||
                    first.Equals("ALTER", StringComparison.OrdinalIgnoreCase) ||
                    first.Equals("REPLACE", StringComparison.OrdinalIgnoreCase))
                {
                    if (start + 1 < tokens.Count)
                    {
                        ddlType = tokens[start + 1].Value.ToUpper();
                    }
                }
                return first.ToUpper();
            }
            return null;
        }

        private List<Token> TokenizeBody(string text)
        {
            return new Lexer(text).Tokenize().Where(t => t.Type != TokenType.EOF).ToList();
        }

        private List<Token> TokenizeQuery(string text)
        {
            return new Lexer(text).Tokenize().ToList();
        }
    }
}
