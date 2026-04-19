using System;
using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser
{
    public partial class StatementParser
    {
        private Statement ParseCreate()
        {
            var startToken = _parser.Previous; 
            bool orAlter = false;
            if (_parser.Match(TokenType.OR))
            {
                _parser.Consume(TokenType.ALTER, "Expected ALTER after CREATE OR");
                orAlter = true;
            }
            var mode = orAlter ? ObjectCreationMode.CreateOrAlter : ObjectCreationMode.Create;

            if (_parser.Match(TokenType.CONNECTION)) return ParseCreateConnection(startToken, mode);
            if (_parser.Match(TokenType.TABLE)) 
            {
                if (orAlter) throw new SyntaxException("CREATE OR ALTER is not supported for TABLE.", _parser.Current.Line, _parser.Current.Column);
                return ParseCreateTable(startToken);
            }
            if (_parser.Match(TokenType.PROCEDURE)) return ParseCreateProcedure(startToken, mode);
            if (_parser.Match(TokenType.FUNCTION)) return ParseCreateFunction(startToken, mode);
            if (_parser.Match(TokenType.JOB))
            {
                if (orAlter) throw new SyntaxException("CREATE OR ALTER is not supported for JOB. Use DROP and CREATE.", _parser.Current.Line, _parser.Current.Column);
                return ParseCreateJob(startToken);
            }
            
            if (_parser.Match(TokenType.DIRECTORY))
            {
                var path = _parser.ParseExpression();
                Expression? overwrite = null;
                if (_parser.Match(TokenType.WITH))
                {
                    overwrite = ParseWithOverwrite();
                }
                _parser.Match(TokenType.SEMICOLON);
                return new DirectoryOperationStatement(DirectoryOpType.Create, path, null, overwrite) { Line = startToken.Line, Column = startToken.Column };
            }
            
            if (_parser.Current.Type == TokenType.UNIQUE || _parser.Current.Type == TokenType.INDEX)
            {
                if (orAlter) throw new SyntaxException("CREATE OR ALTER is not supported for INDEX.", _parser.Current.Line, _parser.Current.Column);
                bool isUnique = _parser.Match(TokenType.UNIQUE);
                return ParseCreateIndex(startToken, isUnique);
            }

            if (_parser.Match(TokenType.SSH_KEY_PAIR))
            {
                if (orAlter) throw new SyntaxException("CREATE OR ALTER is not supported for SSH_KEY_PAIR.", _parser.Current.Line, _parser.Current.Column);
                return ParseCreateSshKeyPair(startToken);
            }

            if (_parser.Match(TokenType.SETS))
            {
                if (orAlter) throw new SyntaxException("CREATE OR ALTER is not supported for SETS.", _parser.Current.Line, _parser.Current.Column);
                return ParseCreateSets(startToken);
            }

            // ── Report-SQL (Phase 9A) ──────────────────────────────────────
            if (_parser.Match(TokenType.VISUAL))
                return ParseCreateVisual(startToken, mode);
            if (_parser.Match(TokenType.PAGE))
                return ParseCreatePage(startToken, mode);
            if (_parser.Match(TokenType.DATASET))
                return ParseCreateDataset(startToken, mode);

            // ── Report-SQL (Phase 9.3) ─────────────────────────────────────
            if (_parser.Match(TokenType.CONTAINER))
                return ParseCreateContainer(startToken, mode);
            if (_parser.Match(TokenType.NAVIGATION))
                return ParseCreateNavigation(startToken, mode);
            if (_parser.Match(TokenType.STYLE))
                return ParseCreateStyle(startToken, mode);

            if (_parser.Match(TokenType.BUTTON))
                return ParseCreateButton(startToken, mode);
            if (_parser.Match(TokenType.TEMPLATE))
                return ParseCreateTemplate(startToken, mode);

            throw new SyntaxException("Expected CONNECTION, TABLE, PROCEDURE, FUNCTION, INDEX, SETS, SSH_KEY_PAIR, VISUAL, PAGE, DATASET, CONTAINER, NAVIGATION, STYLE, BUTTON, or TEMPLATE after CREATE", _parser.Current.Line, _parser.Current.Column);
        }

        private Statement ParseCreateSshKeyPair(Token startToken)
        {
            Expression? path = null;
            Expression? bits = null;
            Expression? algorithm = null;
            Expression? passphrase = null;
            Expression? comment = null;

            if (_parser.Match(TokenType.LPAREN))
            {
                path = _parser.ParseExpression();
                if (_parser.Match(TokenType.COMMA))
                {
                    bits = _parser.ParseExpression();
                    if (_parser.Match(TokenType.COMMA))
                    {
                        algorithm = _parser.ParseExpression();
                        if (_parser.Match(TokenType.COMMA))
                        {
                            passphrase = _parser.ParseExpression();
                            if (_parser.Match(TokenType.COMMA))
                            {
                                comment = _parser.ParseExpression();
                            }
                        }
                    }
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' after arguments");
            }
            else
            {
                path = _parser.ParseExpression();
                if (_parser.Match(TokenType.WITH))
                {
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after WITH");
                    while (!_parser.Match(TokenType.RPAREN))
                    {
                        var keyToken = _parser.Advance();
                        string key = keyToken.Value.ToUpperInvariant();
                        _parser.Consume(TokenType.EQUALS, "Expected '='");
                        var val = _parser.ParseExpression();

                        switch (key)
                        {
                            case "BITS": bits = val; break;
                            case "ALGORITHM": algorithm = val; break;
                            case "PASSPHRASE": passphrase = val; break;
                            case "COMMENT": comment = val; break;
                            default: throw new SyntaxException($"Unknown SSH_KEY_PAIR option: {key}", keyToken.Line, keyToken.Column);
                        }

                        if (!_parser.Match(TokenType.COMMA))
                        {
                            _parser.Consume(TokenType.RPAREN, "Expected ')' or ','");
                            break;
                        }
                    }
                }
            }

            _parser.Match(TokenType.SEMICOLON);

            return new CreateSshKeyPairStatement(path!, bits, algorithm, passphrase, comment)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        private Statement ParseAlter()
        {
            var startToken = _parser.Previous;

            if (_parser.Match(TokenType.CONNECTION)) return ParseAlterConnection(startToken);
            if (_parser.Match(TokenType.PROCEDURE))  return ParseCreateProcedure(startToken, ObjectCreationMode.Alter);
            if (_parser.Match(TokenType.FUNCTION))   return ParseCreateFunction(startToken, ObjectCreationMode.Alter);
            if (_parser.Match(TokenType.TABLE))       return ParseAlterTable(startToken);
            
            // ── Report-SQL ──────────────────────────────────────────────────
            if (_parser.Match(TokenType.VISUAL))      return ParseAlterReportObject(ReportObjectType.Visual);
            if (_parser.Match(TokenType.PAGE))        return ParseAlterReportObject(ReportObjectType.Page);
            if (_parser.Match(TokenType.CONTAINER))   return ParseAlterReportObject(ReportObjectType.Container);
            if (_parser.Match(TokenType.STYLE))       return ParseAlterReportObject(ReportObjectType.Style);
            if (_parser.Match(TokenType.NAVIGATION))  return ParseAlterReportObject(ReportObjectType.Navigation);
            if (_parser.Match(TokenType.DATASET))     return ParseAlterReportObject(ReportObjectType.Dataset);
            if (_parser.Match(TokenType.TEMPLATE))    return ParseAlterReportObject(ReportObjectType.Template);

            throw new SyntaxException("Expected CONNECTION, PROCEDURE, FUNCTION, TABLE, or REPORT object after ALTER", _parser.Current.Line, _parser.Current.Column);
        }

        /// <summary>
        /// Parses ALTER CONNECTION &lt;name&gt; [ON &lt;type&gt;(&lt;target&gt;)] [WITH(&lt;options&gt;)];
        /// Preserves existing options — only the keys supplied in WITH are overwritten.
        /// </summary>
        private Statement ParseAlterConnection(Token startToken)
        {
            var name = _parser.ConsumeIdentifier("Expected connection name after ALTER CONNECTION").Value;

            string? connectionType = null;
            Expression? target = null;

            if (_parser.Match(TokenType.ON))
            {
                var typeToken = _parser.Advance();
                connectionType = typeToken.Value;

                if (typeToken.Type == TokenType.FILE)
                    throw new SyntaxException("Connection type 'FILE' is deprecated. Please use 'FLATFILE' instead.", typeToken.Line, typeToken.Column);

                bool hasParen = _parser.Match(TokenType.LPAREN);
                if (hasParen && _parser.Current.Type == TokenType.RPAREN)
                    target = new LiteralExpression("", TokenType.STRING);
                else
                    target = _parser.ParseExpression();

                if (hasParen) _parser.Consume(TokenType.RPAREN, "Expected ')' after target string");
            }

            Dictionary<string, string>? options = null;
            if (_parser.Match(TokenType.WITH))
            {
                options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _parser.Consume(TokenType.LPAREN, "Expected '(' after WITH");
                while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                {
                    string key = _parser.Advance().Value;
                    _parser.Consume(TokenType.EQUALS, "Expected '=' after option key");
                    string val = _parser.Advance().Value;
                    options[key] = val;
                    if (!_parser.Match(TokenType.COMMA)) break;
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' at end of WITH options");
            }

            _parser.Consume(TokenType.SEMICOLON, "Expected ';' at end of ALTER CONNECTION");

            return new AlterConnectionStatement(name, connectionType, target, options)
            {
                Line   = startToken.Line,
                Column = startToken.Column
            };
        }

        private Statement ParseCreateProcedure(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
        {
            var name = _parser.ConsumeIdentifier("Expected procedure name").Value;
            var parameters = ParseParameterDefinitions();
            
            _parser.Consume(TokenType.AS, "Expected 'AS' after procedure definition");
            var body = _parser.ParseStatement();
            
            if (_parser.Match(TokenType.SEMICOLON)) { /* skipped */ }

            return new CreateProcedureStatement(name, parameters, body, mode) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseCreateFunction(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
        {
            var name = _parser.ConsumeIdentifier("Expected function name").Value;
            var parameters = ParseParameterDefinitions();
            
            _parser.Consume(TokenType.RETURNS, "Expected 'RETURNS' after function definition");
            var returnType = _parser.ParseType();
            
            _parser.Consume(TokenType.AS, "Expected 'AS' after function return type");
            var body = _parser.ParseStatement();
            
            if (_parser.Match(TokenType.SEMICOLON)) { /* skipped */ }

            return new CreateFunctionStatement(name, parameters, returnType, body, mode) { Line = startToken.Line, Column = startToken.Column };
        }

        private Statement ParseAlterTable(Token startToken)
        {
            var targetTable = ParseTableReference();
            AlterTableActionType action;
            ColumnDefinition? newColumn = null;
            string? columnToDelete = null;
            string? oldColumnName = null;
            string? newColumnName = null;

            if (_parser.Match(TokenType.ADD))
            {
                action = AlterTableActionType.ADD;
                var colName = _parser.ConsumeIdentifier("Expected column name").Value;
                string dataType = "NVARCHAR(MAX)";
                if (_parser.IsIdentifier(_parser.Current))
                {
                    dataType = _parser.Advance().Value;
                    if (_parser.Match(TokenType.LPAREN))
                    {
                        dataType += "(" + _parser.Consume(TokenType.NUMBER, "Expected length").Value + ")";
                        _parser.Consume(TokenType.RPAREN, "Expected ')'");
                    }
                }
                
                Expression? defaultExpr = null;
                if (_parser.Match(TokenType.DEFAULT))
                {
                    defaultExpr = _parser.ParseExpression();
                }
                
                newColumn = new ColumnDefinition(colName, dataType, false, defaultExpr);
            }
            else if (_parser.Match(TokenType.DROP))
            {
                _parser.Match(TokenType.COLUMN); // Optional COLUMN keyword
                action = AlterTableActionType.DROP_COLUMN;
                columnToDelete = _parser.ConsumeIdentifier("Expected column name to drop").Value;
            }
            else if (_parser.Match(TokenType.RENAME))
            {
                _parser.Match(TokenType.COLUMN); // Optional COLUMN keyword
                action = AlterTableActionType.RENAME_COLUMN;
                oldColumnName = _parser.ConsumeIdentifier("Expected column name to rename").Value;
                _parser.Consume(TokenType.TO, "Expected 'TO' after old column name");
                newColumnName = _parser.ConsumeIdentifier("Expected new column name").Value;
            }
            else
            {
                throw new SyntaxException("Expected ADD, DROP, or RENAME after ALTER TABLE", _parser.Current.Line, _parser.Current.Column);
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new AlterTableStatement(targetTable, action, newColumn, columnToDelete, oldColumnName, newColumnName)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        private List<ParameterDefinition> ParseParameterDefinitions()
        {
            var parameters = new List<ParameterDefinition>();
            if (_parser.Match(TokenType.LPAREN))
            {
                if (_parser.Current.Type != TokenType.RPAREN)
                {
                    do
                    {
                        var pName = _parser.Consume(TokenType.VARIABLE, "Expected parameter name starting with '@'").Value;
                        var pType = _parser.ParseType();
                        parameters.Add(new ParameterDefinition(pName, pType));
                    } while (_parser.Match(TokenType.COMMA));
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' after parameter list");
            }
            return parameters;
        }

        private Statement ParseCreateConnection(Token startToken, ObjectCreationMode mode)
        {
            var name = _parser.ConsumeIdentifier("Expected connection name").Value;

            string? connectionType = null;
            Expression? target = null;

            if (_parser.Match(TokenType.ON) || (mode == ObjectCreationMode.Create)) 
            {
                if (_parser.Match(TokenType.TYPE))
                {
                    connectionType = _parser.Advance().Value;
                    _parser.Consume(TokenType.TARGET, "Expected TARGET after connection type");
                    target = _parser.ParseExpression();
                }
                else
                {
                    var typeToken = _parser.Advance();
                    connectionType = typeToken.Value;
                    
                    if (typeToken.Type == TokenType.FILE)
                    {
                        throw new SyntaxException("Connection type 'FILE' is deprecated. Please use 'FLATFILE' instead.", typeToken.Line, typeToken.Column);
                    }

                    bool hasParen = _parser.Match(TokenType.LPAREN);
                    if (hasParen && _parser.Current.Type == TokenType.RPAREN)
                    {
                        target = new LiteralExpression("", TokenType.STRING);
                    }
                    else
                    {
                        target = _parser.ParseExpression();
                    }
                    if (hasParen) _parser.Consume(TokenType.RPAREN, "Expected ')' after target string");
                }
            }
            else
            {
                // In ALTER/CREATE OR ALTER, if no ON is provided, it's valid if WITH follows.
            }

            Dictionary<string, string>? options = null;
            if (_parser.Match(TokenType.WITH))
            {
                options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _parser.Consume(TokenType.LPAREN, "Expected '(' after WITH clause");
                while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
                {
                    string key = _parser.Advance().Value;
                    _parser.Consume(TokenType.EQUALS, "Expected '=' after option key");
                    string val = _parser.Advance().Value;
                    options[key] = val;
                    if (!_parser.Match(TokenType.COMMA))
                    {
                        break;
                    }
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' at end of WITH options");
            }

            _parser.Consume(TokenType.SEMICOLON, "Expected ';' at the end of CREATE CONNECTION");

            return new CreateConnectionStatement(name, connectionType, target, options, mode)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        private Statement ParseCreateTable(Token startToken)
        {
            bool ifNotExists = false;
            if (_parser.Match(TokenType.IF))
            {
                _parser.Consume(TokenType.NOT, "Expected 'NOT' after 'IF'");
                _parser.Consume(TokenType.EXISTS, "Expected 'EXISTS' after 'NOT'");
                ifNotExists = true;
            }

            var targetTable = ParseTableReference(false);
            _parser.Consume(TokenType.LPAREN, "Expected '(' after table name");

            var columns = new List<ColumnDefinition>();
            var tableConstraints = new List<TableConstraint>();

            while (_parser.Current.Type != TokenType.RPAREN && _parser.Current.Type != TokenType.EOF)
            {
                // Check if it's a table-level constraint
                string? constraintName = null;
                if (_parser.Match(TokenType.CONSTRAINT))
                {
                    constraintName = _parser.ConsumeIdentifier("Expected constraint name").Value;
                }

                if (_parser.Current.Type == TokenType.PRIMARY || 
                    _parser.Current.Type == TokenType.UNIQUE || 
                    _parser.Current.Type == TokenType.CHECK || 
                    _parser.Current.Type == TokenType.FOREIGN)
                {
                    var tc = ParseTableConstraint(constraintName);
                    tableConstraints.Add(tc);
                }
                else
                {
                    // It's a column definition
                    var colName = _parser.ConsumeIdentifier("Expected column name or constraint").Value;
                    string dataType = "NVARCHAR(MAX)"; // Default type

                    if (_parser.IsIdentifier(_parser.Current))
                    {
                        dataType = _parser.Advance().Value;
                        if (_parser.Match(TokenType.LPAREN))
                        {
                            dataType += "(";
                            dataType += _parser.Consume(TokenType.NUMBER, "Expected length").Value;
                            if (_parser.Match(TokenType.COMMA))
                            {
                                dataType += ",";
                                dataType += _parser.Consume(TokenType.NUMBER, "Expected scale").Value;
                            }
                            dataType += ")";
                            _parser.Consume(TokenType.RPAREN, "Expected ')'");
                        }
                    }

                    Dictionary<string, string>? metadata = null;
                    while (_parser.Match(TokenType.COLUMN_TAG))
                    {
                        if (metadata == null) metadata = new(StringComparer.OrdinalIgnoreCase);
                        _parser.ParseMetadataTags(_parser.Previous.Value, metadata);
                    }
                    var colDef = new ColumnDefinition(colName, dataType, false, null, metadata);

                    // Parse column constraints
                    while (true)
                    {
                        if (_parser.Match(TokenType.IDENTITY))
                        {
                            // Identity handled here
                        }
                        else if (_parser.Match(TokenType.PRIMARY))
                        {
                            _parser.Consume(TokenType.KEY, "Expected KEY after PRIMARY");
                            colDef.IsPrimaryKey = true;
                        }
                        else if (_parser.Match(TokenType.UNIQUE))
                        {
                            colDef.IsUnique = true;
                        }
                        else if (_parser.Match(TokenType.NOT))
                        {
                            _parser.Consume(TokenType.NULL, "Expected NULL after NOT");
                            colDef.IsNullable = false;
                        }
                        else if (_parser.Match(TokenType.NULL))
                        {
                            colDef.IsNullable = true;
                        }
                        else if (_parser.Match(TokenType.CHECK))
                        {
                            _parser.Consume(TokenType.LPAREN, "Expected '(' after CHECK");
                            colDef.CheckConstraint = _parser.ParseExpression();
                            _parser.Consume(TokenType.RPAREN, "Expected ')' after check expression");
                        }
                        else if (_parser.Match(TokenType.DEFAULT))
                        {
                            colDef.DefaultExpression = _parser.ParseExpression();
                        }
                        else if (_parser.Match(TokenType.REFERENCES))
                        {
                            colDef.ForeignKey = ParseForeignKeyReference();
                        }
                        else
                        {
                            break;
                        }
                    }

                    // Parse any trailing tags
                    while (_parser.Match(TokenType.COLUMN_TAG))
                    {
                        _parser.ParseMetadataTags(_parser.Previous.Value, colDef.Metadata);
                    }

                    columns.Add(colDef);
                }

                if (!_parser.Match(TokenType.COMMA))
                {
                    break;
                }
            }

            _parser.Consume(TokenType.RPAREN, "Expected ')' at end of CREATE TABLE");
            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            var stmt = new CreateTableStatement(targetTable, ifNotExists, columns)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
            stmt.TableConstraints.AddRange(tableConstraints);
            return stmt;
        }

        private TableConstraint ParseTableConstraint(string? constraintName)
        {
            if (_parser.Match(TokenType.PRIMARY))
            {
                _parser.Consume(TokenType.KEY, "Expected KEY after PRIMARY");
                _parser.Consume(TokenType.LPAREN, "Expected '(' after PRIMARY KEY");
                var columns = ParseIdentifierList();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after column list");
                return new TablePrimaryKeyConstraint(columns) { ConstraintName = constraintName };
            }
            if (_parser.Match(TokenType.UNIQUE))
            {
                _parser.Consume(TokenType.LPAREN, "Expected '(' after UNIQUE");
                var columns = ParseIdentifierList();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after column list");
                return new TableUniqueConstraint(columns) { ConstraintName = constraintName };
            }
            if (_parser.Match(TokenType.CHECK))
            {
                _parser.Consume(TokenType.LPAREN, "Expected '(' after CHECK");
                var expr = _parser.ParseExpression();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after check expression");
                return new TableCheckConstraint(expr) { ConstraintName = constraintName };
            }
            if (_parser.Match(TokenType.FOREIGN))
            {
                _parser.Consume(TokenType.KEY, "Expected KEY after FOREIGN");
                _parser.Consume(TokenType.LPAREN, "Expected '(' after FOREIGN KEY");
                var columns = ParseIdentifierList();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after column list");
                
                _parser.Consume(TokenType.REFERENCES, "Expected REFERENCES keyword");
                var destTable = ParseTableReference(false);
                _parser.Consume(TokenType.LPAREN, "Expected '(' after reference table name");
                var refCols = ParseIdentifierList();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after reference table columns");
                var reference = new ForeignKeyReference(destTable, refCols);
                
                return new TableForeignKeyConstraint(columns, reference) { ConstraintName = constraintName };
            }
            throw new SyntaxException($"Unexpected token {_parser.Current.Type} in table constraint", _parser.Current.Line, _parser.Current.Column);
        }

        public ForeignKeyReference ParseForeignKeyReference()
        {
            var table = ParseTableReference(false);
            _parser.Consume(TokenType.LPAREN, "Expected '(' after table name in REFERENCES");
            var columns = ParseIdentifierList();
            _parser.Consume(TokenType.RPAREN, "Expected ')' after column list in REFERENCES");
            return new ForeignKeyReference(table, columns);
        }

        private List<string> ParseIdentifierList()
        {
            var list = new List<string>();
            while (true)
            {
                list.Add(_parser.ConsumeIdentifier("Expected identifier").Value);
                if (!_parser.Match(TokenType.COMMA)) break;
            }
            return list;
        }

        private Statement ParseCreateIndex(Token startToken, bool isUnique)
        {
            _parser.Consume(TokenType.INDEX, "Expected INDEX");

            var indexName = _parser.ConsumeIdentifier("Expected index name").Value;
            _parser.Consume(TokenType.ON, "Expected 'ON' after index name");
            var targetTable = ParseTableReference(false);

            _parser.Consume(TokenType.LPAREN, "Expected '(' before column list");
            var columns = new List<string>();
            columns.Add(_parser.ConsumeIdentifier("Expected column name").Value);
            while (_parser.Match(TokenType.COMMA))
            {
                columns.Add(_parser.ConsumeIdentifier("Expected column name").Value);
            }
            _parser.Consume(TokenType.RPAREN, "Expected ')' after column list");

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new CreateIndexStatement(indexName, targetTable, columns, isUnique)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        private Statement ParseDrop()
        {
            var startToken = _parser.Previous;
            
            bool ifExists = false;
            if (_parser.Match(TokenType.TABLE))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var target = ParseTableReference(false);
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropTableStatement(target, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.CONNECTION))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected connection name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropConnectionStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.PROCEDURE))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected procedure name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropProcedureStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.FUNCTION))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected function name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropFunctionStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.INDEX))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected index name").Value;
                TableReference? target = null;
                if (_parser.Match(TokenType.ON)) target = ParseTableReference();
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropIndexStatement(name, target, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.SETS))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                _parser.Consume(TokenType.BANG, "Expected '!' before set name in DROP SETS");
                var name = _parser.ConsumeIdentifier("Expected set name after '!'").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropSetsStatement(name, ifExists) { Line = startToken.Line, Column = startToken.Column };
            }
            // ── Report-SQL ──────────────────────────────────────────────────
            else if (_parser.Match(TokenType.VISUAL) || _parser.Match(TokenType.IDENTIFIER) && _parser.Current.Value.Equals("CHART", StringComparison.OrdinalIgnoreCase))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected visual name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Visual, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.PAGE))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected page name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Page, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.CONTAINER))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected container name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Container, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.STYLE))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected style name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Style, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.NAVIGATION))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected navigation name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Navigation, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.DATASET))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected dataset name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Dataset, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }
            else if (_parser.Match(TokenType.TEMPLATE))
            {
                if (_parser.Match(TokenType.IF)) { _parser.Consume(TokenType.EXISTS, "Expected EXISTS"); ifExists = true; }
                var name = _parser.ConsumeIdentifier("Expected template name").Value;
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new DropReportObjectStatement { ObjectType = ReportObjectType.Template, Name = name, IfExists = ifExists, Line = startToken.Line, Column = startToken.Column };
            }

            throw new SyntaxException("Expected TABLE, CONNECTION, PROCEDURE, FUNCTION, INDEX, SETS, or REPORT object after DROP", _parser.Current.Line, _parser.Current.Column);
        }

        private Statement ParseTruncate()
        {
            var startToken = _parser.Previous;
            _parser.Consume(TokenType.TABLE, "Expected 'TABLE' after 'TRUNCATE'");
            var targetTable = ParseTableReference(false);

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new TruncateTableStatement(targetTable)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        private Statement ParseDelete()
        {
            var startToken = _parser.Previous;
            
            if (_parser.Match(TokenType.FILE))
            {
                var source = _parser.ParseExpression();
                _parser.Match(TokenType.SEMICOLON);
                return new FileOperationStatement(FileOpType.Delete, source) { Line = startToken.Line, Column = startToken.Column };
            }
            if (_parser.Match(TokenType.DIRECTORY))
            {
                var path = _parser.ParseExpression();
                _parser.Match(TokenType.SEMICOLON);
                return new DirectoryOperationStatement(DirectoryOpType.Delete, path) { Line = startToken.Line, Column = startToken.Column };
            }
            if (_parser.Match(TokenType.DIRECTORY_CONTENTS))
            {
                var path = _parser.ParseExpression();
                Expression? recursive = null;
                if (_parser.Match(TokenType.WITH))
                {
                    recursive = ParseWithRecursive();
                }
                _parser.Match(TokenType.SEMICOLON);
                return new DirectoryOperationStatement(DirectoryOpType.DeleteContents, path, null, null, recursive) { Line = startToken.Line, Column = startToken.Column };
            }

            _parser.Match(TokenType.FROM);
            var targetTable = ParseTableReference(false);

            OutputClause? output = null;
            if (_parser.Match(TokenType.OUTPUT))
            {
                output = _parser.ParseOutputClause();
            }
            
            Expression? whereClause = null;
            if (_parser.Match(TokenType.WHERE))
            {
                whereClause = _parser.ParseExpression();
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new DeleteStatement(targetTable, whereClause)
            {
                Line = startToken.Line,
                Column = startToken.Column,
                Output = output
            };
        }

        private Statement ParseInsert()
        {
            var startToken = _parser.Previous;
            _parser.Match(TokenType.INTO);
            var targetTable = ParseTableReference(false);

            List<string>? columns = null;
            if (_parser.Match(TokenType.LPAREN))
            {
                columns = ParseIdentifierList();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after column list");
            }

            OutputClause? output = null;
            if (_parser.Match(TokenType.OUTPUT))
            {
                output = _parser.ParseOutputClause();
            }

            if (_parser.Match(TokenType.VALUES))
            {
                var rows = new List<List<Expression>>();
                do
                {
                    do
                    {
                        _parser.Consume(TokenType.LPAREN, "Expected '(' before values list");
                        var values = new List<Expression>();
                        do
                        {
                            values.Add(_parser.ParseExpression());
                        } while (_parser.Match(TokenType.COMMA));
                        _parser.Consume(TokenType.RPAREN, "Expected ')' after values list");
                        while (_parser.Match(TokenType.COLUMN_TAG)); 
                        rows.Add(values);
                    } while (_parser.Match(TokenType.COMMA));
                } while (_parser.Match(TokenType.VALUES)); // Support multiple VALUES keywords in one statement

                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new InsertStatement(targetTable, columns, rows) { Line = startToken.Line, Column = startToken.Column, Output = output };
            }
            else
            {
                if (_parser.Current.Type == TokenType.EXEC || _parser.Current.Type == TokenType.EXECUTE)
                {
                    _parser.Advance();
                    var exec = ParseExecute();
                    if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                    return new InsertStatement(targetTable, columns, exec) { Line = startToken.Line, Column = startToken.Column, Output = output };
                }

                var query = _parser.ParseQuery();
                if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
                return new InsertStatement(targetTable, columns, query) { Line = startToken.Line, Column = startToken.Column, Output = output };
            }
        }

        private Statement ParseUpdate()
        {
            var startToken = _parser.Previous;
            var targetTable = ParseTableReference(false);

            _parser.Consume(TokenType.SET, "Expected 'SET' in UPDATE statement");

            var assignments = new List<Assignment>();
            do
            {
                var col = _parser.ConsumeIdentifier("Expected column name").Value;
                _parser.Consume(TokenType.EQUALS, "Expected '=' in assignment");
                var expr = _parser.ParseExpression();
                assignments.Add(new Assignment(col, expr));
            } while (_parser.Match(TokenType.COMMA));

            OutputClause? output = null;
            if (_parser.Match(TokenType.OUTPUT))
            {
                output = _parser.ParseOutputClause();
            }

            TableReference? fromTable = null;
            if (_parser.Match(TokenType.FROM))
            {
                fromTable = ParseTableReference(false);
            }

            var joins = _parser.ParseJoins();

            Expression? whereClause = null;
            if (_parser.Match(TokenType.WHERE))
            {
                whereClause = _parser.ParseExpression();
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new UpdateStatement(targetTable, assignments, whereClause)
            {
                Line = startToken.Line,
                Column = startToken.Column,
                Output = output,
                FromTable = fromTable,
                Joins = joins
            };
        }

        private Statement ParseMerge()
        {
            var startToken = _parser.Previous;
            _parser.Match(TokenType.INTO);
            var targetTable = ParseTableReference(false);
            if (_parser.Match(TokenType.AS)) _parser.Advance();
            string? targetAlias = null;
            if (_parser.Current.Type == TokenType.IDENTIFIER) targetAlias = _parser.Advance().Value;

            _parser.Consume(TokenType.USING, "Expected USING in MERGE");
            var sourceTable = ParseTableReference(false);
            if (_parser.Match(TokenType.AS)) _parser.Advance();
            string? sourceAlias = null;
            if (_parser.Current.Type == TokenType.IDENTIFIER) sourceAlias = _parser.Advance().Value;

            _parser.Consume(TokenType.ON, "Expected ON in MERGE");
            var onClause = _parser.ParseExpression();

            var whenMatched = new List<MergeMatchedClause>();
            var whenNotMatched = new List<MergeNotMatchedClause>();

            while (_parser.Match(TokenType.WHEN))
            {
                if (_parser.Match(TokenType.MATCHED))
                {
                    Expression? andExpr = null;
                    if (_parser.Match(TokenType.AND)) andExpr = _parser.ParseExpression();
                    
                    _parser.Consume(TokenType.THEN, "Expected THEN");
                    if (_parser.Match(TokenType.UPDATE))
                    {
                        _parser.Consume(TokenType.SET, "Expected SET after UPDATE");
                        var assignments = new List<Assignment>();
                        do
                        {
                            var col = _parser.ConsumeIdentifier("Expected column name").Value;
                            _parser.Consume(TokenType.EQUALS, "Expected '='");
                            var expr = _parser.ParseExpression();
                            assignments.Add(new Assignment(col, expr));
                        } while (_parser.Match(TokenType.COMMA));
                        whenMatched.Add(new MergeUpdateClause(andExpr, assignments));
                    }
                    else if (_parser.Match(TokenType.DELETE))
                    {
                        whenMatched.Add(new MergeDeleteClause(andExpr));
                    }
                }
                else
                {
                    _parser.Consume(TokenType.NOT, "Expected NOT MATCHED");
                    _parser.Consume(TokenType.MATCHED, "Expected MATCHED");
                    
                    var option = MergeSourceOrTarget.Target;
                    if (_parser.Match(TokenType.BY))
                    {
                        if (_parser.Match(TokenType.SOURCE)) option = MergeSourceOrTarget.Source;
                        else if (_parser.Match(TokenType.TARGET)) option = MergeSourceOrTarget.Target;
                    }
                    
                    Expression? andExpr = null;
                    if (_parser.Match(TokenType.AND)) andExpr = _parser.ParseExpression();

                    _parser.Consume(TokenType.THEN, "Expected THEN");
                    
                    if (_parser.Match(TokenType.INSERT))
                    {
                        List<string>? cols = null;
                        if (_parser.Match(TokenType.LPAREN))
                        {
                            cols = ParseIdentifierList();
                            _parser.Consume(TokenType.RPAREN, "Expected ')'");
                        }
                        
                        _parser.Consume(TokenType.VALUES, "Expected VALUES");
                        _parser.Consume(TokenType.LPAREN, "Expected '('");
                        var vals = new List<Expression>();
                        do
                        {
                            vals.Add(_parser.ParseExpression());
                        } while (_parser.Match(TokenType.COMMA));
                        _parser.Consume(TokenType.RPAREN, "Expected ')'");
                        
                        whenNotMatched.Add(new MergeInsertClause(andExpr, cols, vals, option));
                    }
                    else if (_parser.Match(TokenType.UPDATE))
                    {
                        _parser.Consume(TokenType.SET, "Expected SET");
                        var assignments = new List<Assignment>();
                        do
                        {
                            var col = _parser.ConsumeIdentifier("Expected column name").Value;
                            _parser.Consume(TokenType.EQUALS, "Expected '='");
                            var expr = _parser.ParseExpression();
                            assignments.Add(new Assignment(col, expr));
                        } while (_parser.Match(TokenType.COMMA));
                        
                        // Treat NOT MATCHED BY SOURCE UPDATE as a special condition or handle via Ast
                        // For now we fulfill the parser requirement
                        whenNotMatched.Add(new MergeNotMatchedClause(andExpr, option) { ActionType = MergeActionType.UPDATE, UpdateAssignments = assignments });
                    }
                    else if (_parser.Match(TokenType.DELETE))
                    {
                        whenNotMatched.Add(new MergeNotMatchedClause(andExpr, option) { ActionType = MergeActionType.DELETE });
                    }
                }
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();

            return new MergeStatement(targetTable, targetAlias, sourceTable, sourceAlias, onClause, whenMatched, whenNotMatched)
            {
                Line = startToken.Line,
                Column = startToken.Column
            };
        }

        private Statement ParseBulkInsert()
        {
            var startToken = _parser.Previous;
            if (!_parser.Match(TokenType.INSERT) && !_parser.Match(TokenType.LOAD))
            {
                throw new SyntaxException("Expected INSERT or LOAD after BULK", _parser.Current.Line, _parser.Current.Column);
            }
            var targetTable = ParseTableReference(false);
            List<string>? columns = null;
            if (_parser.Match(TokenType.LPAREN))
            {
                columns = ParseIdentifierList();
                _parser.Consume(TokenType.RPAREN, "Expected ')' after column list");
            }
            _parser.Consume(TokenType.FROM, "Expected FROM in BULK INSERT");
            var sourceFile = _parser.ParseExpression();

            Dictionary<string, string>? options = null;
            if (_parser.Match(TokenType.WITH))
            {
                options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _parser.Consume(TokenType.LPAREN, "Expected '(' after WITH");
                while (!_parser.Match(TokenType.RPAREN))
                {
                    var keyTok = _parser.Advance();
                    _parser.Consume(TokenType.EQUALS, "Expected '='");
                    var valTok = _parser.Advance();
                    options[keyTok.Value] = valTok.Value;
                    if (!_parser.Match(TokenType.COMMA))
                    {
                        _parser.Consume(TokenType.RPAREN, "Expected ')' or ','");
                        break;
                    }
                }
            }

            if (_parser.Current.Type == TokenType.SEMICOLON) _parser.Advance();
            
            // BulkInsertStatement record expects string for path and Dict<string, Expression> for options
            var optionsExpr = options?.ToDictionary(kv => kv.Key, kv => (Expression)new LiteralExpression(kv.Value, TokenType.STRING));
            return new BulkInsertStatement(targetTable, sourceFile.ToSql().Trim('\''), optionsExpr ?? new(), columns) 
            { 
                Line = startToken.Line, 
                Column = startToken.Column 
            };
        }
    }
}
