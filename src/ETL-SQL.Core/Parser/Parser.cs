using System;
using System.Collections.Generic;

using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser
{
    /// <summary>
    /// Recursive descent parser for the ETL-SQL language.
    /// Orchestrates expression and statement parsing.
    /// </summary>
    public class Parser : IParser
    {
        private readonly List<Token> _tokens;
        private int _position;
        private readonly string _source;
        private readonly ExpressionParser _expressionParser;
        private readonly StatementParser _statementParser;
        
        private static readonly HashSet<TokenType> IdentifierTokens = new()
        {
            TokenType.IDENTIFIER, TokenType.LINEAGE, TokenType.FILE, TokenType.DIRECTORY, 
            TokenType.SFTP, TokenType.FTP_CONN, TokenType.FLATFILE, TokenType.JSON, TokenType.XML, 
            TokenType.EXCEL, TokenType.AZURE_BLOB, TokenType.SYSDATE, TokenType.CURRENT_TIMESTAMP, 
            TokenType.CURRENT_DATE, TokenType.CURRENT_TIME, TokenType.YEAR, TokenType.MONTH, 
            TokenType.DAY, TokenType.HOUR, TokenType.MINUTE, TokenType.SECOND,
            TokenType.TELEMETRY, TokenType.POSITION, TokenType.FORMAT, TokenType.TARGET,
            TokenType.TYPE, TokenType.VERSION, TokenType.SOURCE, TokenType.MATCHED,
            TokenType.TABLE, TokenType.TAG, TokenType.VALUE, TokenType.BITS, 
            TokenType.ALGORITHM, TokenType.PASSPHRASE, TokenType.COMMENT, TokenType.DATE,
            TokenType.GETDATE, TokenType.RETURNS
        };

        private static readonly HashSet<TokenType> DataTypeTokens = new()
        {
            TokenType.TIME, TokenType.JSON, TokenType.XML, TokenType.DATETIME, TokenType.CHAR,
            TokenType.INT, TokenType.INTEGER, TokenType.BIGINT, TokenType.SMALLINT, TokenType.TINYINT, 
            TokenType.BIT, TokenType.BOOLEAN, TokenType.BOOL, TokenType.DECIMAL, TokenType.NUMERIC, 
            TokenType.MONEY, TokenType.SMALLMONEY, TokenType.FLOAT, TokenType.REAL, TokenType.DOUBLE, 
            TokenType.DATE, TokenType.DATETIME2, TokenType.SMALLDATETIME, TokenType.DATETIMEOFFSET, 
            TokenType.TIMESTAMP, TokenType.VARCHAR, TokenType.NCHAR, TokenType.NVARCHAR, 
            TokenType.TEXT, TokenType.NTEXT, TokenType.BINARY, TokenType.VARBINARY, TokenType.IMAGE, 
            TokenType.UNIQUEIDENTIFIER, TokenType.UUID, TokenType.GUID, TokenType.GEOMETRY, 
            TokenType.GEOGRAPHY, TokenType.HIERARCHYID, TokenType.VARIANT, TokenType.SQL_VARIANT, 
            TokenType.ANY, TokenType.TABLE, TokenType.STRING, TokenType.SENSITIVE, TokenType.SECRET,
            TokenType.VARCHAR2, TokenType.MINMAX, TokenType.MARKDOWN, TokenType.PATH
        };

        /// <summary>
        /// Initializes a new instance of the <see cref="Parser"/> class with the specified tokens.
        /// </summary>
        /// <param name="tokens">The list of tokens to parse.</param>
        public Parser(List<Token> tokens, string source = "")
        {
            _tokens = tokens;
            _source = source;
            _position = 0;
            _expressionParser = new ExpressionParser(this);
            _statementParser = new StatementParser(this);
        }

        public Token Current => _position < _tokens.Count ? _tokens[_position] : _tokens[^1];
        public Token Peek => _position + 1 < _tokens.Count ? _tokens[_position + 1] : _tokens[^1];
        public Token Peek2 => _position + 2 < _tokens.Count ? _tokens[_position + 2] : _tokens[^1];
        public Token Previous => _position > 0 ? _tokens[_position - 1] : _tokens[0];

        public Token LookAhead(int distance)
        {
            int pos = _position + distance;
            if (pos < 0) return _tokens[0];
            if (pos >= _tokens.Count) return _tokens[^1];
            return _tokens[pos];
        }

        public int LastTokenEndLine { get; private set; }
        public int LastTokenEndColumn { get; private set; }

        public void Backtrack()
        {
            if (_position > 0) _position--;
            // Minimal backtracking won't break simple range tracking for now.
            // In a better parser, we'd store a history of end positions.
        }

        public Token Advance()
        {
            var token = Current;
            if (token.Type != TokenType.EOF) 
            {
                LastTokenEndLine = token.EndLine;
                LastTokenEndColumn = token.EndColumn;
                _position++;
            }
            return token;
        }

        public bool Match(TokenType type)
        {
            if (Current.Type == type)
            {
                Advance();
                return true;
            }
            return false;
        }

        public Token Consume(TokenType type, string message)
        {
            if (Current.Type == type) {
                var t = Advance();
                return t;
            }
            throw new SyntaxException(message, Current.Line, Current.Column);
        }

        public Token ConsumeIdentifier(string message)
        {
            if (IsIdentifier(Current)) return Advance();
            throw new SyntaxException(message, Current.Line, Current.Column);
        }

        public bool IsIdentifier(Token token)
        {
            if (IdentifierTokens.Contains(token.Type)) return true;
            if (token.Type == TokenType.EOF) return false;

            // Connector types from metadata are always valid identifiers
            if (LanguageMetadata.IsConnectorType(token.Value)) return true;

            // Data types are allowed as identifiers in many contexts
            if (IsDataType(token.Type)) return true;

            // Report-SQL and overlay tokens are contextual: reserved inside their own clauses,
            // but should be allowed as identifiers/function names elsewhere.
            if (token.Type >= TokenType.VISUAL && token.Type <= TokenType.COLOR)
                return true;


            // Symbols and operators should never be identifiers.
            if (token.Type >= TokenType.STAR && token.Type < TokenType.VISUAL) return false;

            // Specific keyword exceptions
            var val = token.Value;
            if (val.Equals("VALUE", StringComparison.OrdinalIgnoreCase)) return true;
            if (val.Equals("EMAIL", StringComparison.OrdinalIgnoreCase)) return true;
            
            if (!IsKeyword(val)) return true;

            // Context-aware keyword handling for common join/set keywords used as identifiers/aliases.
            // This allows patterns like "FROM #SmallTable inner" while preserving "INNER JOIN".
            if (token.Type >= TokenType.JOIN && token.Type <= TokenType.INTERSECT)
            {
                // JOIN, UNION, EXCEPT, INTERSECT are strictly reserved as clause starters.
                if (token.Type == TokenType.JOIN || token.Type == TokenType.UNION || 
                    token.Type == TokenType.EXCEPT || token.Type == TokenType.INTERSECT)
                    return false;

                // Join modifiers (INNER, LEFT, etc.) can be aliases if they are not followed 
                // by tokens that unambiguously start a JOIN clause.
                var next = Peek;
                if (next.Type == TokenType.JOIN || next.Type == TokenType.HASH || 
                    next.Type == TokenType.LOOP || next.Type == TokenType.MERGE || 
                    next.Type == TokenType.APPLY || next.Type == TokenType.OUTER ||
                    next.Type == TokenType.INNER || next.Type == TokenType.LEFT ||
                    next.Type == TokenType.RIGHT || next.Type == TokenType.FULL ||
                    next.Type == TokenType.CROSS)
                    return false;
                
                return true; 
            }

            return false;
        }

        public bool IsDataType(TokenType type) => DataTypeTokens.Contains(type);

        /// <summary>
        /// Parses the entire token stream into a <see cref="Script"/> object.
        /// </summary>
        /// <returns>A script containing the parsed statements and any diagnostics.</returns>
        public Script Parse()
        {
            var script = new Script();
            
            // Capture script metadata from header comments
            while (Current.Type == TokenType.COLUMN_TAG)
            {
                var comment = Advance().Value;
                var lines = comment.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("@"))
                    {
                        var parts = trimmedLine.Substring(1).Split(':', 2);
                        if (parts.Length == 2)
                        {
                            script.Metadata[parts[0].Trim()] = parts[1].Trim();
                        }
                    }
                }
            }

            while (Current.Type != TokenType.EOF)
            {
                try
                {
                    if (Match(TokenType.SEMICOLON)) continue;
                    if (Current.Type == TokenType.COLUMN_TAG) { Advance(); continue; } // Skip inline tags

                    script.Statements.Add(ParseStatement());
                }
                catch (SyntaxException ex)
                {
                    script.Diagnostics.Add(new Diagnostic(ex.Message, ex.Line, ex.Column, DiagnosticSeverity.Error, "SYNTAX"));
                    // Error recovery: skip to next semicolon or EOF
                    while (Current.Type != TokenType.EOF && Current.Type != TokenType.SEMICOLON)
                    {
                        Advance();
                    }
                    if (Current.Type == TokenType.SEMICOLON) Advance();
                }
                catch (Exception ex)
                {
                    script.Diagnostics.Add(new Diagnostic(ex.Message, Current.Line, Current.Column, DiagnosticSeverity.Error, "INTERNAL"));
                     // Fallback for unexpected errors
                     if (Current.Type != TokenType.EOF) Advance();
                }
            }
            return script;
        }

        /// <summary>Parses a single statement from the current position.</summary>
        public Statement ParseStatement()
        {
            return _statementParser.ParseStatement();
        }


        /// <summary>Parses a top-level query (SELECT or set operation).</summary>
        public Statement ParseQuery()
        {
            if (!Match(TokenType.SELECT)) throw new SyntaxException("Expected SELECT", Current.Line, Current.Column);
            var left = ParseSelectBody();

            while (Current.Type == TokenType.UNION || Current.Type == TokenType.EXCEPT || Current.Type == TokenType.INTERSECT)
            {
                var op = SetOpType.UNION;
                if (Match(TokenType.UNION))
                {
                    op = Match(TokenType.ALL) ? SetOpType.UNION_ALL : SetOpType.UNION;
                }
                else if (Match(TokenType.EXCEPT))
                {
                    op = SetOpType.EXCEPT;
                }
                else if (Match(TokenType.INTERSECT))
                {
                    op = SetOpType.INTERSECT;
                }

                Consume(TokenType.SELECT, "Expected SELECT after set operator");
                var right = ParseSelectBody();
                left = new SetOperationStatement(left, op, right);
            }

            // Global semicolon consumption for top level queries
            if (Current.Type == TokenType.SEMICOLON) Advance();
            return left;
        }

        public string CaptureRawBlock()
        {
            // Assumes Current is BEGIN
            var startToken = Consume(TokenType.BEGIN, "Expected BEGIN");
            int depth = 1;
            var tokens = new List<Token>();

            while (depth > 0 && Current.Type != TokenType.EOF)
            {
                if (Current.Type == TokenType.BEGIN) depth++;
                else if (Current.Type == TokenType.END) depth--;
                
                if (depth == 0) break;
                tokens.Add(Advance());
            }

            var endToken = Consume(TokenType.END, "Expected END");
            
            if (!string.IsNullOrEmpty(_source)) 
                return ReconstructFromSource(startToken, endToken);

            // Fallback: reconstruct from tokens if source is missing
            return string.Join(" ", tokens.Select(t => t.Type == TokenType.STRING_LITERAL ? $"'{t.Value.Replace("'", "''")}'" : t.Value));
        }

        private string ReconstructFromSource(Token startToken, Token endToken)
        {
            if (string.IsNullOrEmpty(_source)) return "";
            int start = startToken.EndOffset;
            int end = endToken.Offset;
            if (start < 0 || end > _source.Length || start > end) return "";
            return _source.Substring(start, end - start).Trim();
        }

        private Statement ParseSelect()
        {
            return ParseQuery();
        }

        private Statement ParseSelectBody()
        {
            var startToken = Current;
            bool isDistinct = Match(TokenType.DISTINCT);
            Expression? topCount = null;
            bool isTopPercent = false;
            bool withTies = false;
            if (Match(TokenType.TOP))
            {
                bool hasParen = Match(TokenType.LPAREN);
                if (Current.Type == TokenType.NUMBER)
                {
                    var t = Advance();
                    topCount = new LiteralExpression(decimal.Parse(t.Value), TokenType.NUMBER) { Line = t.Line, Column = t.Column };
                }
                else if (Current.Type == TokenType.VARIABLE)
                {
                    var t = Advance();
                    topCount = new VariableExpression(t.Value) { Line = t.Line, Column = t.Column };
                }
                else throw new SyntaxException("Expected number or variable after TOP", Current.Line, Current.Column);

                if (hasParen) Consume(TokenType.RPAREN, "Expected ')' after TOP");

                if (Match(TokenType.PERCENT)) isTopPercent = true;
                if (Match(TokenType.WITH))
                {
                    Consume(TokenType.TIES, "Expected 'TIES' after 'WITH' in TOP clause");
                    withTies = true;
                }
            }

            // SELECT col1, col2, ... [INTO table] FROM table [WHERE cond]
            var columns = new List<SelectColumn>();
            TableReference? intoTable = null;

                if (Match(TokenType.STAR))
                {
                    columns.Add(new SelectColumn(new IdentifierExpression("*"), null, null));
                }
                else
                {
                    columns.Add(ParseSelectColumn());
                    while (Match(TokenType.COMMA))
                    {
                        columns.Add(ParseSelectColumn());
                    }
                }

                if (Match(TokenType.INTO))
                {
                    intoTable = ParseTableReference(allowAlias: false);
                }
            
            // Riverside: removed swallowed catch block to expose errors.
            TableReference? fromTable = null;
            if (Match(TokenType.FROM))
            {
                fromTable = ParseTableReference();
            }
            else
            {
                // Internal "Dual" style table for empty FROM
                fromTable = new TableReference("DUAL");
            }

            var joins = ParseJoins();
            
            Expression? whereClause = null;
            if (Match(TokenType.WHERE))
            {
                whereClause = ParseExpression();
            }

            List<Expression>? groupBy = null;
            GroupingSetClause? groupingSet = null;
            if (Match(TokenType.GROUP))
            {
                Consume(TokenType.BY, "Expected 'BY' after 'GROUP'");
                if (Current.Type == TokenType.ROLLUP)
                {
                    Advance();
                    Consume(TokenType.LPAREN, "Expected '(' after ROLLUP");
                    var cols = ParseCommaSeparatedExpressions();
                    Consume(TokenType.RPAREN, "Expected ')' after ROLLUP columns");
                    groupingSet = new GroupingSetClause(GroupingSetType.Rollup, new List<List<Expression>> { cols });
                    groupBy = cols; // plain fallback columns (engine will expand)
                }
                else if (Current.Type == TokenType.CUBE)
                {
                    Advance();
                    Consume(TokenType.LPAREN, "Expected '(' after CUBE");
                    var cols = ParseCommaSeparatedExpressions();
                    Consume(TokenType.RPAREN, "Expected ')' after CUBE columns");
                    groupingSet = new GroupingSetClause(GroupingSetType.Cube, new List<List<Expression>> { cols });
                    groupBy = cols;
                }
                else if (Current.Type == TokenType.GROUPING && Peek.Type == TokenType.SETS)
                {
                    Advance(); // GROUPING
                    Advance(); // SETS
                    Consume(TokenType.LPAREN, "Expected '(' after GROUPING SETS");
                    var sets = new List<List<Expression>>();
                    do
                    {
                        Consume(TokenType.LPAREN, "Expected '(' for each grouping set");
                        var setExprs = new List<Expression>();
                        if (Current.Type != TokenType.RPAREN)
                        {
                            setExprs.Add(ParseExpression());
                            while (Match(TokenType.COMMA)) setExprs.Add(ParseExpression());
                        }
                        Consume(TokenType.RPAREN, "Expected ')' to close grouping set");
                        sets.Add(setExprs);
                    } while (Match(TokenType.COMMA));
                    Consume(TokenType.RPAREN, "Expected ')' to close GROUPING SETS");
                    groupingSet = new GroupingSetClause(GroupingSetType.GroupingSets, sets);
                    // Collect all distinct expressions from the sets as the plain groupBy for compatibility
                    groupBy = sets.SelectMany(s => s).Distinct().ToList();
                }
                else
                {
                    groupBy = new List<Expression>();
                    groupBy.Add(ParseExpression());
                    while (Match(TokenType.COMMA))
                    {
                        groupBy.Add(ParseExpression());
                    }
                }
            }

            Expression? havingClause = null;
            if (Match(TokenType.HAVING))
            {
                havingClause = ParseExpression();
            }

            List<OrderByClause>? orderBy = null;
            if (Current.Type == TokenType.ORDER)
            {
                Advance(); // ORDER
                Consume(TokenType.BY, "Expected BY after ORDER");
                orderBy = new List<OrderByClause>();
                do
                {
                    var orderExpr = ParseExpression();
                    bool descending = false;
                    if (Current.Type == TokenType.DESC)
                    {
                        descending = true;
                        Advance();
                    }
                    else if (Current.Type == TokenType.ASC)
                    {
                        Advance(); // optional ASC
                    }
                    orderBy.Add(new OrderByClause(orderExpr, descending));
                }
                while (Match(TokenType.COMMA));
            }

            Expression? limitCount = null;
            if (Match(TokenType.LIMIT))
            {
                if (Current.Type == TokenType.NUMBER)
                {
                    var t = Advance();
                    limitCount = new LiteralExpression(decimal.Parse(t.Value), TokenType.NUMBER) { Line = t.Line, Column = t.Column };
                }
                else if (Current.Type == TokenType.VARIABLE)
                {
                    var t = Advance();
                    limitCount = new VariableExpression(t.Value) { Line = t.Line, Column = t.Column };
                }
                else throw new SyntaxException("Expected number or variable after LIMIT", Current.Line, Current.Column);
            }

            Expression? offset = null;
            if (Match(TokenType.OFFSET))
            {
                offset = ParseExpression();
                if (!Match(TokenType.ROWS)) Match(TokenType.ROW);
            }

            var selectStmt = new SelectStatement(columns, intoTable, fromTable, joins, whereClause, groupBy, havingClause, orderBy)
            {
                Line = startToken.Line,
                Column = startToken.Column,
                EndLine = LastTokenEndLine,
                EndColumn = LastTokenEndColumn,
                IsDistinct = isDistinct,
                TopCount = topCount,
                IsTopPercent = isTopPercent,
                WithTies = withTies,
                LimitCount = limitCount,
                Offset = offset,
                GroupingSet = groupingSet
            };

            if (Match(TokenType.FOR))
            {
                selectStmt = selectStmt with 
                { 
                    ForClause = ParseForClause(),
                    EndLine = LastTokenEndLine,
                    EndColumn = LastTokenEndColumn
                };
            }

            return selectStmt;
        }

        private List<Expression> ParseCommaSeparatedExpressions()
        {
            var result = new List<Expression>();
            if (Current.Type != TokenType.RPAREN)
            {
                result.Add(ParseExpression());
                while (Match(TokenType.COMMA)) result.Add(ParseExpression());
            }
            return result;
        }

        private ForClause ParseForClause()
        {
            ForType type;
            if (Match(TokenType.JSON)) type = ForType.JSON;
            else if (Match(TokenType.XML)) type = ForType.XML;
            else throw new SyntaxException("Expected JSON or XML after FOR", Current.Line, Current.Column);

            ForMode mode = ForMode.PATH;
            if (Match(TokenType.PATH)) mode = ForMode.PATH;
            else if (Match(TokenType.AUTO)) mode = ForMode.AUTO;
            else if (Match(TokenType.RAW)) mode = ForMode.RAW;
            else if (Match(TokenType.EXPLICIT)) mode = ForMode.EXPLICIT;
            
            string? rootName = null;
            bool includeNulls = false;
            bool withoutWrapper = false;
            bool useElements = false;

            while (Match(TokenType.COMMA) || 
                   Current.Type == TokenType.ROOT || 
                   Current.Type == TokenType.INCLUDE_NULL_VALUES || 
                   Current.Type == TokenType.WITHOUT_ARRAY_WRAPPER ||
                   Current.Type == TokenType.ELEMENTS)
            {
                if (Match(TokenType.ROOT))
                {
                    if (Match(TokenType.LPAREN))
                    {
                        rootName = Consume(TokenType.STRING_LITERAL, "Expected root name string").Value;
                        Consume(TokenType.RPAREN, "Expected ')' after root name");
                    }
                    else
                    {
                        // Some dialects allow ROOT without parens or with a string immediately
                        if (Current.Type == TokenType.STRING_LITERAL) rootName = Advance().Value;
                        else rootName = "root";
                    }
                }
                else if (Match(TokenType.INCLUDE_NULL_VALUES))
                {
                    includeNulls = true;
                }
                else if (Match(TokenType.WITHOUT_ARRAY_WRAPPER))
                {
                    withoutWrapper = true;
                }
                else if (Match(TokenType.ELEMENTS))
                {
                    useElements = true;
                }
                else if (Previous.Type != TokenType.COMMA)
                {
                    break;
                }
            }

            return new ForClause(type, mode, rootName) 
            { 
                IncludeNullValues = includeNulls, 
                WithoutArrayWrapper = withoutWrapper,
                UseElements = useElements
            };
        }

        private Statement ParseSetOperation(Statement left)
        {
            SetOpType op = SetOpType.UNION;
            if (Match(TokenType.UNION))
            {
                op = Match(TokenType.ALL) ? SetOpType.UNION_ALL : SetOpType.UNION;
            }
            else if (Match(TokenType.EXCEPT)) op = SetOpType.EXCEPT;
            else if (Match(TokenType.INTERSECT)) op = SetOpType.INTERSECT;

            Consume(TokenType.SELECT, "Expected 'SELECT' after UNION/EXCEPT/INTERSECT");
            var right = ParseSelectBody(); // Parse only the body
            var setStmt = new SetOperationStatement(left, op, right);

            // Recursively check for more set operations
            if (Current.Type == TokenType.UNION || Current.Type == TokenType.EXCEPT || Current.Type == TokenType.INTERSECT)
            {
                return ParseSetOperation(setStmt);
            }

            return setStmt;
        }

        public string ParseType()
        {
            var startToken = Current;
            string typeName = ConsumeIdentifier("Expected type name").Value;
            if (Match(TokenType.LPAREN))
            {
                if (Current.Type == TokenType.NUMBER)
                {
                    typeName += "(" + Consume(TokenType.NUMBER, "Expected length").Value;
                    if (Match(TokenType.COMMA))
                    {
                        typeName += "," + Consume(TokenType.NUMBER, "Expected scale").Value;
                    }
                    Consume(TokenType.RPAREN, "Expected ')'");
                    typeName += ")";
                }
                else if (IsIdentifier(Current) || Current.Type == TokenType.MAX)
                {
                    typeName += "(" + Advance().Value + ")";
                    Consume(TokenType.RPAREN, "Expected ')' after type parameter");
                }
                else
                {
                    Consume(TokenType.RPAREN, "Expected ')' for empty type parameters");
                    typeName += "()";
                }
            }
            return typeName;
        }

        public TableReference ParseTableReference(bool allowFunction = true, bool allowWithClause = true, bool allowAlias = true)
        {
            var t = Current;
            
            TableReference tableRef;
            
            // Subquery Support: (SELECT ...) [AS] Alias
            if (Match(TokenType.LPAREN))
            {
                if (Current.Type == TokenType.SELECT)
                {
                    var subquery = ParseQuery();
                    Consume(TokenType.RPAREN, "Expected ')' after subquery in FROM/JOIN");
                    
                    string alias = "";
                    if (Match(TokenType.AS))
                    {
                        alias = ConsumeIdentifier("Expected alias after AS for subquery").Value;
                    }
                    else if (Current.Type == TokenType.IDENTIFIER)
                    {
                        alias = Advance().Value;
                    }
                    else
                    {
                        alias = "Sub_" + new Random().Next(1000, 9999);
                    }
                    
                    tableRef = new TableReference("SUBQUERY", null, null, null, alias, subquery);
                }
                else
                {
                    throw new SyntaxException("Expected SELECT after '(' in FROM/JOIN table reference", Current.Line, Current.Column);
                }
            }
            else
            {
                var parts = new List<string>();
                
                if (Current.Type == TokenType.VARIABLE)
                {
                    parts.Add(Consume(TokenType.VARIABLE, "Expected variable table reference").Value);
                }
                else if (Current.Type == TokenType.STRING_LITERAL)
                {
                    parts.Add(Consume(TokenType.STRING_LITERAL, "Expected string for table reference").Value);
                }
                else
                {
                    parts.Add(ConsumeIdentifier("Expected identifier for table reference").Value);
                }

                while (Match(TokenType.DOT))
                {
                    // After a dot in a table reference (schema.table, conn.table), any word token is valid —
                    // keywords like TABLE, INDEX, etc. are commonly used as table names.
                    if (Current.Type < TokenType.STAR)
                        parts.Add(Advance().Value);
                    else
                        break;
                }

                // Capture table-level metadata if available before alias
                var tableMetadata = _expressionParser.ParseMetadataTags();

                if (allowFunction && Match(TokenType.LPAREN))
                {
                    var args = new List<Expression>();
                    if (!Match(TokenType.RPAREN))
                    {
                        args.Add(_expressionParser.ParseExpression());
                        while (Match(TokenType.COMMA))
                        {
                            args.Add(_expressionParser.ParseExpression());
                        }
                        Consume(TokenType.RPAREN, "Expected ')' after function arguments");
                    }
                    var funcName = parts[^1];
                    var funcCall = new FunctionCallExpression(funcName, args) { Line = t.Line, Column = t.Column };
                    
                    if (parts.Count == 4) tableRef = new TableReference(parts[3], parts[2], parts[1], parts[0], functionCall: funcCall);
                    else if (parts.Count == 3) tableRef = new TableReference(parts[2], parts[1], null, parts[0], functionCall: funcCall);
                    else if (parts.Count == 2) tableRef = new TableReference(parts[1], null, null, parts[0], functionCall: funcCall);
                    else tableRef = new TableReference(funcName, functionCall: funcCall);
                }
                else if (parts.Count == 4)
                {
                    tableRef = new TableReference(parts[3], parts[2], parts[1], parts[0]);
                }
                else if (parts.Count == 3)
                {
                    tableRef = new TableReference(parts[2], parts[1], null, parts[0]);
                }
                else if (parts.Count == 2)
                {
                    tableRef = new TableReference(parts[1], null, null, parts[0]);
                }
                else
                {
                    tableRef = new TableReference(parts[0]);
                }

                // Optional Alias
                if (allowAlias)
                {
                    if (Match(TokenType.AS))
                    {
                        tableRef = new TableReference(tableRef.TableName, tableRef.SchemaName, tableRef.DatabaseName, tableRef.ConnectionName, ConsumeIdentifier("Expected alias after AS").Value);
                        // Tags after alias: mytable AS c /* @tag: val */
                        var aliasMetadata = _expressionParser.ParseMetadataTags();
                        foreach (var tag in aliasMetadata) tableRef.Metadata[tag.Key] = tag.Value;
                    }
                    else if (IsIdentifier(Current))
                    {
                        // Implicit alias
                        tableRef = new TableReference(tableRef.TableName, tableRef.SchemaName, tableRef.DatabaseName, tableRef.ConnectionName, Advance().Value);
                        var aliasMetadata = _expressionParser.ParseMetadataTags();
                        foreach (var tag in aliasMetadata) tableRef.Metadata[tag.Key] = tag.Value;
                    }
                }

                // Merge table-level metadata
                if (tableMetadata != null)
                {
                    foreach (var tag in tableMetadata) tableRef.Metadata[tag.Key] = tag.Value;
                }
            }

            tableRef = tableRef with 
            { 
                Line = t.Line, 
                Column = t.Column, 
                EndLine = LastTokenEndLine, 
                EndColumn = LastTokenEndColumn 
            };

            // Handle PIVOT/UNPIVOT operators
            while (Current.Type == TokenType.PIVOT || Current.Type == TokenType.UNPIVOT)
            {
                if (Match(TokenType.PIVOT)) tableRef.TableOperators.Add(ParsePivotClause());
                else if (Match(TokenType.UNPIVOT)) tableRef.TableOperators.Add(ParseUnpivotClause());
                tableRef = tableRef with 
                { 
                    EndLine = LastTokenEndLine, 
                    EndColumn = LastTokenEndColumn 
                };
            }

            // Handle WITH (...) options
            if (allowWithClause && Match(TokenType.WITH))
            {
                Consume(TokenType.LPAREN, "Expected '(' after WITH");
                while (Current.Type != TokenType.RPAREN && Current.Type != TokenType.EOF)
                {
                    // Permissive key: allow identifiers or any keyword (needed for MOCKDB(COLUMNS=...))
                    string key;
                    if (IsIdentifier(Current) || LanguageMetadata.IsKeyword(Current.Value))
                        key = Advance().Value;
                    else
                        throw new SyntaxException("Expected option key", Current.Line, Current.Column);
                    Match(TokenType.EQUALS);
                    var val = _expressionParser.ParseExpression();
                    tableRef.Options[key] = val;
                    if (!Match(TokenType.COMMA)) break;
                }
                Consume(TokenType.RPAREN, "Expected ')' to close WITH options");
                tableRef = tableRef with 
                { 
                    EndLine = LastTokenEndLine, 
                    EndColumn = LastTokenEndColumn 
                };
            }

            return tableRef;
        }

        private PivotClause ParsePivotClause()
        {
            Consume(TokenType.LPAREN, "Expected '(' after PIVOT");
            var aggFunc = ConsumeIdentifier("Expected aggregate function name").Value;
            Consume(TokenType.LPAREN, "Expected '(' after aggregate function");
            
            // Support both COUNT(*) and COUNT(Col)
            string aggCol = "";
            if (Match(TokenType.STAR)) aggCol = "*";
            else aggCol = ConsumeIdentifier("Expected aggregate column name").Value;
            
            Consume(TokenType.RPAREN, "Expected ')' after aggregate column");
            Consume(TokenType.FOR, "Expected 'FOR' in PIVOT clause");
            var pivotCol = ConsumeIdentifier("Expected pivot column name").Value;
            Consume(TokenType.IN, "Expected 'IN' in PIVOT clause");
            Consume(TokenType.LPAREN, "Expected '(' before pivot values");
            var values = new List<Expression>();
            values.Add(ParseExpression());
            while (Match(TokenType.COMMA))
            {
                values.Add(ParseExpression());
            }
            Consume(TokenType.RPAREN, "Expected ')' after pivot values");
            Consume(TokenType.RPAREN, "Expected ')' to close PIVOT clause");
            
            string? alias = null;
            if (Match(TokenType.AS)) alias = ConsumeIdentifier("Expected alias after PIVOT").Value;
            else if (Current.Type == TokenType.IDENTIFIER && !IsKeyword(Current.Value)) alias = Advance().Value;
            
            return new PivotClause(aggFunc, aggCol, pivotCol, values) { Alias = alias };
        }

        private UnpivotClause ParseUnpivotClause()
        {
            Consume(TokenType.LPAREN, "Expected '(' after UNPIVOT");
            var valCol = ConsumeIdentifier("Expected value column name").Value;
            Consume(TokenType.FOR, "Expected 'FOR' in UNPIVOT clause");
            var nameCol = ConsumeIdentifier("Expected name column name").Value;
            Consume(TokenType.IN, "Expected 'IN' in UNPIVOT clause");
            Consume(TokenType.LPAREN, "Expected '(' before unpivot columns");
            var cols = new List<string>();
            cols.Add(ConsumeIdentifier("Expected column name").Value);
            while (Match(TokenType.COMMA))
            {
                cols.Add(ConsumeIdentifier("Expected column name").Value);
            }
            Consume(TokenType.RPAREN, "Expected ')' after unpivot columns");
            Consume(TokenType.RPAREN, "Expected ')' to close UNPIVOT clause");
            
            string? alias = null;
            if (Match(TokenType.AS)) alias = ConsumeIdentifier("Expected alias after UNPIVOT").Value;
            else if (Current.Type == TokenType.IDENTIFIER && !IsKeyword(Current.Value)) alias = Advance().Value;
            
            return new UnpivotClause(valCol, nameCol, cols) { Alias = alias };
        }

        public OutputClause ParseOutputClause()
        {
            var columns = new List<SelectColumn>();
            do
            {
                columns.Add(ParseSelectColumn());
            } while (Match(TokenType.COMMA));

            TableReference? intoTable = null;
            if (Match(TokenType.INTO))
            {
                intoTable = ParseTableReference();
            }

            return new OutputClause(columns, intoTable);
        }

        // Pre-computed static set derived from LanguageMetadata — single source of truth, zero allocation per call.
        // Supplemented with a few parser-specific tokens that must never be treated as implicit aliases.
        private static readonly HashSet<string> _reservedKeywords = BuildReservedKeywords();

        private static HashSet<string> BuildReservedKeywords()
        {
            var set = new HashSet<string>(ETL_SQL.Common.LanguageMetadata.GetAllKeywords(), StringComparer.OrdinalIgnoreCase);
            // Parser-level tokens not in LanguageMetadata (internal SQL keywords used during parsing)
            set.Add("INSERTED"); set.Add("DELETED"); set.Add("ACTION");
            return set;
        }

        private bool IsKeyword(string value) => _reservedKeywords.Contains(value);

        public SelectColumn ParseSelectColumn()
        {
            Expression expr;
            if (Current.Type == TokenType.STAR)
            {
                var t = Advance();
                expr = new IdentifierExpression("*") { Line = t.Line, Column = t.Column };
            }
            else
            {
                expr = ParseExpression();
            }
            
            string? alias = null;
            if (Match(TokenType.AS))
            {
                if (IsIdentifier(Current) || Current.Type == TokenType.STRING_LITERAL)
                {
                    alias = Advance().Value;
                }
                else
                {
                    throw new SyntaxException($"Expected alias after AS", Current.Line, Current.Column);
                }
            }
            else if (IsIdentifier(Current))
            {
                alias = Advance().Value;
            }

            Dictionary<string, string>? metadata = null;
            while (Match(TokenType.COLUMN_TAG))
            {
                if (metadata == null) metadata = new(StringComparer.OrdinalIgnoreCase);
                ParseMetadataTags(Previous.Value, metadata);
            }

            var col = new SelectColumn(expr, alias, metadata)
            {
                Line = expr.Line,
                Column = expr.Column,
                EndLine = LastTokenEndLine,
                EndColumn = LastTokenEndColumn
            };
            return col;
        }

        public void ParseMetadataTags(string tagContent, Dictionary<string, string> metadata)
        {
            // Expected format: @tag: value; @tag2: value2;
            var parts = tagContent.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var part in parts)
            {
                if (!part.StartsWith("@")) continue;
                
                var colonIndex = part.IndexOf(':');
                if (colonIndex > 0)
                {
                    var tagName = part.Substring(1, colonIndex - 1).Trim();
                    var tagValue = part.Substring(colonIndex + 1).Trim();
                    metadata[tagName] = tagValue;
                }
                else
                {
                    // Handle @tag (boolean existence or just key name)
                    metadata[part.Substring(1).Trim()] = "true";
                }
            }
        }

        public Expression ParseExpression() => _expressionParser.ParseExpression();

        public List<JoinClause> ParseJoins()
        {
            var joins = new List<JoinClause>();
            while (Current.Type == TokenType.JOIN || Current.Type == TokenType.INNER || Current.Type == TokenType.LEFT || 
                   Current.Type == TokenType.RIGHT || Current.Type == TokenType.OUTER || Current.Type == TokenType.FULL || 
                   Current.Type == TokenType.CROSS || Current.Type == TokenType.APPLY || Current.Type == TokenType.SEMI || 
                   Current.Type == TokenType.ANTI || Current.Type == TokenType.HASH || Current.Type == TokenType.LOOP || Current.Type == TokenType.MERGE)
            {
                string joinType = "INNER";
                JoinHint hint = JoinHint.None;

                if (Match(TokenType.INNER)) 
                { 
                    joinType = "INNER"; 
                    if (Match(TokenType.HASH)) hint = JoinHint.Hash;
                    else if (Match(TokenType.LOOP)) hint = JoinHint.Loop;
                    else if (Match(TokenType.MERGE)) hint = JoinHint.Merge;
                    Consume(TokenType.JOIN, "Expected 'JOIN' after INNER [hint]"); 
                }
                else if (Match(TokenType.LEFT))
                {
                    joinType = "LEFT"; Match(TokenType.OUTER);
                    if (Match(TokenType.SEMI)) { joinType = "LEFT SEMI"; Consume(TokenType.JOIN, "Expected 'JOIN' after LEFT SEMI"); }
                    else if (Match(TokenType.ANTI)) { joinType = "LEFT ANTI"; Consume(TokenType.JOIN, "Expected 'JOIN' after LEFT ANTI"); }
                    else
                    {
                        if (Match(TokenType.HASH)) hint = JoinHint.Hash;
                        else if (Match(TokenType.LOOP)) hint = JoinHint.Loop;
                        else if (Match(TokenType.MERGE)) hint = JoinHint.Merge;
                        Consume(TokenType.JOIN, "Expected 'JOIN'");
                    }
                }
                else if (Match(TokenType.RIGHT)) 
                { 
                    joinType = "RIGHT"; Match(TokenType.OUTER); 
                    if (Match(TokenType.HASH)) hint = JoinHint.Hash;
                    else if (Match(TokenType.LOOP)) hint = JoinHint.Loop;
                    else if (Match(TokenType.MERGE)) hint = JoinHint.Merge;
                    Consume(TokenType.JOIN, "Expected 'JOIN'"); 
                }
                else if (Match(TokenType.FULL)) 
                { 
                    joinType = "FULL"; Match(TokenType.OUTER); 
                    if (Match(TokenType.HASH)) hint = JoinHint.Hash;
                    else if (Match(TokenType.LOOP)) hint = JoinHint.Loop;
                    else if (Match(TokenType.MERGE)) hint = JoinHint.Merge;
                    Consume(TokenType.JOIN, "Expected 'JOIN'"); 
                }
                else if (Match(TokenType.HASH))
                {
                    hint = JoinHint.Hash;
                    Consume(TokenType.JOIN, "Expected 'JOIN' after HASH");
                }
                else if (Match(TokenType.LOOP))
                {
                    hint = JoinHint.Loop;
                    Consume(TokenType.JOIN, "Expected 'JOIN' after LOOP");
                }
                else if (Match(TokenType.MERGE))
                {
                    hint = JoinHint.Merge;
                    Consume(TokenType.JOIN, "Expected 'JOIN' after MERGE");
                }
                else if (Match(TokenType.CROSS)) 
                {
                    if (Match(TokenType.APPLY)) joinType = "CROSS APPLY";
                    else if (Match(TokenType.JOIN)) joinType = "CROSS JOIN";
                    else throw new SyntaxException("Expected JOIN or APPLY after CROSS", Current.Line, Current.Column);
                }
                else if (Match(TokenType.OUTER))
                {
                    if (Match(TokenType.APPLY)) joinType = "OUTER APPLY";
                    else { joinType = "LEFT"; Consume(TokenType.JOIN, "Expected 'JOIN' after 'OUTER'"); }
                }
                else if (Match(TokenType.SEMI))
                {
                    joinType = "SEMI";
                    Consume(TokenType.JOIN, "Expected 'JOIN' after 'SEMI'");
                }
                else if (Match(TokenType.ANTI))
                {
                    joinType = "ANTI";
                    Consume(TokenType.JOIN, "Expected 'JOIN' after 'ANTI'");
                }
                else if (Match(TokenType.APPLY)) { joinType = "CROSS APPLY"; }
                else if (Match(TokenType.JOIN)) { joinType = "INNER"; }

                var joinTable = ParseTableReference();
                Expression? onCondition = null;
                if (!joinType.Contains("APPLY") && joinType != "CROSS JOIN")
                {
                    Consume(TokenType.ON, $"Expected 'ON' after {joinType} JOIN table");
                    onCondition = ParseExpression();
                }
                else if (joinType == "CROSS JOIN" || joinType.Contains("APPLY"))
                {
                    onCondition = new LiteralExpression(true, TokenType.NUMBER);
                }
                
                joins.Add(new JoinClause(joinType, joinTable, onCondition!, hint));
            }
            return joins;
        }
    }
}

