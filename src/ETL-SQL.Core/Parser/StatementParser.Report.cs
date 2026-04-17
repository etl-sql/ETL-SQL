using System;
using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser
{
    /// <summary>
    /// Report-SQL statement parsers (Phase 9A).
    /// Partial class of <see cref="StatementParser"/>.
    ///
    /// Handles:
    ///   CREATE VISUAL name AS type ( ... )
    ///   CREATE PAGE name AS LAYOUT ( ... ) WITH PARAMETERS ( ... )
    ///   CREATE DATASET #name REFRESH EVERY '...' ... AS ( SELECT ... )
    /// </summary>
    public partial class StatementParser
    {
        private bool ReportCheck(TokenType t) => _parser.Current.Type == t;
        private bool ReportAtEnd()            => _parser.Current.Type == TokenType.EOF;

        // ── CREATE VISUAL ─────────────────────────────────────────────────────

        private Statement ParseCreateVisual(Token startToken)
        {
            var name      = _parser.ConsumeIdentifier("Expected visual name after CREATE VISUAL").Value;
            _parser.Consume(TokenType.AS, "Expected AS after visual name");
            var visualType = ParseVisualType();
            _parser.Consume(TokenType.LPAREN, "Expected '(' after visual type");

            VisualSourceExpression? source = null;
            string? title = null;
            string? subtitle = null;
            var mappings    = new List<VisualMapping>();
            var options     = new List<VisualOption>();
            var axisOptions = new List<AxisOptions>();
            var actions     = new List<VisualAction>();
            var styles      = new Dictionary<string, string>();
            var typedSeries = new List<TypedSeries>();

            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (_parser.Match(TokenType.SOURCE))
                {
                    _parser.Match(TokenType.EQUALS); // Optional =
                    source = ParseVisualSource();
                }
                else if (_parser.Match(TokenType.TITLE))
                {
                    _parser.Match(TokenType.EQUALS); // Optional =
                    if (_parser.Match(TokenType.LPAREN))
                    {
                        title = _parser.Consume(TokenType.STRING, "Expected string literal for TITLE").Value;
                        _parser.Consume(TokenType.RPAREN, "Expected ')' after TITLE");
                    }
                    else
                    {
                        title = _parser.Consume(TokenType.STRING, "Expected string literal for TITLE").Value;
                    }
                }
                else if (_parser.Match(TokenType.SUBTITLE))
                {
                    _parser.Match(TokenType.EQUALS); // Optional =
                    if (_parser.Match(TokenType.LPAREN))
                    {
                        subtitle = _parser.Consume(TokenType.STRING, "Expected string literal for SUBTITLE").Value;
                        _parser.Consume(TokenType.RPAREN, "Expected ')' after SUBTITLE");
                    }
                    else
                    {
                        subtitle = _parser.Consume(TokenType.STRING, "Expected string literal for SUBTITLE").Value;
                    }
                }
                else if (_parser.Match(TokenType.MAPPINGS))
                {
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after MAPPINGS");
                    mappings.AddRange(ParseMappings());
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close MAPPINGS");
                }
                else if (_parser.Match(TokenType.OPTIONS))
                {
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after OPTIONS");
                    ParseOptions(options, axisOptions);
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close OPTIONS");
                }
                else if (_parser.Match(TokenType.ACTIONS))
                {
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after ACTIONS");
                    actions.AddRange(ParseActions());
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close ACTIONS");
                }
                else if (_parser.Match(TokenType.STYLE))
                {
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after STYLE");
                    ParseStyleBody(styles);
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close STYLE");
                }
                else if (_parser.Match(TokenType.SERIES))
                {
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after SERIES");
                    typedSeries.AddRange(ParseTypedSeries());
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close SERIES");
                }
                else
                {
                    throw new SyntaxException(
                        $"Unexpected token '{_parser.Current.Value}' inside CREATE VISUAL body",
                        _parser.Current.Line, _parser.Current.Column);
                }
                _parser.Match(TokenType.COMMA);
            }

            _parser.Consume(TokenType.RPAREN, "Expected ')' to close CREATE VISUAL");
            _parser.Match(TokenType.SEMICOLON);

            if (source == null)
            {
                // Filter controls and TEXT visuals are data-source-optional
                if (visualType == VisualType.Text
                    || visualType == VisualType.DatePicker
                    || visualType == VisualType.Slider
                    || visualType == VisualType.Search)
                    source = new VisualSourceExpression();
                else
                    throw new SyntaxException($"CREATE VISUAL '{name}' is missing a SOURCE clause.", startToken.Line, startToken.Column);
            }

            return new CreateVisualStatement
            {
                Name        = name,
                VisualType  = visualType,
                Title       = title,
                Subtitle    = subtitle,
                Source      = source,
                Mappings    = mappings,
                Options     = options,
                AxisOptions = axisOptions,
                Actions     = actions,
                TypedSeries = typedSeries,
                Styles      = styles,
                Line        = startToken.Line,
                Column      = startToken.Column
            };
        }

        private VisualType ParseVisualType()
        {
            if (_parser.Match(TokenType.BAR))          return VisualType.Bar;
            if (_parser.Match(TokenType.LINE))         return VisualType.Line;
            if (_parser.Match(TokenType.SCATTER))      return VisualType.Scatter;
            if (_parser.Match(TokenType.PIE))          return VisualType.Pie;
            if (_parser.Match(TokenType.TABLE_VISUAL)) return VisualType.Table;
            if (_parser.Match(TokenType.TABLE))        return VisualType.Table;
            if (_parser.Match(TokenType.CARD))         return VisualType.Card;
            if (_parser.Match(TokenType.SLICER))       return VisualType.Slicer;
            if (_parser.Match(TokenType.HEATMAP))      return VisualType.HeatMap;
            if (_parser.Match(TokenType.DONUT))        return VisualType.Donut;
            if (_parser.Match(TokenType.HBAR))         return VisualType.HorizontalBar;
            if (_parser.Match(TokenType.BOXPLOT))      return VisualType.BoxPlot;
            if (_parser.Match(TokenType.TREEMAP))      return VisualType.Treemap;
            if (_parser.Match(TokenType.TEXT))         return VisualType.Text;
            if (_parser.Match(TokenType.COMBO))        return VisualType.Combo;
            if (_parser.Match(TokenType.DATEPICKER))   return VisualType.DatePicker;
            if (_parser.Match(TokenType.SLIDER))       return VisualType.Slider;
            if (_parser.Match(TokenType.MULTISELECT))  return VisualType.MultiSelect;
            if (_parser.Match(TokenType.SEARCH))       return VisualType.Search;

            // Fallback: visual type may arrive as IDENTIFIER when lexer context is ambiguous
            if (_parser.Current.Type == TokenType.IDENTIFIER)
            {
                var val = _parser.Current.Value.ToUpperInvariant();
                _parser.Advance();
                return val switch
                {
                    "BAR"         => VisualType.Bar,
                    "LINE"        => VisualType.Line,
                    "SCATTER"     => VisualType.Scatter,
                    "PIE"         => VisualType.Pie,
                    "TABLE"       => VisualType.Table,
                    "CARD"        => VisualType.Card,
                    "SLICER"      => VisualType.Slicer,
                    "HEATMAP"     => VisualType.HeatMap,
                    "DONUT"       => VisualType.Donut,
                    "HBAR"        => VisualType.HorizontalBar,
                    "BOXPLOT"     => VisualType.BoxPlot,
                    "TREEMAP"     => VisualType.Treemap,
                    "TEXT"        => VisualType.Text,
                    "COMBO"       => VisualType.Combo,
                    "DATEPICKER"  => VisualType.DatePicker,
                    "SLIDER"      => VisualType.Slider,
                    "MULTISELECT" => VisualType.MultiSelect,
                    "SEARCH"      => VisualType.Search,
                    _ => throw new SyntaxException(
                             $"Unknown visual type '{val}'.",
                             _parser.Previous.Line, _parser.Previous.Column)
                };
            }

            throw new SyntaxException(
                $"Expected visual type (BAR, LINE, SCATTER, PIE, TABLE, CARD, SLICER, HEATMAP, DONUT, HBAR, BOXPLOT, TREEMAP, TEXT, COMBO, DATEPICKER, SLIDER, MULTISELECT, SEARCH) but got '{_parser.Current.Value}'",
                _parser.Current.Line, _parser.Current.Column);
        }

        private VisualSourceExpression ParseVisualSource()
        {
            if (_parser.Match(TokenType.LPAREN))
            {
                // Inline SELECT; ParseStatement() stops at next ';' or on unrecognized token before ')'
                var select = (SelectStatement)_parser.ParseStatement();
                _parser.Consume(TokenType.RPAREN, "Expected ')' to close SOURCE subquery");
                return new VisualSourceExpression { InlineSelect = select };
            }

            if (_parser.Current.Type == TokenType.SELECT)
            {
                // Raw inline SELECT
                var select = (SelectStatement)_parser.ParseStatement();
                return new VisualSourceExpression { InlineSelect = select };
            }

            if (_parser.Match(TokenType.STRING))
            {
                var val = _parser.Previous.Value;
                if (val.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    // Re-parse the string as a script to extract the SelectStatement
                    var subLexer = new Lexer(val);
                    var subParser = new Parser(subLexer.Tokenize(), val);
                    var subScript = subParser.Parse();
                    if (subScript.Statements.Count > 0 && subScript.Statements[0] is SelectStatement sel)
                    {
                        return new VisualSourceExpression { InlineSelect = sel };
                    }
                }
                return new VisualSourceExpression { TempTableName = val };
            }

            // #temp table reference — may start with # or just be a plain name
            var tableRef = _parser.ConsumeIdentifier("Expected #tableName or SELECT or ( SELECT ... ) after SOURCE").Value;
            return new VisualSourceExpression { TempTableName = tableRef };
        }

        private IEnumerable<VisualMapping> ParseMappings()
        {
            var result = new List<VisualMapping>();
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                var role   = _parser.ConsumeIdentifier("Expected mapping role name").Value;
                _parser.Consume(TokenType.EQUALS, $"Expected '=' after mapping role '{role}'");
                var column = _parser.ConsumeIdentifier("Expected column name after '='").Value;
                result.Add(new VisualMapping { Role = role, Column = column });
                _parser.Match(TokenType.COMMA);
            }
            return result;
        }

        private void ParseOptions(List<VisualOption> options, List<AxisOptions> axisOptions)
        {
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (_parser.Match(TokenType.X_AXIS))
                {
                    var axisOpts = new AxisOptions { Axis = "X" };
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after X_AXIS");
                    ParseAxisOptionBody(axisOpts.Options);
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close X_AXIS");
                    axisOptions.Add(axisOpts);
                }
                else if (_parser.Match(TokenType.Y_AXIS))
                {
                    var axisOpts = new AxisOptions { Axis = "Y" };
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after Y_AXIS");
                    ParseAxisOptionBody(axisOpts.Options);
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close Y_AXIS");
                    axisOptions.Add(axisOpts);
                }
                else if (_parser.Match(TokenType.COLORS))
                {
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after COLORS");
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        // Key may be a quoted string (e.g. 'North America') or an identifier
                        var colorKey = _parser.Current.Type == TokenType.STRING
                            ? (_parser.Advance().Value)
                            : _parser.ConsumeIdentifier("Expected color key in COLORS").Value;
                        _parser.Consume(TokenType.EQUALS, "Expected '=' after color key");
                        var colorVal = ConsumeReportOptionValue();
                        options.Add(new VisualOption { Key = "color:" + colorKey, Value = colorVal });
                        _parser.Match(TokenType.COMMA);
                    }
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close COLORS");
                }
                else
                {
                    var key = _parser.ConsumeIdentifier("Expected option key").Value;
                    _parser.Consume(TokenType.EQUALS, $"Expected '=' after option '{key}'");
                    var val = ConsumeReportOptionValue();
                    options.Add(new VisualOption { Key = key, Value = val });
                }
                _parser.Match(TokenType.COMMA);
            }
        }

        private void ParseAxisOptionBody(List<VisualOption> opts)
        {
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                var key = _parser.ConsumeIdentifier("Expected axis option key").Value;
                _parser.Consume(TokenType.EQUALS, "Expected '=' in axis option");
                var val = ConsumeReportOptionValue();
                opts.Add(new VisualOption { Key = key, Value = val });
                _parser.Match(TokenType.COMMA);
            }
        }

        private string ConsumeReportOptionValue()
        {
            var t = _parser.Current.Type;
            if (t == TokenType.STRING   || t == TokenType.NUMBER  ||
                t == TokenType.IDENTIFIER || t == TokenType.TRUE  || t == TokenType.FALSE ||
                t == TokenType.TOP      || t == TokenType.BOTTOM  ||
                t == TokenType.LEFT     || t == TokenType.RIGHT)
            {
                var value = _parser.Current.Value;
                _parser.Advance();
                return value;
            }
            throw new SyntaxException(
                $"Expected option value but got '{_parser.Current.Value}'",
                _parser.Current.Line, _parser.Current.Column);
        }

        private IEnumerable<VisualAction> ParseActions()
        {
            var result = new List<VisualAction>();
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                string trigger;
                if (_parser.Match(TokenType.ON_CLICK))       trigger = "ON_CLICK";
                else if (_parser.Match(TokenType.ON_CHANGE)) trigger = "ON_CHANGE";
                else throw new SyntaxException(
                    $"Expected ON_CLICK or ON_CHANGE in ACTIONS but got '{_parser.Current.Value}'",
                    _parser.Current.Line, _parser.Current.Column);

                _parser.Consume(TokenType.EQUALS, $"Expected '=' after {trigger}");

                VisualAction action;
                if (_parser.Match(TokenType.DRILL_DOWN))
                {
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after DRILL_DOWN");
                    // TARGET and KEY are reserved keywords, so advance past them generically
                    _parser.Advance(); // skip Target label
                    _parser.Consume(TokenType.EQUALS, "Expected '=' after Target");
                    var target = _parser.ConsumeIdentifier("Expected target visual name").Value;
                    _parser.Match(TokenType.COMMA);
                    _parser.Advance(); // skip Key label
                    _parser.Consume(TokenType.EQUALS, "Expected '=' after Key");
                    var key = _parser.ConsumeIdentifier("Expected key column name").Value;
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close DRILL_DOWN");
                    action = new DrillDownAction { Trigger = trigger, TargetVisual = target, KeyColumn = key };
                }
                else if (_parser.Match(TokenType.SET_PARAMETER))
                {
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after SET_PARAMETER");
                    var paramName = _parser.ConsumeIdentifier("Expected parameter name").Value;
                    if (!paramName.StartsWith("@")) paramName = "@" + paramName;
                    _parser.Match(TokenType.COMMA);
                    var valueExpr = _parser.ConsumeIdentifier("Expected value expression").Value;
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close SET_PARAMETER");
                    action = new SetParameterAction { Trigger = trigger, ParameterName = paramName, ValueExpression = valueExpr };
                }
                else
                {
                    throw new SyntaxException(
                        $"Expected DRILL_DOWN or SET_PARAMETER after {trigger} =",
                        _parser.Current.Line, _parser.Current.Column);
                }

                result.Add(action);
                _parser.Match(TokenType.COMMA);
            }
            return result;
        }

        // ── CREATE PAGE ───────────────────────────────────────────────────────

        private Statement ParseCreatePage(Token startToken)
        {
            var name = _parser.ConsumeIdentifier("Expected page name after CREATE PAGE").Value;
            _parser.Consume(TokenType.AS,     "Expected AS after page name");
            _parser.Consume(TokenType.LAYOUT, "Expected LAYOUT after AS");
            _parser.Consume(TokenType.LPAREN, "Expected '(' after LAYOUT");

            string? structure = null;
            var slotMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pageStyles = new Dictionary<string, string>();

            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (_parser.Match(TokenType.STRUCTURE))
                {
                    _parser.Consume(TokenType.EQUALS, "Expected '=' after STRUCTURE");
                    structure = _parser.Consume(TokenType.STRING, "Expected string literal for STRUCTURE").Value;
                }
                else if (_parser.Match(TokenType.MAP))
                {
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after MAP");
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        var slot   = _parser.Consume(TokenType.STRING, "Expected slot letter (e.g. 'A')").Value;
                        _parser.Consume(TokenType.EQUALS, "Expected '=' in MAP entry");
                        var visual = _parser.ConsumeIdentifier("Expected visual name after '='").Value;
                        slotMap[slot] = visual;
                        _parser.Match(TokenType.COMMA);
                    }
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close MAP");
                }
                else if (_parser.Match(TokenType.STYLE))
                {
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after STYLE");
                    ParseStyleBody(pageStyles);
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close STYLE");
                }
                else
                {
                    throw new SyntaxException(
                        $"Unexpected token '{_parser.Current.Value}' in CREATE PAGE body",
                        _parser.Current.Line, _parser.Current.Column);
                }
                _parser.Match(TokenType.COMMA);
            }
            _parser.Consume(TokenType.RPAREN, "Expected ')' to close CREATE PAGE LAYOUT");

            var parameters = new List<PageParameter>();
            if (_parser.Match(TokenType.WITH))
            {
                // Consume PARAMETERS as an identifier (no dedicated token)
                _parser.ConsumeIdentifier("Expected PARAMETERS after WITH");
                _parser.Consume(TokenType.LPAREN, "Expected '(' after PARAMETERS");
                while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                {
                    var paramName = _parser.ConsumeIdentifier("Expected parameter name").Value;
                    if (!paramName.StartsWith("@")) paramName = "@" + paramName;
                    string? defaultVal = null;
                    if (_parser.Match(TokenType.EQUALS))
                        defaultVal = ConsumeReportOptionValue();
                    parameters.Add(new PageParameter { Name = paramName, DefaultValue = defaultVal });
                    _parser.Match(TokenType.COMMA);
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' to close PARAMETERS");
            }

            _parser.Match(TokenType.SEMICOLON);

            if (structure == null)
                throw new SyntaxException($"CREATE PAGE '{name}' is missing a STRUCTURE clause.", startToken.Line, startToken.Column);

            return new CreatePageStatement
            {
                Name       = name,
                Structure  = structure,
                SlotMap    = slotMap,
                Parameters = parameters,
                Styles     = pageStyles,
                Line       = startToken.Line,
                Column     = startToken.Column
            };
        }

        // ── CREATE DATASET ────────────────────────────────────────────────────

        private Statement ParseCreateDataset(Token startToken)
        {
            var tableName = _parser.ConsumeIdentifier("Expected #tableName after CREATE DATASET").Value;
            if (!tableName.StartsWith("#")) tableName = "#" + tableName;

            string? refreshInterval                          = null;
            string? ttl                                     = null;
            bool    compress                                = false;
            var     encryptionMode                          = DatasetEncryptionMode.None;
            string? encryptionPassword                      = null;
            string? keyFile                                 = null;

            while (!ReportCheck(TokenType.AS) && !ReportAtEnd())
            {
                if (_parser.Match(TokenType.REFRESH))
                {
                    _parser.Consume(TokenType.EVERY, "Expected EVERY after REFRESH");
                    refreshInterval = _parser.Consume(TokenType.STRING, "Expected interval string after REFRESH EVERY").Value;
                }
                else if (_parser.Match(TokenType.TTL))
                {
                    _parser.Match(TokenType.EQUALS);
                    ttl = _parser.Consume(TokenType.STRING, "Expected TTL duration string").Value;
                }
                else if (_parser.Match(TokenType.COMPRESS))
                {
                    _parser.Match(TokenType.EQUALS);
                    compress = ParseOnOffValue();
                }
                else if (_parser.Match(TokenType.ENCRYPT))
                {
                    _parser.Match(TokenType.EQUALS);
                    var modeVal = _parser.Current.Value.ToUpperInvariant();
                    _parser.Advance();
                    encryptionMode = modeVal switch
                    {
                        "MACHINE"  => DatasetEncryptionMode.MachineBound,
                        "PASSWORD" => DatasetEncryptionMode.Password,
                        "KEYFILE"  => DatasetEncryptionMode.KeyFile,
                        "ON" or "TRUE" or "1" => DatasetEncryptionMode.MachineBound,
                        _ => DatasetEncryptionMode.None
                    };
                }
                else if (_parser.Match(TokenType.PASSWORD))
                {
                    _parser.Match(TokenType.EQUALS);
                    encryptionPassword = _parser.Consume(TokenType.STRING, "Expected password string after PASSWORD =").Value;
                }
                else if (_parser.Match(TokenType.KEYFILE))
                {
                    _parser.Match(TokenType.EQUALS);
                    keyFile = _parser.Consume(TokenType.STRING, "Expected key file path after KEYFILE").Value;
                }
                else
                {
                    throw new SyntaxException(
                        $"Unexpected token '{_parser.Current.Value}' in CREATE DATASET options",
                        _parser.Current.Line, _parser.Current.Column);
                }
            }

            _parser.Consume(TokenType.AS,     "Expected AS before source query");
            _parser.Consume(TokenType.LPAREN, "Expected '(' before source SELECT");
            var sourceSelect = (SelectStatement)_parser.ParseStatement();
            _parser.Consume(TokenType.RPAREN, "Expected ')' after source SELECT");
            _parser.Match(TokenType.SEMICOLON);

            return new CreateDatasetStatement
            {
                TempTableName      = tableName,
                RefreshInterval    = refreshInterval,
                Ttl                = ttl,
                Compress           = compress,
                EncryptionMode     = encryptionMode,
                EncryptionPassword = encryptionPassword,
                KeyFile            = keyFile,
                SourceQuery        = sourceSelect,
                Line               = startToken.Line,
                Column             = startToken.Column
            };
        }

        private bool ParseOnOffValue()
        {
            var value = _parser.Current.Value;
            _parser.Advance();
            return value.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
                   value == "1";
        }

        // ── STYLE body helper ──────────────────────────────────────────────────

        private void ParseStyleBody(Dictionary<string, string> styles)
        {
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                // Key can be any identifier-like token
                var key = _parser.ConsumeIdentifier("Expected style key").Value;
                _parser.Consume(TokenType.EQUALS, $"Expected '=' after style key '{key}'");
                // Value: string, number, or identifier
                string val;
                var t = _parser.Current.Type;
                if (t == TokenType.STRING || t == TokenType.NUMBER || t == TokenType.IDENTIFIER ||
                    t == TokenType.TRUE || t == TokenType.FALSE)
                {
                    val = _parser.Current.Value;
                    _parser.Advance();
                }
                else
                {
                    val = _parser.Current.Value;
                    _parser.Advance();
                }
                styles[key] = val;
                _parser.Match(TokenType.COMMA);
            }
        }

        // ── SERIES body helper (COMBO) ─────────────────────────────────────────

        private IEnumerable<TypedSeries> ParseTypedSeries()
        {
            var result = new List<TypedSeries>();
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                // Consume the series type (BAR or LINE) — may be a keyword or identifier
                string seriesType;
                if (_parser.Match(TokenType.BAR))       seriesType = "bar";
                else if (_parser.Match(TokenType.LINE)) seriesType = "line";
                else
                {
                    var raw = _parser.ConsumeIdentifier("Expected BAR or LINE in SERIES").Value;
                    seriesType = raw.ToLowerInvariant();
                }

                var column = _parser.ConsumeIdentifier("Expected column name after series type").Value;
                result.Add(new TypedSeries { SeriesType = seriesType, Column = column });
                _parser.Match(TokenType.COMMA);
            }
            return result;
        }

        // ── CREATE CONTAINER ──────────────────────────────────────────────────

        private Statement ParseCreateContainer(Token startToken)
        {
            var name = _parser.ConsumeIdentifier("Expected container name after CREATE CONTAINER").Value;
            _parser.Consume(TokenType.AS, "Expected AS after container name");

            // ContainerType: BOX or SCROLL
            string containerType;
            if (_parser.Match(TokenType.BOX))         containerType = "BOX";
            else if (_parser.Match(TokenType.SCROLL))  containerType = "SCROLL";
            else
            {
                var raw = _parser.ConsumeIdentifier("Expected BOX or SCROLL after AS").Value.ToUpperInvariant();
                containerType = raw is "BOX" or "SCROLL" ? raw : "BOX";
            }

            _parser.Consume(TokenType.LPAREN, "Expected '(' after container type");

            var styles  = new Dictionary<string, string>();
            var visuals = new List<string>();

            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (_parser.Match(TokenType.STYLE))
                {
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after STYLE");
                    ParseStyleBody(styles);
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close STYLE");
                }
                else if (_parser.Current.Type == TokenType.IDENTIFIER &&
                         _parser.Current.Value.Equals("VISUALS", StringComparison.OrdinalIgnoreCase))
                {
                    _parser.Advance(); // consume "VISUALS"
                    _parser.Consume(TokenType.LPAREN, "Expected '(' after VISUALS");
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        visuals.Add(_parser.ConsumeIdentifier("Expected visual name in VISUALS list").Value);
                        _parser.Match(TokenType.COMMA);
                    }
                    _parser.Consume(TokenType.RPAREN, "Expected ')' to close VISUALS");
                }
                else
                {
                    throw new SyntaxException(
                        $"Unexpected token '{_parser.Current.Value}' in CREATE CONTAINER body",
                        _parser.Current.Line, _parser.Current.Column);
                }
                _parser.Match(TokenType.COMMA);
            }

            _parser.Consume(TokenType.RPAREN, "Expected ')' to close CREATE CONTAINER");
            _parser.Match(TokenType.SEMICOLON);

            return new CreateContainerStatement
            {
                Name          = name,
                ContainerType = containerType,
                Visuals       = visuals,
                Styles        = styles,
                Line          = startToken.Line,
                Column        = startToken.Column
            };
        }

        // ── CREATE NAVIGATION ─────────────────────────────────────────────────

        private Statement ParseCreateNavigation(Token startToken)
        {
            var name = _parser.ConsumeIdentifier("Expected navigation name after CREATE NAVIGATION").Value;
            _parser.Consume(TokenType.AS, "Expected AS after navigation name");

            // NavType: TAB, BUTTON, or LINK (LINK_NAV token)
            NavigationType navType;
            if (_parser.Match(TokenType.NAV_TAB))       navType = NavigationType.Tab;
            else if (_parser.Match(TokenType.BUTTON))   navType = NavigationType.Button;
            else if (_parser.Match(TokenType.LINK_NAV)) navType = NavigationType.Link;
            else
            {
                var raw = _parser.ConsumeIdentifier("Expected TAB, BUTTON, or LINK after AS").Value.ToUpperInvariant();
                navType = raw switch
                {
                    "TAB"    => NavigationType.Tab,
                    "BUTTON" => NavigationType.Button,
                    "LINK"   => NavigationType.Link,
                    _        => NavigationType.Tab
                };
            }

            _parser.Consume(TokenType.LPAREN, "Expected '(' after navigation type");

            var orientation = NavigationOrientation.Horizontal;
            string? defaultPage = null;

            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (_parser.Current.Type == TokenType.IDENTIFIER &&
                    _parser.Current.Value.Equals("ORIENTATION", StringComparison.OrdinalIgnoreCase))
                {
                    _parser.Advance(); // consume ORIENTATION
                    _parser.Consume(TokenType.EQUALS, "Expected '=' after ORIENTATION");
                    var oriVal = _parser.ConsumeIdentifier("Expected HORIZONTAL or VERTICAL").Value.ToUpperInvariant();
                    orientation = oriVal == "VERTICAL" ? NavigationOrientation.Vertical : NavigationOrientation.Horizontal;
                }
                else if (_parser.Current.Type == TokenType.IDENTIFIER &&
                         _parser.Current.Value.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase))
                {
                    _parser.Advance(); // consume DEFAULT
                    _parser.Consume(TokenType.EQUALS, "Expected '=' after DEFAULT");
                    defaultPage = _parser.ConsumeIdentifier("Expected page name after DEFAULT =").Value;
                }
                else if (_parser.Current.Type == TokenType.DEFAULT)
                {
                    _parser.Advance(); // consume DEFAULT keyword token
                    _parser.Consume(TokenType.EQUALS, "Expected '=' after DEFAULT");
                    defaultPage = _parser.ConsumeIdentifier("Expected page name after DEFAULT =").Value;
                }
                else
                {
                    throw new SyntaxException(
                        $"Unexpected token '{_parser.Current.Value}' in CREATE NAVIGATION body",
                        _parser.Current.Line, _parser.Current.Column);
                }
                _parser.Match(TokenType.COMMA);
            }

            _parser.Consume(TokenType.RPAREN, "Expected ')' to close CREATE NAVIGATION options");

            var pages = new List<string>();
            if (_parser.Match(TokenType.WITH))
            {
                _parser.ConsumeIdentifier("Expected PAGES after WITH");
                _parser.Consume(TokenType.LPAREN, "Expected '(' after PAGES");
                while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                {
                    pages.Add(_parser.ConsumeIdentifier("Expected page name").Value);
                    _parser.Match(TokenType.COMMA);
                }
                _parser.Consume(TokenType.RPAREN, "Expected ')' to close PAGES");
            }

            _parser.Match(TokenType.SEMICOLON);

            return new CreateNavigationStatement
            {
                Name        = name,
                NavType     = navType,
                Orientation = orientation,
                DefaultPage = defaultPage,
                Pages       = pages,
                Line        = startToken.Line,
                Column      = startToken.Column
            };
        }
    }
}
