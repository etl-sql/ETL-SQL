using System;
using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser
{
    public partial class StatementParser
    {
        private Statement ParseDeclare()
        {
            var startToken = _parser.Previous;
            var declares = new List<Statement>();

            do
            {
                var varToken = _parser.Consume(TokenType.VARIABLE, "Expected variable name starting with '@'");
                string type = "ANY";
                if (_parser.IsIdentifier(_parser.Current))
                {
                    type = _parser.ParseType();
                }
                
                bool isSensitive = _parser.Match(TokenType.PASSWORD);
                bool isInput = _parser.Match(TokenType.INPUT);
                bool isOutput = _parser.Match(TokenType.OUTPUT);

                Expression? initialValue = null;
                if (_parser.Match(TokenType.EQUALS))
                {
                    initialValue = _parser.ParseExpression();
                }

                if (!isSensitive) isSensitive = _parser.Match(TokenType.PASSWORD);
                if (!isInput) isInput = _parser.Match(TokenType.INPUT);
                if (!isOutput) isOutput = _parser.Match(TokenType.OUTPUT);

                Dictionary<string, string>? metadata = null;
                while (_parser.Match(TokenType.COLUMN_TAG))
                {
                    if (metadata == null) metadata = new(StringComparer.OrdinalIgnoreCase);
                    _parser.ParseMetadataTags(_parser.Previous.Value, metadata);
                }

                var stmt = new DeclareStatement(varToken.Value, type, initialValue, isSensitive, isInput, isOutput, metadata) 
                { 
                    Line = varToken.Line, 
                    Column = varToken.Column,
                    EndLine = _parser.LastTokenEndLine,
                    EndColumn = _parser.LastTokenEndColumn,
                    IsSensitive = isSensitive,
                    IsInput = isInput,
                    IsOutput = isOutput
                };
                
                declares.Add(stmt);

            } while (_parser.Match(TokenType.COMMA));

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            if (declares.Count == 1) return declares[0];
            return new BlockStatement(declares) { Line = startToken.Line, Column = startToken.Column, EndLine = _parser.LastTokenEndLine, EndColumn = _parser.LastTokenEndColumn };
        }

        private Statement ParseSetVariable()
        {
            var startToken = _parser.Previous;
            var varToken = _parser.Consume(TokenType.VARIABLE, "Expected variable name starting with '@'");
            _parser.Consume(TokenType.EQUALS, "Expected '=' in SET statement");
            var expr = _parser.ParseExpression();

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new SetVariableStatement(varToken.Value, expr)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        private Statement ParseSetProfiling()
        {
            var enabled = true;
            if (_parser.Match(TokenType.ON)) enabled = true;
            else if (_parser.Match(TokenType.OFF)) enabled = false;
            else throw new SyntaxException($"Expected ON or OFF after SET PROFILE", _parser.Current.Line, _parser.Current.Column);

            if (_parser.Match(TokenType.SEMICOLON)) { }
            return new SetProfilingStatement { Enabled = enabled };
        }

        private Statement ParseSetWhatIf()
        {
            var enabled = true;
            if (_parser.Match(TokenType.ON)) enabled = true;
            else if (_parser.Match(TokenType.OFF)) enabled = false;
            else throw new SyntaxException($"Expected ON or OFF after SET WHAT_IF", _parser.Current.Line, _parser.Current.Column);

            if (_parser.Match(TokenType.SEMICOLON)) { }
            return new SetWhatIfStatement { Enabled = enabled };
        }

        private Statement ParseSetShowPassword()
        {
            var enabled = true;
            if (_parser.Match(TokenType.ON)) enabled = true;
            else if (_parser.Match(TokenType.OFF)) enabled = false;
            else throw new SyntaxException($"Expected ON or OFF after SET SHOW_PASSWORD", _parser.Current.Line, _parser.Current.Column);

            if (_parser.Match(TokenType.SEMICOLON)) { }
            return new SetShowPasswordStatement(enabled);
        }

        private Statement ParseSetThreshold(ThresholdType type)
        {
            var startToken = _parser.Previous;
            _parser.Consume(TokenType.EQUALS, $"Expected '=' after SET {startToken.Value}");
            var value = _parser.ParseExpression();
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new SetThresholdStatement(type, value) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseSetSecurityOverride()
        {
            var startToken = _parser.Previous; // The token after SET that matched ALLOW_...
            SecurityOverride overrideType;
            string val = startToken.Value.ToUpperInvariant();

            if (val == "ALLOW_FILE_TYPE_ACCESS")
            {
                overrideType = SecurityOverride.FileTypeAccess;
            }
            else if (val.StartsWith("ALLOW_GREATER_THAN_") && val.EndsWith("_FILE"))
            {
                overrideType = SecurityOverride.LargeFileCount;
            }
            else if (val.StartsWith("ALLOW_RECURSIVE_GREATER_THAN_") && val.EndsWith("_LAYERS"))
            {
                overrideType = SecurityOverride.DeepRecursion;
            }
            else
            {
                throw new SyntaxException($"Unknown security override: {startToken.Value}", startToken.Line, startToken.Column);
            }

            var enabled = true;
            if (_parser.Match(TokenType.ON)) enabled = true;
            else if (_parser.Match(TokenType.OFF)) enabled = false;
            else throw new SyntaxException($"Expected ON or OFF after SET {startToken.Value}", _parser.Current.Line, _parser.Current.Column);

            if (_parser.Match(TokenType.SEMICOLON)) { }
            return new SetSecurityOverrideStatement(overrideType, enabled);
        }

        private Statement ParseSetReportMetadata()
        {
            var startToken = _parser.Previous; // REPORT token
            string key;
            if (_parser.Current.Type == TokenType.IDENTIFIER &&
                (_parser.Current.Value.Equals("TITLE", StringComparison.OrdinalIgnoreCase) ||
                 _parser.Current.Value.Equals("DESCRIPTION", StringComparison.OrdinalIgnoreCase)))
            {
                key = _parser.Current.Value.ToUpperInvariant();
                _parser.Advance();
            }
            else
            {
                throw new Common.Exceptions.SyntaxException(
                    "Expected TITLE or DESCRIPTION after SET REPORT",
                    _parser.Current.Line, _parser.Current.Column);
            }

            _parser.Consume(TokenType.EQUALS, $"Expected '=' after SET REPORT {key}");
            var valueToken = _parser.Consume(TokenType.STRING, $"Expected string value after SET REPORT {key} =");
            _parser.Match(TokenType.SEMICOLON);
            return new SetReportMetadataStatement { Key = key, Value = valueToken.Value };
        }

        private Statement ParseRun()
        {
            var startToken = _parser.Previous;
            _parser.Consume(TokenType.SCRIPT, "Expected 'SCRIPT' after 'RUN'");
            var pathExpr = _parser.ParseExpression();
            
            var parameters = new Dictionary<string, Expression>(StringComparer.OrdinalIgnoreCase);
            if (_parser.Match(TokenType.WITH))
            {
                _parser.Consume(TokenType.LPAREN, "Expected '(' after WITH");
                while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                {
                    var nameToken = _parser.Consume(TokenType.VARIABLE, "Expected parameter name starting with '@'");
                    _parser.Consume(TokenType.EQUALS, "Expected '='");
                    var value = _parser.ParseExpression();
                    parameters[nameToken.Value] = value;
                    if (!_parser.Match(TokenType.COMMA)) break;
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' after parameters");
            }
            
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new RunScriptStatement(pathExpr, parameters) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseUse()
        {
            var startToken = _parser.Previous;
            if (_parser.Match(TokenType.DOCKER))
            {
                _parser.Consume(TokenType.LPAREN, "Expected '(' after DOCKER");
                var imageName = _parser.ParseExpression();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after image name");

                string? alias = null;
                if (_parser.Match(TokenType.AS))
                {
                    alias = _parser.ConsumeIdentifier("Expected alias after AS").Value;
                }

                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DockerStatement(imageName, alias) { Line = startToken.Line, Column = startToken.Column };
            }

            if (_parser.Match(TokenType.SETS))
            {
                _parser.Consume(TokenType.BANG, "Expected '!' before set name in USE SETS");
                var name = _parser.ConsumeIdentifier("Expected set name after '!'").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new UseSetsStatement(name) { Line = startToken.Line, Column = startToken.Column };
            }

            if (_parser.Match(TokenType.PASSWORD))
            {
                _parser.Consume(TokenType.EQUALS, "Expected '=' after USE PASSWORD");
                var password = _parser.Consume(TokenType.STRING, "Expected password string").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new UsePasswordStatement(password) { Line = startToken.Line, Column = startToken.Column };
            }

            throw new SyntaxException("Expected DOCKER, SETS, or PASSWORD after USE", _parser.Current.Line, _parser.Current.Column);
        }

        private Statement ParseClear()
        {
            var startToken = _parser.Previous;
            bool isPlural = _parser.Match(TokenType.SESSIONS);
            if (!isPlural) _parser.Consume(TokenType.SESSION, "Expected SESSION or SESSIONS after CLEAR");
            
            ClearSessionMode mode = ClearSessionMode.Current;
            Expression? sessionId = null;

            if (_parser.Current.Type != TokenType.SEMICOLON && _parser.Current.Type != TokenType.EOF)
            {
                if (_parser.Match(TokenType.ALL))
                {
                    mode = ClearSessionMode.All;
                }
                else if (_parser.Current.Type == TokenType.IDENTIFIER && _parser.Current.Value.Equals("STALE", StringComparison.OrdinalIgnoreCase))
                {
                    _parser.Advance();
                    mode = ClearSessionMode.Stale;
                }
                else
                {
                    sessionId = _parser.ParseExpression();
                    mode = ClearSessionMode.Single;
                }
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new ClearSessionStatement(mode, sessionId) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseHelp()
        {
            string? topic = null;
            string? subTopic = null;

            if (_parser.Current.Type == TokenType.IDENTIFIER || IsContextualKeyword(_parser.Current.Type))
            {
                topic = _parser.Advance().Value;
                if (_parser.Current.Type == TokenType.IDENTIFIER || IsContextualKeyword(_parser.Current.Type))
                {
                    subTopic = _parser.Advance().Value;
                }
            }

            _parser.Match(TokenType.SEMICOLON);
            return new HelpStatement(topic, subTopic);
        }

        private Statement ParseShow()
        {
            var startToken = _parser.Previous;
            Statement? stmt = null;

            if (_parser.Match(TokenType.PROFILE) || _parser.Match(TokenType.PROFILING))
            {
                stmt = new ShowProfileStatement();
            }
            else if (_parser.Match(TokenType.JOB))
            {
                if (_parser.Match(TokenType.HISTORY))
                {
                    string? jobName = null;
                    if (_parser.Current.Type == TokenType.IDENTIFIER || _parser.Current.Type == TokenType.STRING)
                    {
                        jobName = _parser.Advance().Value;
                    }
                    stmt = new ShowJobHistoryStatement(jobName);
                }
                else
                {
                    throw new SyntaxException("Expected HISTORY after SHOW JOB", _parser.Current.Line, _parser.Current.Column);
                }
            }
            else if (_parser.Match(TokenType.JOBS))
            {
                stmt = new ShowJobsStatement();
            }
            else if (_parser.Match(TokenType.CONNECTIONS))
            {
                stmt = new ShowConnectionsStatement();
            }
            else if (_parser.Match(TokenType.TABLES))
            {
                string? connName = null;
                if (_parser.Match(TokenType.ON))
                {
                    connName = _parser.ConsumeIdentifier("Expected connection name after ON").Value;
                }
                stmt = new ShowTablesStatement(connName);
            }
            else if (_parser.Match(TokenType.COLUMNS))
            {
                _parser.Consume(TokenType.FOR, "Expected FOR after SHOW COLUMNS");
                var table = ParseTableReference();
                stmt = new ShowColumnsStatement(table);
            }
            else if (_parser.Match(TokenType.VARIABLES) || (_parser.Current.Type == TokenType.LOCAL && _parser.Peek.Type == TokenType.VARIABLES))
            {
                bool localOnly = false;
                if (_parser.Match(TokenType.LOCAL))
                {
                    localOnly = true;
                    _parser.Consume(TokenType.VARIABLES, "Expected VARIABLES after SHOW LOCAL");
                }
                stmt = new ShowVariablesStatement(localOnly);
            }
            else if (_parser.Match(TokenType.TAGS))
            {
                _parser.Consume(TokenType.FOR, "Expected FOR after SHOW TAGS");
                _parser.Consume(TokenType.TABLE, "Expected TABLE after FOR");
                var tableName = _parser.ConsumeIdentifier("Expected table name").Value;
                string? columnName = null;
                if (_parser.Match(TokenType.COLUMN))
                {
                    columnName = _parser.ConsumeIdentifier("Expected column name").Value;
                }
                stmt = new ShowTagsStatement(tableName, columnName);
            }
            else if (_parser.Match(TokenType.TAG))
            {
                _parser.Consume(TokenType.VALUE, "Expected VALUE after SHOW TAG");
                _parser.Consume(TokenType.FOR, "Expected FOR after SHOW TAG VALUE");
                _parser.Consume(TokenType.TABLE, "Expected TABLE after FOR");
                var tableName = _parser.ConsumeIdentifier("Expected table name").Value;
                string? columnName = null;
                if (_parser.Match(TokenType.COLUMN))
                {
                    columnName = _parser.ConsumeIdentifier("Expected column name").Value;
                }
                _parser.Consume(TokenType.WITH, "Expected WITH after table/column");
                _parser.Consume(TokenType.TAG, "Expected TAG after WITH");
                var tagName = _parser.ConsumeIdentifier("Expected tag name").Value;
                stmt = new ShowTagValueStatement(tableName, tagName, columnName);
            }
            else if (_parser.Match(TokenType.LINEAGE))
            {
                _parser.Match(TokenType.FOR);
                var targetTable = _parser.ParseTableReference();
                stmt = new LineageStatement(targetTable);
            }
            else if (_parser.Match(TokenType.VERSION))
            {
                stmt = new ShowVersionStatement();
            }
            else if (_parser.Match(TokenType.SAFE))
            {
                _parser.Consume(TokenType.ZONES, "Expected ZONES after SHOW SAFE");
                stmt = new ShowSafeZonesStatement();
            }
            else if (_parser.Match(TokenType.SESSIONS))
            {
                stmt = new ShowSessionsStatement();
            }

            if (stmt == null)
            {
                throw new SyntaxException($"Expected PROFILE, JOBS, JOB HISTORY, CONNECTIONS, TABLES, COLUMNS, VARIABLES, TAGS, VERSION or LINEAGE after SHOW", _parser.Current.Line, _parser.Current.Column);
            }

            if (_parser.Match(TokenType.INTO))
            {
                var tempTable = _parser.ConsumeIdentifier("Expected temporary table name after INTO").Value;
                if (!tempTable.StartsWith("#")) throw new SyntaxException("SHOW ... INTO target must be a temporary table starting with '#'", _parser.Current.Line, _parser.Current.Column);
                
                stmt = stmt switch
                {
                    ShowProfileStatement sps => sps with { IntoTable = tempTable },
                    ShowJobHistoryStatement sjh => sjh with { IntoTable = tempTable },
                    ShowJobsStatement sjs => sjs with { IntoTable = tempTable },
                    ShowConnectionsStatement scs => scs with { IntoTable = tempTable },
                    ShowVersionStatement svs => svs with { IntoTable = tempTable },
                    ShowTablesStatement sts => sts with { IntoTable = tempTable },
                    ShowColumnsStatement scols => scols with { IntoTable = tempTable },
                    ShowTagsStatement stag => stag with { IntoTable = tempTable },
                    ShowTagValueStatement stv => stv with { IntoTable = tempTable },
                    ShowVariablesStatement svars => svars with { IntoTable = tempTable },
                    ShowSafeZonesStatement ssz => ssz with { IntoTable = tempTable },
                    ShowSessionsStatement sess => sess with { IntoTable = tempTable },
                    _ => stmt
                };
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            
            stmt = stmt with { Line = startToken.Line, Column = startToken.Column };
            return stmt;
        }

        private Statement ParseExplain()
        {
            var startToken = _parser.Previous;
            bool isAnalyze = _parser.Match(TokenType.ANALYZE);
            var stmt = _parser.ParseStatement();
            
            TableReference? intoTable = null;
            if (_parser.Match(TokenType.INTO))
            {
                var tempTable = _parser.ConsumeIdentifier("Expected temporary table name after INTO").Value;
                if (!tempTable.StartsWith("#")) throw new SyntaxException("EXPLAIN ... INTO target must be a temporary table starting with '#'", _parser.Current.Line, _parser.Current.Column);
                intoTable = new TableReference(tempTable);
            }
            
            return new ExplainStatement(stmt, isAnalyze, intoTable) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParsePrint()
        {
            bool hasParen = _parser.Match(TokenType.LPAREN);
            var message = _parser.ParseExpression();
            Expression? showTimestamp = null;
            Expression? format = null;

            if (_parser.Match(TokenType.COMMA))
            {
                showTimestamp = _parser.ParseExpression();
                if (_parser.Match(TokenType.COMMA))
                {
                    format = _parser.ParseExpression();
                }
            }

            if (hasParen)
            {
                _parser.Consume(TokenType.RPAREN, "Expected ')' after PRINT arguments");
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new PrintStatement(message, showTimestamp, format);
        }

        private Statement ParseBeginTransaction()
        {
            string? name = null;
            if (_parser.Current.Type == TokenType.IDENTIFIER) name = _parser.Advance().Value;
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new BeginTransactionStatement(name);
        }

        private Statement ParseCommitTransaction()
        {
            if (_parser.Match(TokenType.TRANSACTION) || _parser.Match(TokenType.TRAN)) { }
            string? name = null;
            if (_parser.Current.Type == TokenType.IDENTIFIER) name = _parser.Advance().Value;
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new CommitTransactionStatement(name);
        }

        private Statement ParseRollbackTransaction()
        {
            if (_parser.Match(TokenType.TRANSACTION) || _parser.Match(TokenType.TRAN)) { }
            string? name = null;
            if (_parser.Current.Type == TokenType.IDENTIFIER) name = _parser.Advance().Value;
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            return new RollbackTransactionStatement(name);
        }

        private Statement ParseRequireVersion()
        {
            var startToken = _parser.Previous; // REQUIRE
            
            // VERSION keyword is optional: REQUIRE VERSION >= '0.5.0' vs REQUIRE >= '0.5.0'
            _parser.Match(TokenType.VERSION);

            string op = ">=";
            if (_parser.Match(TokenType.GREATER_EQUALS)) op = ">=";
            else if (_parser.Match(TokenType.GREATER_THAN)) op = ">";
            else if (_parser.Match(TokenType.EQUALS)) op = "=";
            else throw new SyntaxException("Expected operator (>=, >, or =) after REQUIRE VERSION", _parser.Current.Line, _parser.Current.Column);

            var versionToken = _parser.Consume(TokenType.STRING, "Expected version string literal after REQUIRE operator");
            var version = versionToken.Value.Trim('\'', '\"');

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new RequireVersionStatement(op, version)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }
    }
}
