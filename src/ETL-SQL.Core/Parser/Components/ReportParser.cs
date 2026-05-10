using System;
using System.Collections.Generic;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Common;

namespace ETL_SQL.Core.Parser.Components
{
    public class ReportParser : ParserComponent
    {
        public ReportParser(IParser parser, StatementParser parent) : base(parser, parent) { }

        private bool ReportCheck(TokenType t) => _parser.Current.Type == t;
        private bool ReportAtEnd()            => _parser.Current.Type == TokenType.EOF;

        // ── CREATE VISUAL ─────────────────────────────────────────────────────

        public Statement ParseCreateVisual(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
        {
            var name       = ConsumeIdentifier("Expected visual name after CREATE VISUAL").Value;
            Consume(TokenType.AS, "Expected AS after visual name");
            var visualType = ParseVisualType();
            Consume(TokenType.LPAREN, "Expected '(' after visual type");

            VisualSourceExpression? source = null;
            string? title = null, subtitle = null;
            bool titleMd = false, subtitleMd = false;
            string? defaultValue = null;
            string? styleName = null, placeholder = null;
            TooltipDefinition? tooltip = null;
            var mappings        = new List<VisualMapping>();
            var options         = new List<VisualOption>();
            var axisOptions     = new List<AxisOptions>();
            var actions         = new List<VisualAction>();
            var styles          = new Dictionary<string, string>();
            var typedSeries     = new List<TypedSeries>();
            var formattingRules = new List<FormattingRule>();
            var overlays        = new List<VisualOverlay>();
            var summaries       = new List<TableSummaryItem>();
            string? labelPosition = null;
            double? min = null, max = null;
            int? decimals = null;
            TableSummaryOptions? summaryOptions = null;

            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (Match(TokenType.SOURCE))
                {
                    Match(TokenType.EQUALS);
                    source = ParseVisualSource();
                }
                else if (Match(TokenType.TITLE))
                {
                    (title, titleMd) = ParseVisualPropertyWithMd("TITLE");
                }
                else if (Match(TokenType.SUBTITLE))
                {
                    (subtitle, subtitleMd) = ParseVisualPropertyWithMd("SUBTITLE");
                }
                else if (Match(TokenType.TOOLTIP))
                {
                    tooltip = ParseTooltipDefinition();
                }
                else if (Match(TokenType.MAPPINGS))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after MAPPINGS");
                    mappings.AddRange(ParseMappings());
                    Consume(TokenType.RPAREN, "Expected ')' to close MAPPINGS");
                }
                else if (Match(TokenType.OPTIONS))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after OPTIONS");
                    ParseOptions(options, axisOptions);
                    Consume(TokenType.RPAREN, "Expected ')' to close OPTIONS");
                }
                else if (Match(TokenType.ACTIONS))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after ACTIONS");
                    actions.AddRange(ParseActions());
                    Consume(TokenType.RPAREN, "Expected ')' to close ACTIONS");
                }
                else if (Match(TokenType.STYLE))
                {
                    ParseStyleClause(styles, ref styleName);
                }
                else if (Match(TokenType.SERIES))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after SERIES");
                    typedSeries.AddRange(ParseTypedSeries());
                    Consume(TokenType.RPAREN, "Expected ')' to close SERIES");
                }
                else if (Match(TokenType.FORMATTING))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after FORMATTING");
                    formattingRules.AddRange(ParseFormattingRules());
                    Consume(TokenType.RPAREN, "Expected ')' to close FORMATTING");
                }
                else if (Match(TokenType.OVERLAYS))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after OVERLAYS");
                    overlays.AddRange(ParseOverlays());
                    Consume(TokenType.RPAREN, "Expected ')' to close OVERLAYS");
                }
                else if (Match(TokenType.CONTENT))
                {
                    defaultValue = ParseVisualProperty("CONTENT");
                }
                else if (Match(TokenType.DEFAULT) || Match(TokenType.VALUE))
                {
                    defaultValue = ParseVisualProperty("DEFAULT");
                }
                else if (Match(TokenType.LABEL_POSITION))
                {
                    Match(TokenType.EQUALS);
                    labelPosition = ConsumeReportOptionValue();
                }
                else if (Match(TokenType.MIN))
                {
                    Match(TokenType.EQUALS);
                    if (double.TryParse(ConsumeReportOptionValue(), out var minVal)) min = minVal;
                }
                else if (Match(TokenType.MAX))
                {
                    Match(TokenType.EQUALS);
                    if (double.TryParse(ConsumeReportOptionValue(), out var maxVal)) max = maxVal;
                }
                else if (Match(TokenType.DECIMALS))
                {
                    Match(TokenType.EQUALS);
                    if (int.TryParse(ConsumeReportOptionValue(), out var decVal)) decimals = decVal;
                }
                else if (Match(TokenType.PLACEHOLDER))
                {
                    Match(TokenType.EQUALS);
                    placeholder = ConsumeReportOptionValue();
                }
                else if (Match(TokenType.SUMMARY))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after SUMMARY");
                    (summaries, summaryOptions) = ParseSummaryClause();
                    Consume(TokenType.RPAREN, "Expected ')' to close SUMMARY");
                }
                else
                {
                    throw new SyntaxException(
                        $"Unexpected token '{_parser.Current.Value}' inside CREATE VISUAL body",
                        _parser.Current.Line, _parser.Current.Column);
                }
                Match(TokenType.COMMA);
            }

            Consume(TokenType.RPAREN, "Expected ')' to close CREATE VISUAL");
            Match(TokenType.SEMICOLON);

            if (source == null)
            {
                if (visualType == VisualType.Text
                    || visualType == VisualType.DatePicker
                    || visualType == VisualType.RelDatePicker
                    || visualType == VisualType.Slider
                    || visualType == VisualType.Search
                    || visualType == VisualType.Slicer
                    || visualType == VisualType.MultiSelect
                    || visualType == VisualType.Checkbox
                    || visualType == VisualType.Textbox
                    || visualType == VisualType.Numberbox
                    || visualType == VisualType.Image)
                    source = new VisualSourceExpression();
                else
                    throw new SyntaxException($"CREATE VISUAL '{name}' is missing a SOURCE clause.", startToken.Line, startToken.Column);
            }

            return new CreateVisualStatement
            {
                Name            = name,
                VisualType      = visualType,
                Title           = title,
                TitleIsMarkdown = titleMd,
                Subtitle        = subtitle,
                SubtitleIsMarkdown = subtitleMd,
                DefaultValue    = defaultValue,
                LabelPosition   = labelPosition,
                Min             = min,
                Max             = max,
                Decimals        = decimals,
                Placeholder     = placeholder,
                Source          = source,
                Mappings        = mappings,
                Options         = options,
                AxisOptions     = axisOptions,
                Actions         = actions,
                TypedSeries     = typedSeries,
                FormattingRules = formattingRules,
                Overlays        = overlays,
                Summaries       = summaries,
                SummaryOptions  = summaryOptions,
                Styles          = styles,
                StyleName       = styleName,
                Tooltip         = tooltip,
                Mode            = mode,
                Line            = startToken.Line,
                Column          = startToken.Column
            };
        }

        // ── CREATE PAGE ───────────────────────────────────────────────────────

        public Statement ParseCreatePage(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
        {
            var name = ConsumeIdentifier("Expected page name after CREATE PAGE").Value;
            Consume(TokenType.AS,     "Expected AS after page name");
            Match(TokenType.LAYOUT); // Optional LAYOUT keyword
            Consume(TokenType.LPAREN, "Expected '(' after AS");

            string? structure = null;
            string? pageStyleName = null;
            string? title = null, subtitle = null;
            bool titleMd = false, subtitleMd = false;
            TooltipDefinition? tooltip = null;
            var slotMap    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pageStyles = new Dictionary<string, string>();

            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (Match(TokenType.STRUCTURE))
                {
                    Consume(TokenType.EQUALS, "Expected '=' after STRUCTURE");
                    structure = Consume(TokenType.STRING_LITERAL, "Expected string literal for STRUCTURE").Value;
                }
                else if (Match(TokenType.MAP))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after MAP");
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        var slot   = Consume(TokenType.STRING_LITERAL, "Expected slot letter (e.g. 'A')").Value;
                        Consume(TokenType.EQUALS, "Expected '=' in MAP entry");
                        var visual = ConsumeIdentifier("Expected visual name after '='").Value;
                        slotMap[slot] = visual;
                        Match(TokenType.COMMA);
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close MAP");
                }
                else if (Match(TokenType.STYLE))
                {
                    ParseStyleClause(pageStyles, ref pageStyleName);
                }
                else if (Match(TokenType.TITLE))
                {
                    (title, titleMd) = ParseVisualPropertyWithMd("TITLE");
                }
                else if (Match(TokenType.SUBTITLE))
                {
                    (subtitle, subtitleMd) = ParseVisualPropertyWithMd("SUBTITLE");
                }
                else if (Match(TokenType.TOOLTIP))
                {
                    tooltip = ParseTooltipDefinition();
                }
                else
                {
                    throw new SyntaxException(
                        $"Unexpected token '{_parser.Current.Value}' in CREATE PAGE body",
                        _parser.Current.Line, _parser.Current.Column);
                }
                Match(TokenType.COMMA);
            }
            Consume(TokenType.RPAREN, "Expected ')' to close CREATE PAGE LAYOUT");

            // Optional WITH (HIDDEN = ON, REFRESH = <seconds>) clause
            bool isHidden = false;
            int refreshSecs = 0;
            if (Match(TokenType.WITH))
            {
                Consume(TokenType.LPAREN, "Expected '(' after WITH");
                while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                {
                    var optKey = _parser.Advance().Value;
                    Consume(TokenType.EQUALS, $"Expected '=' after '{optKey}' in WITH clause");
                    var optVal = _parser.Advance().Value;
                    if (string.Equals(optKey, "HIDDEN", StringComparison.OrdinalIgnoreCase))
                        isHidden = string.Equals(optVal, "ON", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(optVal, "TRUE", StringComparison.OrdinalIgnoreCase);
                    else if (string.Equals(optKey, "REFRESH", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(optVal, out refreshSecs);
                    Match(TokenType.COMMA);
                }
                Consume(TokenType.RPAREN, "Expected ')' to close WITH clause");
            }

            Match(TokenType.SEMICOLON);

            if (structure == null)
                throw new SyntaxException($"CREATE PAGE '{name}' is missing a STRUCTURE clause.", startToken.Line, startToken.Column);

            return new CreatePageStatement
            {
                Name            = name,
                Structure       = structure,
                SlotMap         = slotMap,
                Styles          = pageStyles,
                StyleName       = pageStyleName,
                Title           = title,
                TitleIsMarkdown = titleMd,
                Subtitle        = subtitle,
                SubtitleIsMarkdown = subtitleMd,
                Tooltip         = tooltip,
                IsHidden               = isHidden,
                RefreshIntervalSeconds = refreshSecs,
                Mode            = mode,
                Line            = startToken.Line,
                Column          = startToken.Column
            };
        }

        // ── CREATE DATASET ────────────────────────────────────────────────────

        public Statement ParseCreateDataset(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
        {
            var tableName = ConsumeIdentifier("Expected &datasetName after CREATE DATASET").Value;
            // Only prepend '&' when the name has no sigil; '#'-prefixed temp-table names are kept as-is.
            if (!tableName.StartsWith("&") && !tableName.StartsWith("#")) tableName = "&" + tableName;

            string? refreshInterval    = null;
            string? ttl                = null;
            bool    compress           = false;
            var     encryptionMode     = DatasetEncryptionMode.None;
            string? encryptionPassword = null;
            string? keyFile            = null;
            var     accessLevel        = ETL_SQL.Core.Data.DatasetAccessLevel.Private;

            while (!ReportCheck(TokenType.AS) && !ReportAtEnd())
            {
                if (Match(TokenType.REFRESH))
                {
                    Consume(TokenType.EVERY, "Expected EVERY after REFRESH");
                    refreshInterval = Consume(TokenType.STRING_LITERAL, "Expected interval string after REFRESH EVERY").Value;
                }
                else if (Match(TokenType.TTL))
                {
                    Match(TokenType.EQUALS);
                    ttl = Consume(TokenType.STRING_LITERAL, "Expected TTL duration string").Value;
                }
                else if (Match(TokenType.COMPRESS))
                {
                    Match(TokenType.EQUALS);
                    compress = ParseOnOffValue();
                }
                else if (Match(TokenType.ENCRYPT))
                {
                    Match(TokenType.EQUALS);
                    var modeVal = _parser.Current.Value.ToUpperInvariant();
                    Advance();
                    encryptionMode = modeVal switch
                    {
                        "MACHINE"  => DatasetEncryptionMode.MachineBound,
                        "PASSWORD" => DatasetEncryptionMode.Password,
                        "KEYFILE"  => DatasetEncryptionMode.KeyFile,
                        "ON" or "TRUE" or "1" => DatasetEncryptionMode.MachineBound,
                        _ => DatasetEncryptionMode.None
                    };
                }
                else if (Match(TokenType.PASSWORD))
                {
                    Match(TokenType.EQUALS);
                    encryptionPassword = Consume(TokenType.STRING_LITERAL, "Expected password string after PASSWORD =").Value;
                }
                else if (Match(TokenType.KEYFILE))
                {
                    Match(TokenType.EQUALS);
                    keyFile = Consume(TokenType.STRING_LITERAL, "Expected key file path after KEYFILE").Value;
                }
                else if (MatchIdentifier("ACCESS"))
                {
                    var val = _parser.Current.Value.ToUpperInvariant();
                    if (val != "PUBLIC" && val != "PRIVATE")
                        throw new SyntaxException(
                            $"Expected PUBLIC or PRIVATE after ACCESS, got '{_parser.Current.Value}'",
                            _parser.Current.Line, _parser.Current.Column);
                    Advance();
                    accessLevel = val == "PUBLIC"
                        ? ETL_SQL.Core.Data.DatasetAccessLevel.Public
                        : ETL_SQL.Core.Data.DatasetAccessLevel.Private;
                }
                else
                {
                    throw new SyntaxException(
                        $"Unexpected token '{_parser.Current.Value}' in CREATE DATASET options",
                        _parser.Current.Line, _parser.Current.Column);
                }
            }

            Consume(TokenType.AS, "Expected AS before source query");

            Statement sourceSelect;
            if (Match(TokenType.LPAREN))
            {
                sourceSelect = _parser.ParseStatement();
                Consume(TokenType.RPAREN, "Expected ')' after source SELECT");
            }
            else if (_parser.Current.Type == TokenType.SELECT)
            {
                sourceSelect = _parser.ParseStatement();
            }
            else
            {
                throw new SyntaxException("Expected '(' or SELECT after AS in CREATE DATASET", _parser.Current.Line, _parser.Current.Column);
            }

            Match(TokenType.SEMICOLON);

            return new CreateDatasetStatement
            {
                TempTableName      = tableName,
                RefreshInterval    = refreshInterval,
                Ttl                = ttl,
                Compress           = compress,
                EncryptionMode     = encryptionMode,
                EncryptionPassword = encryptionPassword,
                KeyFile            = keyFile,
                AccessLevel        = accessLevel,
                SourceQuery        = sourceSelect,
                Mode               = mode,
                Line               = startToken.Line,
                Column             = startToken.Column
            };
        }

        // ── CREATE STYLE ──────────────────────────────────────────────────────

        public Statement ParseCreateStyle(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
        {
            var name = ConsumeIdentifier("Expected style name after CREATE STYLE").Value;
            Consume(TokenType.LPAREN, "Expected '(' after style name");
            var styles = new Dictionary<string, string>();
            ParseStyleBody(styles);
            Consume(TokenType.RPAREN, "Expected ')' to close CREATE STYLE");
            Match(TokenType.SEMICOLON);
            return new CreateStyleStatement
            {
                Name   = name,
                Styles = styles,
                Mode   = mode,
                Line   = startToken.Line,
                Column = startToken.Column
            };
        }

        public Statement ParseStyleStatement(Token startToken)
        {
            var styles = new Dictionary<string, string>();
            string? styleName = null;
            ParseStyleClause(styles, ref styleName);
            Match(TokenType.SEMICOLON);
            
            return new CreateStyleStatement
            {
                Name   = "GLOBAL",
                Styles = styles,
                StyleName = styleName,
                Mode   = ObjectCreationMode.Create,
                Line   = startToken.Line,
                Column = startToken.Column
            };
        }

        // ── CREATE TEMPLATE ───────────────────────────────────────────────────

        public Statement ParseCreateTemplate(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
        {
            var name = ConsumeIdentifier("Expected template name after CREATE TEMPLATE").Value;
            Consume(TokenType.AS, "Expected AS after template name");
            Consume(TokenType.LPAREN, "Expected '(' after AS");

            var options = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                var key = ConsumeIdentifier("Expected option key").Value;
                Match(TokenType.EQUALS);
                var val = ConsumeReportOptionValue();
                options[key] = val;
                Match(TokenType.COMMA);
            }

            Consume(TokenType.RPAREN, "Expected ')' to close CREATE TEMPLATE");
            Match(TokenType.SEMICOLON);

            return new CreateTemplateStatement
            {
                Name    = name,
                Options = options,
                Mode    = mode,
                Line    = startToken.Line,
                Column  = startToken.Column
            };
        }

        // ── CREATE THEME ─────────────────────────────────────────────────────

        public Statement ParseCreateTheme(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
        {
            var name = ConsumeIdentifier("Expected theme name after CREATE THEME").Value;
            Consume(TokenType.AS, "Expected AS after theme name");
            Consume(TokenType.LPAREN, "Expected '(' after AS");

            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                var key = _parser.Advance().Value;
                Match(TokenType.EQUALS);
                var val = ConsumeReportOptionValue();
                properties[key] = val;
                Match(TokenType.COMMA);
            }

            Consume(TokenType.RPAREN, "Expected ')' to close CREATE THEME");
            Match(TokenType.SEMICOLON);

            return new CreateThemeStatement
            {
                Name       = name,
                Properties = properties,
                Mode       = mode,
                Line       = startToken.Line,
                Column     = startToken.Column
            };
        }

        // ── CREATE CONTAINER ──────────────────────────────────────────────────

        public Statement ParseCreateContainer(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
        {
            var name = ConsumeIdentifier("Expected container name after CREATE CONTAINER").Value;
            Consume(TokenType.AS, "Expected AS after container name");

            string containerType;
            if (Match(TokenType.BOX))         containerType = "BOX";
            else if (Match(TokenType.SCROLL))  containerType = "SCROLL";
            else
            {
                var raw = ConsumeIdentifier("Expected BOX or SCROLL after AS").Value.ToUpperInvariant();
                containerType = raw is "BOX" or "SCROLL" ? raw : "BOX";
            }

            Consume(TokenType.LPAREN, "Expected '(' after container type");

            string? containerStyleName = null;
            string? title = null, subtitle = null;
            bool titleMd = false, subtitleMd = false;
            TooltipDefinition? tooltip = null;
            string? structure = null;
            var slotMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var styles  = new Dictionary<string, string>();
            bool isCollapsible = false, isPinnable = true;
            string? icon = null;


            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (Match(TokenType.STYLE))
                {
                    ParseStyleClause(styles, ref containerStyleName);
                }
                else if (Match(TokenType.TITLE))
                {
                    (title, titleMd) = ParseVisualPropertyWithMd("TITLE");
                }
                else if (Match(TokenType.SUBTITLE))
                {
                    (subtitle, subtitleMd) = ParseVisualPropertyWithMd("SUBTITLE");
                }
                else if (Match(TokenType.TOOLTIP))
                {
                    tooltip = ParseTooltipDefinition();
                }
                else if (Match(TokenType.STRUCTURE))
                {
                    Consume(TokenType.EQUALS, "Expected '=' after STRUCTURE");
                    structure = Consume(TokenType.STRING_LITERAL, "Expected string literal for STRUCTURE").Value;
                }
                else if (Match(TokenType.MAP))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after MAP");
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        var slot   = Consume(TokenType.STRING_LITERAL, "Expected slot letter (e.g. 'A')").Value;
                        Consume(TokenType.EQUALS, "Expected '=' in MAP entry");
                        var visual = ConsumeIdentifier("Expected visual or container name after '='").Value;
                        slotMap[slot] = visual;
                        Match(TokenType.COMMA);
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close MAP");
                }
                else if (Match(TokenType.COLLAPSIBLE))
                {
                    Consume(TokenType.EQUALS, "Expected '=' after COLLAPSIBLE");
                    isCollapsible = ParseOnOffValue();
                }
                else if (Match(TokenType.ICON))
                {
                    Consume(TokenType.EQUALS, "Expected '=' after ICON");
                    icon = Consume(TokenType.STRING_LITERAL, "Expected string literal for ICON").Value;
                }
                else if (Match(TokenType.PINNABLE))
                {
                    Consume(TokenType.EQUALS, "Expected '=' after PINNABLE");
                    isPinnable = ParseOnOffValue();
                }
                else
                {
                    throw new SyntaxException(
                        $"Unexpected token '{_parser.Current.Value}' in CREATE CONTAINER body",
                        _parser.Current.Line, _parser.Current.Column);
                }
                Match(TokenType.COMMA);
            }

            Consume(TokenType.RPAREN, "Expected ')' to close CREATE CONTAINER");
            Match(TokenType.SEMICOLON);

            return new CreateContainerStatement
            {
                Name               = name,
                ContainerType      = containerType,
                Structure          = structure,
                SlotMap            = slotMap,
                Styles             = styles,
                StyleName          = containerStyleName,
                Title              = title,
                TitleIsMarkdown     = titleMd,
                Subtitle           = subtitle,
                SubtitleIsMarkdown  = subtitleMd,
                Tooltip            = tooltip,
                IsCollapsible      = isCollapsible,
                Icon               = icon,
                IsPinnable         = isPinnable,
                Mode               = mode,
                Line               = startToken.Line,
                Column             = startToken.Column
            };

        }

        // ── CREATE NAVIGATION ─────────────────────────────────────────────────

        public Statement ParseCreateNavigation(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
        {
            var name = ConsumeIdentifier("Expected navigation name after CREATE NAVIGATION").Value;
            Consume(TokenType.AS, "Expected AS after navigation name");

            NavigationType navType;
            if (Match(TokenType.NAV_TAB))       navType = NavigationType.Tab;
            else if (Match(TokenType.BUTTON))   navType = NavigationType.Button;
            else if (Match(TokenType.LINK_NAV)) navType = NavigationType.Link;
            else
            {
                var raw = ConsumeIdentifier("Expected TAB, BUTTON, or LINK after AS").Value.ToUpperInvariant();
                navType = raw switch
                {
                    "TAB"    => NavigationType.Tab,
                    "BUTTON" => NavigationType.Button,
                    "LINK"   => NavigationType.Link,
                    _        => NavigationType.Tab
                };
            }

            Consume(TokenType.LPAREN, "Expected '(' after navigation type");

            var orientation = NavigationOrientation.Horizontal;
            string? defaultPage = null;
            var pages = new List<string>();

            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (_parser.Current.Type == TokenType.IDENTIFIER &&
                    _parser.Current.Value.Equals("ORIENTATION", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    Consume(TokenType.EQUALS, "Expected '=' after ORIENTATION");
                    var oriVal = ConsumeIdentifier("Expected HORIZONTAL or VERTICAL").Value.ToUpperInvariant();
                    orientation = oriVal == "VERTICAL" ? NavigationOrientation.Vertical : NavigationOrientation.Horizontal;
                }
                else if (_parser.Current.Type == TokenType.IDENTIFIER &&
                         _parser.Current.Value.Equals("DEFAULT", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    Consume(TokenType.EQUALS, "Expected '=' after DEFAULT");
                    defaultPage = ConsumeIdentifier("Expected page name after DEFAULT =").Value;
                }
                else if (_parser.Current.Type == TokenType.DEFAULT)
                {
                    Advance();
                    Consume(TokenType.EQUALS, "Expected '=' after DEFAULT");
                    defaultPage = ConsumeIdentifier("Expected page name after DEFAULT =").Value;
                }
                else if (Match(TokenType.PAGES) || (_parser.Current.Type == TokenType.IDENTIFIER && _parser.Current.Value.Equals("PAGES", StringComparison.OrdinalIgnoreCase)))
                {
                    if (_parser.Current.Type != TokenType.LPAREN) Match(TokenType.PAGES); // handle identifier vs token
                    Consume(TokenType.LPAREN, "Expected '(' after PAGES");
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        pages.Add(ConsumeIdentifier("Expected page name").Value);
                        Match(TokenType.COMMA);
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close PAGES");
                }
                else
                {
                    throw new SyntaxException(
                        $"Unexpected token '{_parser.Current.Value}' in CREATE NAVIGATION body",
                        _parser.Current.Line, _parser.Current.Column);
                }
                Match(TokenType.COMMA);
            }

            Consume(TokenType.RPAREN, "Expected ')' to close CREATE NAVIGATION options");

            if (pages.Count == 0)
            {
                bool hasPagesClause = Match(TokenType.WITH) || ReportCheck(TokenType.PAGES);
                if (hasPagesClause)
                {
                    Match(TokenType.PAGES);
                    Consume(TokenType.LPAREN, "Expected '(' after PAGES");
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        pages.Add(ConsumeIdentifier("Expected page name").Value);
                        Match(TokenType.COMMA);
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close PAGES");
                }
            }

            Match(TokenType.SEMICOLON);

            return new CreateNavigationStatement
            {
                Name        = name,
                NavType     = navType,
                Orientation = orientation,
                DefaultPage = defaultPage,
                Pages       = pages,
                Mode        = mode,
                Line        = startToken.Line,
                Column      = startToken.Column
            };
        }

        // ── CREATE BUTTON ─────────────────────────────────────────────────────

        public Statement ParseCreateButton(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
        {
            var name = ConsumeIdentifier("Expected button name after CREATE BUTTON").Value;
            Consume(TokenType.AS, "Expected AS after button name");

            string buttonType;
            if (Match(TokenType.BACK))         buttonType = "BACK";
            else if (Match(TokenType.REFRESH)) buttonType = "REFRESH";
            else if (Match(TokenType.CLEAR_FILTERS)) buttonType = "CLEAR_FILTERS";
            else if (_parser.Current.Type != TokenType.LPAREN && _parser.Current.Type != TokenType.EOF)
                buttonType = _parser.Advance().Value.ToUpperInvariant(); // accept any keyword as custom button type
            else throw new SyntaxException("Expected button type (BACK, REFRESH, or custom identifier) after AS", _parser.Current.Line, _parser.Current.Column);

            Consume(TokenType.LPAREN, "Expected '(' after button type");

            string? title = null;
            TooltipDefinition? tooltip = null;
            string? styleName = null;
            var options = new List<VisualOption>();
            var actions = new List<VisualAction>();
            var styles  = new Dictionary<string, string>();

            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (Match(TokenType.TITLE))
                {
                    Match(TokenType.EQUALS);
                    title = Consume(TokenType.STRING_LITERAL, "Expected button title string").Value;
                }
                else if (Match(TokenType.TOOLTIP))
                {
                    tooltip = ParseTooltipDefinition();
                }
                else if (Match(TokenType.OPTIONS))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after OPTIONS");
                    ParseOptions(options, new List<AxisOptions>());
                    Consume(TokenType.RPAREN, "Expected ')' to close OPTIONS");
                }
                else if (Match(TokenType.ACTIONS))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after ACTIONS");
                    actions.AddRange(ParseActions());
                    Consume(TokenType.RPAREN, "Expected ')' to close ACTIONS");
                }
                else if (Match(TokenType.STYLE))
                {
                    ParseStyleClause(styles, ref styleName);
                }
                else
                {
                    throw new SyntaxException($"Unexpected token '{_parser.Current.Value}' in CREATE BUTTON body", _parser.Current.Line, _parser.Current.Column);
                }
                Match(TokenType.COMMA);
            }

            Consume(TokenType.RPAREN, "Expected ')' to close CREATE BUTTON");
            Match(TokenType.SEMICOLON);

            return new CreateButtonStatement
            {
                Name       = name,
                ButtonType = buttonType,
                Title      = title,
                Tooltip    = tooltip,
                Options    = options,
                Actions    = actions,
                Styles     = styles,
                StyleName  = styleName,
                Mode       = mode,
                Line       = startToken.Line,
                Column     = startToken.Column
            };
        }

        // ── ALTER (Report Objects) ────────────────────────────────────────────

        public Statement ParseAlterReportObject(ReportObjectType type)
        {
            var startToken = _parser.Previous;
            var name = ConsumeIdentifier($"Expected {type} name after ALTER {type}").Value;
            Consume(TokenType.LPAREN, $"Expected '(' after {type} name");

            VisualSourceExpression? source = null;
            var mappings    = new List<VisualMapping>();
            var options     = new List<VisualOption>();
            var axisOptions = new List<AxisOptions>();
            var actions     = new List<VisualAction>();
            var styles      = new Dictionary<string, string>();
            string? styleName = null;
            string? title = null, subtitle = null;
            bool titleMd = false, subtitleMd = false;
            TooltipDefinition? tooltip = null;

            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (Match(TokenType.SOURCE))
                {
                    Match(TokenType.EQUALS);
                    source = ParseVisualSource();
                }
                else if (Match(TokenType.TITLE))
                {
                    Match(TokenType.EQUALS);
                    title = Consume(TokenType.STRING_LITERAL, "Expected title string").Value;
                }
                else if (Match(TokenType.SUBTITLE))
                {
                    Match(TokenType.EQUALS);
                    subtitle = Consume(TokenType.STRING_LITERAL, "Expected subtitle string").Value;
                }
                else if (Match(TokenType.TOOLTIP))
                {
                    tooltip = ParseTooltipDefinition();
                }
                else if (Match(TokenType.MAPPINGS))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after MAPPINGS");
                    mappings.AddRange(ParseMappings());
                    Consume(TokenType.RPAREN, "Expected ')' to close MAPPINGS");
                }
                else if (Match(TokenType.OPTIONS))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after OPTIONS");
                    ParseOptions(options, axisOptions);
                    Consume(TokenType.RPAREN, "Expected ')' to close OPTIONS");
                }
                else if (Match(TokenType.ACTIONS))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after ACTIONS");
                    actions.AddRange(ParseActions());
                    Consume(TokenType.RPAREN, "Expected ')' to close ACTIONS");
                }
                else if (Match(TokenType.STYLE))
                {
                    ParseStyleClause(styles, ref styleName);
                }
                else
                {
                    throw new SyntaxException($"Unexpected token '{_parser.Current.Value}' in ALTER {type} body", _parser.Current.Line, _parser.Current.Column);
                }
                Match(TokenType.COMMA);
            }

            Consume(TokenType.RPAREN, $"Expected ')' to close ALTER {type}");
            Match(TokenType.SEMICOLON);

            return new AlterReportObjectStatement
            {
                ObjectType         = type,
                Name               = name,
                Source             = source,
                Mappings           = mappings.Count > 0 ? mappings : null,
                Options            = options.Count > 0 ? options : null,
                AxisOptions        = axisOptions.Count > 0 ? axisOptions : null,
                Actions            = actions.Count > 0 ? actions : null,
                Styles             = styles.Count > 0 ? styles : null,
                StyleName          = styleName,
                Title              = title,
                TitleIsMarkdown     = titleMd,
                Subtitle           = subtitle,
                SubtitleIsMarkdown  = subtitleMd,
                Tooltip            = tooltip,
                Line               = startToken.Line,
                Column             = startToken.Column
            };
        }

        // ── Private helpers ───────────────────────────────────────────────────

        private VisualType ParseVisualType()
        {
            if (Match(TokenType.BAR))          return VisualType.Bar;
            if (Match(TokenType.LINE))         return VisualType.Line;
            if (Match(TokenType.SCATTER))      return VisualType.Scatter;
            if (Match(TokenType.PIE))          return VisualType.Pie;
            if (Match(TokenType.TABLE_VISUAL)) return VisualType.Table;
            if (Match(TokenType.TABLE))        return VisualType.Table;
            if (Match(TokenType.CARD))         return VisualType.Card;
            if (Match(TokenType.SLICER))       return VisualType.Slicer;
            if (Match(TokenType.HEATMAP))      return VisualType.HeatMap;
            if (Match(TokenType.DONUT))        return VisualType.Donut;
            if (Match(TokenType.HBAR))         return VisualType.HorizontalBar;
            if (Match(TokenType.BOXPLOT))      return VisualType.BoxPlot;
            if (Match(TokenType.TREEMAP))      return VisualType.Treemap;
            if (Match(TokenType.TEXT))         return VisualType.Text;
            if (Match(TokenType.COMBO))        return VisualType.Combo;
            if (Match(TokenType.DATEPICKER))    return VisualType.DatePicker;
            if (Match(TokenType.RELDATEPICKER)) return VisualType.RelDatePicker;
            if (Match(TokenType.SLIDER))       return VisualType.Slider;
            if (Match(TokenType.MULTISELECT))  return VisualType.MultiSelect;
            if (Match(TokenType.SEARCH))       return VisualType.Search;
            if (Match(TokenType.GAUGE))        return VisualType.Gauge;
            if (Match(TokenType.FUNNEL))       return VisualType.Funnel;
            if (Match(TokenType.WATERFALL))    return VisualType.Waterfall;
            if (Match(TokenType.IMAGE))        return VisualType.Image;
            if (Match(TokenType.BUBBLE))       return VisualType.Bubble;
            if (Match(TokenType.RADAR))        return VisualType.Radar;
            if (Match(TokenType.CANDLESTICK))  return VisualType.Candlestick;
            if (Match(TokenType.CHECKBOX))     return VisualType.Checkbox;
            if (Match(TokenType.TEXTBOX))      return VisualType.Textbox;
            if (Match(TokenType.NUMBERBOX))    return VisualType.Numberbox;

            // MAP token already exists for container MAP() clauses; match it here only when
            // ParseVisualType() is called (i.e. after AS in CREATE VISUAL ... AS MAP).
            if (Match(TokenType.MAP))          return VisualType.Map;

            if (_parser.Current.Type == TokenType.IDENTIFIER)
            {
                var val = _parser.Current.Value.ToUpperInvariant();
                Advance();
                return val switch
                {
                    "BAR"          => VisualType.Bar,
                    "LINE"         => VisualType.Line,
                    "SCATTER"      => VisualType.Scatter,
                    "PIE"          => VisualType.Pie,
                    "TABLE"        => VisualType.Table,
                    "CARD"         => VisualType.Card,
                    "SLICER"       => VisualType.Slicer,
                    "HEATMAP"      => VisualType.HeatMap,
                    "DONUT"        => VisualType.Donut,
                    "HBAR"         => VisualType.HorizontalBar,
                    "BOXPLOT"      => VisualType.BoxPlot,
                    "TREEMAP"      => VisualType.Treemap,
                    "TEXT"         => VisualType.Text,
                    "COMBO"        => VisualType.Combo,
                    "DATEPICKER"    => VisualType.DatePicker,
                    "RELDATEPICKER" => VisualType.RelDatePicker,
                    "SLIDER"       => VisualType.Slider,
                    "MULTISELECT"  => VisualType.MultiSelect,
                    "SEARCH"       => VisualType.Search,
                    "GAUGE"        => VisualType.Gauge,
                    "FUNNEL"       => VisualType.Funnel,
                    "WATERFALL"    => VisualType.Waterfall,
                    "BUBBLE"       => VisualType.Bubble,
                    "RADAR"        => VisualType.Radar,
                    "CANDLESTICK"  => VisualType.Candlestick,
                    "MAP"          => VisualType.Map,
                    "CHECKBOX"     => VisualType.Checkbox,
                    "TEXTBOX"      => VisualType.Textbox,
                    "NUMBERBOX"    => VisualType.Numberbox,
                    _ => throw new SyntaxException(
                             $"Unknown visual type '{val}'.",
                             _parser.Previous.Line, _parser.Previous.Column)
                };
            }

            throw new SyntaxException(
                $"Expected visual type (BAR, LINE, SCATTER, PIE, TABLE, CARD, SLICER, HEATMAP, DONUT, HBAR, BOXPLOT, TREEMAP, TEXT, COMBO, DATEPICKER, RELDATEPICKER, SLIDER, MULTISELECT, SEARCH, GAUGE, FUNNEL, WATERFALL, BUBBLE, RADAR, CANDLESTICK, MAP, CHECKBOX, TEXTBOX, NUMBERBOX) but got '{_parser.Current.Value}'",
                _parser.Current.Line, _parser.Current.Column);
        }

        private VisualSourceExpression ParseVisualSource()
        {
            if (Match(TokenType.LPAREN))
            {
                var query = _parser.ParseStatement();
                Consume(TokenType.RPAREN, "Expected ')' to close SOURCE subquery");
                return new VisualSourceExpression { InlineSelect = query };
            }

            if (_parser.Current.Type == TokenType.SELECT)
            {
                var query = _parser.ParseStatement();
                return new VisualSourceExpression { InlineSelect = query };
            }

            if (Match(TokenType.STRING_LITERAL))
            {
                var val = _parser.Previous.Value;
                if (val.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
                {
                    var subLexer  = new Lexer(val);
                    var subParser = new Parser(subLexer.Tokenize(), val);
                    var subScript = subParser.Parse();
                    if (subScript.Statements.Count > 0 && subScript.Statements[0] is SelectStatement sel)
                        return new VisualSourceExpression { InlineSelect = sel };
                }
                return new VisualSourceExpression { TempTableName = val };
            }

            var tableRef = ConsumeIdentifier("Expected #datasetName or SELECT or ( SELECT ... ) after SOURCE").Value;
            return new VisualSourceExpression { TempTableName = tableRef };
        }

        private (string? Value, bool IsMarkdown) ParseVisualPropertyWithMd(string propertyName)
        {
            Match(TokenType.EQUALS);
            string? value;
            bool isMarkdown = false;
            if (Match(TokenType.LPAREN))
            {
                isMarkdown = true;
                if (Match(TokenType.VARIABLE))
                {
                    value = _parser.Previous.Value;
                }
                else
                {
                    if (propertyName == "DEFAULT")
                    {
                        value = ConsumeReportOptionValue();
                    }
                    else if (Match(TokenType.NUMBER))
                    {
                        value = _parser.Previous.Value;
                    }
                    else
                    {
                        value = Consume(TokenType.STRING_LITERAL, $"Expected string literal or variable for {propertyName}").Value;
                    }
                }
                Consume(TokenType.RPAREN, $"Expected ')' after {propertyName}");
            }
            else
            {
                if (Match(TokenType.VARIABLE))
                {
                    value = _parser.Previous.Value;
                }
                else
                {
                    if (propertyName == "DEFAULT")
                    {
                        value = ConsumeReportOptionValue();
                    }
                    else if (Match(TokenType.NUMBER))
                    {
                        value = _parser.Previous.Value;
                    }
                    else
                    {
                        value = Consume(TokenType.STRING_LITERAL, $"Expected string literal or variable for {propertyName}").Value;
                    }
                }
            }
            return (value, isMarkdown);
        }

        private string? ParseVisualProperty(string propertyName) => ParseVisualPropertyWithMd(propertyName).Value;

        private TooltipDefinition ParseTooltipDefinition()
        {
            // Optional EQUALS. If followed by LPAREN, it's the markdown/visuals block.
            bool hadEquals = Match(TokenType.EQUALS);

            if (ReportCheck(TokenType.LPAREN))
            {
                Advance();
                string? markdown = null;
                var visuals = new List<string>();

                if (ReportCheck(TokenType.STRING_LITERAL))
                {
                    markdown = Advance().Value;
                    Match(TokenType.COMMA);
                }

                if (_parser.Current.Value.Equals("VISUALS", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    Consume(TokenType.LPAREN, "Expected '(' after VISUALS in TOOLTIP");
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        visuals.Add(ConsumeIdentifier("Expected visual name in TOOLTIP VISUALS").Value);
                        Match(TokenType.COMMA);
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close TOOLTIP VISUALS");
                }

                Consume(TokenType.RPAREN, "Expected ')' to close TOOLTIP inline block");
                return TooltipDefinition.Inline(markdown, visuals);
            }

            if (ReportCheck(TokenType.STRING_LITERAL))
                return TooltipDefinition.Text(Advance().Value);

            return TooltipDefinition.Container(
                ConsumeIdentifier("Expected string, container name, or '(' for TOOLTIP").Value);
        }

        private IEnumerable<VisualMapping> ParseMappings()
        {
            var result = new List<VisualMapping>();
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                var roleToken = _parser.Current;
                if (roleToken.Type == TokenType.RPAREN) break;
                Advance();
                var role = roleToken.Value.ToUpperInvariant();

                Consume(TokenType.EQUALS, $"Expected '=' after mapping role '{role}'");
                var column = ConsumeIdentifier("Expected column name after '='").Value;
                result.Add(new VisualMapping { Role = role, Column = column });
                Match(TokenType.COMMA);
            }
            return result;
        }

        private void ParseOptions(List<VisualOption> options, List<AxisOptions> axisOptions)
        {
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                bool isX = _parser.Current.Type == TokenType.X_AXIS || (_parser.Current.Type == TokenType.IDENTIFIER && _parser.Current.Value.Equals("X_AXIS", StringComparison.OrdinalIgnoreCase));
                bool isY = _parser.Current.Type == TokenType.Y_AXIS || (_parser.Current.Type == TokenType.IDENTIFIER && _parser.Current.Value.Equals("Y_AXIS", StringComparison.OrdinalIgnoreCase));

                if (isX)
                {
                    Advance();
                    var axisOpts = new AxisOptions { Axis = "X" };
                    Consume(TokenType.LPAREN, "Expected '(' after X_AXIS");
                    ParseAxisOptionBody(axisOpts.Options);
                    Consume(TokenType.RPAREN, "Expected ')' to close X_AXIS");
                    axisOptions.Add(axisOpts);
                }
                else if (isY)
                {
                    Advance();
                    var axisOpts = new AxisOptions { Axis = "Y" };
                    Consume(TokenType.LPAREN, "Expected '(' after Y_AXIS");
                    ParseAxisOptionBody(axisOpts.Options);
                    Consume(TokenType.RPAREN, "Expected ')' to close Y_AXIS");
                    axisOptions.Add(axisOpts);
                }
                else if (Match(TokenType.COLORS))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after COLORS");
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        var colorKey = _parser.Current.Type == TokenType.STRING_LITERAL
                            ? Advance().Value
                            : ConsumeIdentifier("Expected color key in COLORS").Value;
                        Consume(TokenType.EQUALS, "Expected '=' after color key");
                        var colorVal = ConsumeReportOptionValue();
                        var finalKey = colorKey.StartsWith("color:", StringComparison.OrdinalIgnoreCase)
                            ? colorKey
                            : "color:" + colorKey;
                        options.Add(new VisualOption { Key = finalKey, Value = colorVal });
                        Match(TokenType.COMMA);
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close COLORS");
                }
                else if (Match(TokenType.DATA_LABELS))
                {
                    Match(TokenType.EQUALS);
                    var val = ConsumeReportOptionValue();
                    options.Add(new VisualOption { Key = "DATA_LABELS", Value = NormalizeBoolOptionValue(val) });
                    if (Match(TokenType.WITH))
                    {
                        Consume(TokenType.LPAREN, "Expected '(' after WITH in DATA_LABELS");
                        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                        {
                            var subKey = _parser.Advance().Value.ToUpperInvariant();
                            Match(TokenType.EQUALS);
                            // Standard numeric/string values for sub-options
                            var subVal = ConsumeReportOptionValue();
                            options.Add(new VisualOption { Key = "DATA_LABELS:" + subKey, Value = subVal });
                            Match(TokenType.COMMA);
                        }
                        Consume(TokenType.RPAREN, "Expected ')' to close DATA_LABELS WITH block");
                    }
                }
                else if (Match(TokenType.GRID))
                {
                    Match(TokenType.EQUALS);
                    string val;
                    if (Match(TokenType.LPAREN))
                    {
                        var vals = new List<string>();
                        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                        {
                            vals.Add(ConsumeReportOptionValue().ToUpperInvariant());
                            Match(TokenType.COMMA);
                        }
                        Consume(TokenType.RPAREN, "Expected ')' to close GRID list");
                        val = string.Join(",", vals);
                    }
                    else
                    {
                        val = ConsumeReportOptionValue().ToUpperInvariant();
                    }
                    options.Add(new VisualOption { Key = "GRID", Value = val });
                }
                else
                {
                    // Accept any token as an option key — reserved keywords like STEP and DEFAULT
                    // are valid option names inside an OPTIONS() block.
                    if (_parser.Current.Type == TokenType.RPAREN || _parser.Current.Type == TokenType.EOF)
                        break;
                    var keyToken = _parser.Advance();
                    var key = keyToken.Value.ToUpperInvariant();
                    Match(TokenType.EQUALS);
                    var val = ParseExpression();
                    options.Add(new VisualOption { Key = key, Value = val is LiteralExpression lit ? lit.Value?.ToString() ?? "" : val.ToSql() });
                }
                Match(TokenType.COMMA);
            }
        }

        private void ParseAxisOptionBody(List<VisualOption> opts)
        {
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (_parser.Current.Type == TokenType.RPAREN || _parser.Current.Type == TokenType.EOF)
                    break;
                var keyToken = _parser.Advance();
                var key = keyToken.Value.ToUpperInvariant();
                Match(TokenType.EQUALS);
                var val = ParseExpression();
                opts.Add(new VisualOption { Key = key, Value = val is LiteralExpression lit ? lit.Value?.ToString() ?? "" : val.ToSql() });
                Match(TokenType.COMMA);
            }
        }

        private static readonly HashSet<TokenType> _overlayKeywordTokens = new()
        {
            TokenType.LINEAR, TokenType.EXPONENTIAL, TokenType.LOGARITHMIC,
            TokenType.POLYNOMIAL, TokenType.POWER, TokenType.GOAL, TokenType.AVERAGE,
            TokenType.MOVING_AVG, TokenType.SOLID, TokenType.DASHED, TokenType.DOTTED,
            TokenType.OVERLAYS, TokenType.COLOR,
        };

        private string ConsumeReportOptionValue()
        {
            var t = _parser.Current.Type;
            if (t == TokenType.STRING_LITERAL   || t == TokenType.NUMBER  ||
                t == TokenType.IDENTIFIER || t == TokenType.TRUE  || t == TokenType.FALSE ||
                t == TokenType.ON       || t == TokenType.OFF     ||
                t == TokenType.TOP      || t == TokenType.BOTTOM  ||
                t == TokenType.LEFT     || t == TokenType.RIGHT   ||
                t == TokenType.GRID     || t == TokenType.DATA_LABELS ||
                t == TokenType.NONE     || t == TokenType.HEADER || t == TokenType.FOOTER ||
                t == TokenType.ALL      ||
                t == TokenType.CENTER   || t == TokenType.FONT_SIZE ||
                t == TokenType.INSIDE   || t == TokenType.INSIDE_TOP || t == TokenType.INSIDE_BOTTOM ||
                t == TokenType.INSIDE_LEFT || t == TokenType.INSIDE_RIGHT ||
                t == TokenType.INSIDE_TOP_LEFT || t == TokenType.INSIDE_TOP_RIGHT ||
                t == TokenType.INSIDE_BOTTOM_LEFT || t == TokenType.INSIDE_BOTTOM_RIGHT ||
                t == TokenType.DATA_LABELS_POSITION || t == TokenType.FONT_FAMILY ||
                t == TokenType.FONT_WEIGHT || t == TokenType.GAUGE_STYLE ||
                t == TokenType.SHOW_NO_DATA_PLACEHOLDER ||
                _overlayKeywordTokens.Contains(t))
            {
                var value = _parser.Current.Value;
                Advance();
                return value;
            }
            throw new SyntaxException(
                $"Expected option value but got '{_parser.Current.Value}'",
                _parser.Current.Line, _parser.Current.Column);
        }

        private static string NormalizeBoolOptionValue(string val) =>
            val.ToUpperInvariant() switch
            {
                "TRUE" or "ON" or "1"   => "ON",
                "FALSE" or "OFF" or "0" => "OFF",
                _                       => val
            };

        private bool ParseOnOffValue()
        {
            var value = _parser.Current.Value;
            Advance();
            return value.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
                   value == "1";
        }

        private IEnumerable<VisualAction> ParseActions()
        {
            var result = new List<VisualAction>();
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                string trigger;
                if (Match(TokenType.ON_CLICK))       trigger = "ON_CLICK";
                else if (Match(TokenType.ON_CHANGE)) trigger = "ON_CHANGE";
                else throw new SyntaxException(
                    $"Expected ON_CLICK or ON_CHANGE in ACTIONS but got '{_parser.Current.Value}'",
                    _parser.Current.Line, _parser.Current.Column);

                Consume(TokenType.EQUALS, $"Expected '=' after {trigger}");

                VisualAction action;
                if (Match(TokenType.DRILL_DOWN))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after DRILL_DOWN");
                    Advance();
                    Consume(TokenType.EQUALS, "Expected '=' after Target");
                    var target = ConsumeIdentifier("Expected target visual name").Value;
                    Match(TokenType.COMMA);
                    Advance();
                    Consume(TokenType.EQUALS, "Expected '=' after Key");
                    string[] keys;
                    if (Match(TokenType.LPAREN))
                    {
                        var keyList = new List<string>();
                        keyList.Add(ConsumeIdentifier("Expected key column name").Value);
                        while (Match(TokenType.COMMA))
                            keyList.Add(ConsumeIdentifier("Expected key column name").Value);
                        Consume(TokenType.RPAREN, "Expected ')' to close key list");
                        keys = keyList.ToArray();
                    }
                    else
                    {
                        keys = new[] { ConsumeIdentifier("Expected key column name").Value };
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close DRILL_DOWN");
                    action = new DrillDownAction { Trigger = trigger, TargetVisual = target, KeyColumns = keys };
                }
                else if (Match(TokenType.DRILL_IN))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after DRILL_IN");
                    Advance(); // skip "HIERARCHY" identifier
                    Consume(TokenType.EQUALS, "Expected '=' after HIERARCHY");
                    Consume(TokenType.LPAREN, "Expected '(' to open hierarchy list");
                    var hierarchy = new List<string>();
                    hierarchy.Add(ConsumeIdentifier("Expected column name").Value);
                    while (Match(TokenType.COMMA))
                        hierarchy.Add(ConsumeIdentifier("Expected column name").Value);
                    Consume(TokenType.RPAREN, "Expected ')' to close hierarchy list");
                    Consume(TokenType.RPAREN, "Expected ')' to close DRILL_IN");
                    action = new DrillInAction { Trigger = trigger, Hierarchy = hierarchy.ToArray() };
                }
                else if (Match(TokenType.SET_PARAMETER))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after SET_PARAMETER");
                    var paramName = ConsumeIdentifier("Expected parameter name").Value;
                    if (!paramName.StartsWith("@")) paramName = "@" + paramName;
                    Match(TokenType.COMMA);
                    var valueExpr = ConsumeIdentifier("Expected value expression").Value;
                    Consume(TokenType.RPAREN, "Expected ')' to close SET_PARAMETER");
                    action = new SetParameterAction { Trigger = trigger, ParameterName = paramName, ValueExpression = valueExpr };
                }
                else if (Match(TokenType.RUN_SCRIPT))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after RUN_SCRIPT");
                    var scriptPath = Consume(TokenType.STRING_LITERAL, "Expected script path string").Value;
                    var actionParams = new Dictionary<string, string>();
                    while (Match(TokenType.COMMA))
                    {
                        var pName = ConsumeIdentifier("Expected parameter name").Value;
                        if (!pName.StartsWith("@")) pName = "@" + pName;
                        Consume(TokenType.EQUALS, "Expected '=' after parameter name");
                        var pVal = ConsumeIdentifier("Expected column name or expression").Value;
                        actionParams[pName] = pVal;
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close RUN_SCRIPT");
                    action = new RunScriptAction { Trigger = trigger, ScriptPath = scriptPath, Parameters = actionParams };
                }
                else if (Match(TokenType.CLEAR_FILTERS))
                {
                    action = new ClearFiltersAction { Trigger = trigger };
                }
                else
                {
                    throw new SyntaxException(
                        $"Expected DRILL_DOWN, DRILL_IN, SET_PARAMETER, or CLEAR_FILTERS after {trigger} =",
                        _parser.Current.Line, _parser.Current.Column);
                }

                result.Add(action);
                Match(TokenType.COMMA);
            }
            return result;
        }

        private IEnumerable<TypedSeries> ParseTypedSeries()
        {
            var result = new List<TypedSeries>();
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                string seriesType;
                if (Match(TokenType.BAR))       seriesType = "bar";
                else if (Match(TokenType.LINE)) seriesType = "line";
                else
                {
                    var raw = ConsumeIdentifier("Expected BAR or LINE in SERIES").Value;
                    seriesType = raw.ToLowerInvariant();
                }

                var column = ConsumeIdentifier("Expected column name after series type").Value;
                result.Add(new TypedSeries { SeriesType = seriesType, Column = column });
                Match(TokenType.COMMA);
            }
            return result;
        }

        private IEnumerable<FormattingRule> ParseFormattingRules()
        {
            var result = new List<FormattingRule>();
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                var condition = _parser.ParseExpression();
                Consume(TokenType.THEN, "Expected THEN after formatting condition");
                var color = Consume(TokenType.STRING_LITERAL, "Expected color string after THEN").Value;
                result.Add(new FormattingRule { Condition = condition, Color = color });
                Match(TokenType.COMMA);
            }
            return result;
        }

        private IEnumerable<VisualOverlay> ParseOverlays()
        {
            var result = new List<VisualOverlay>();
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                OverlayType overlayType;
                double?     parameter = null;

                if (Match(TokenType.GOAL))
                {
                    overlayType = OverlayType.Goal;
                    Consume(TokenType.LPAREN, "Expected '(' after GOAL");
                    parameter = double.Parse(Consume(TokenType.NUMBER, "Expected numeric value for GOAL").Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                    Consume(TokenType.RPAREN, "Expected ')' after GOAL value");
                }
                else if (Match(TokenType.AVERAGE))     { overlayType = OverlayType.Average; }
                else if (Match(TokenType.MOVING_AVG))
                {
                    overlayType = OverlayType.MovingAvg;
                    Consume(TokenType.LPAREN, "Expected '(' after MOVING_AVG");
                    parameter = double.Parse(Consume(TokenType.NUMBER, "Expected window size for MOVING_AVG").Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                    Consume(TokenType.RPAREN, "Expected ')' after MOVING_AVG window");
                }
                else if (Match(TokenType.LINEAR))      { overlayType = OverlayType.Linear; }
                else if (Match(TokenType.EXPONENTIAL)) { overlayType = OverlayType.Exponential; }
                else if (Match(TokenType.LOGARITHMIC)) { overlayType = OverlayType.Logarithmic; }
                else if (Match(TokenType.POWER))       { overlayType = OverlayType.Power; }
                else if (Match(TokenType.POLYNOMIAL))
                {
                    overlayType = OverlayType.Polynomial;
                    Consume(TokenType.LPAREN, "Expected '(' after POLYNOMIAL");
                    parameter = double.Parse(Consume(TokenType.NUMBER, "Expected degree for POLYNOMIAL").Value,
                        System.Globalization.CultureInfo.InvariantCulture);
                    Consume(TokenType.RPAREN, "Expected ')' after POLYNOMIAL degree");
                }
                else throw new SyntaxException(
                    $"Expected overlay type (GOAL, AVERAGE, MOVING_AVG, LINEAR, ...) but got '{_parser.Current.Value}'",
                    _parser.Current.Line, _parser.Current.Column);

                Consume(TokenType.AS, "Expected AS after overlay type");
                OverlayLineStyle lineStyle;
                if      (Match(TokenType.SOLID))  lineStyle = OverlayLineStyle.Solid;
                else if (Match(TokenType.DASHED)) lineStyle = OverlayLineStyle.Dashed;
                else if (Match(TokenType.DOTTED)) lineStyle = OverlayLineStyle.Dotted;
                else throw new SyntaxException(
                    $"Expected SOLID, DASHED, or DOTTED after AS in OVERLAYS but got '{_parser.Current.Value}'",
                    _parser.Current.Line, _parser.Current.Column);

                string? color = null;
                string? label = null;
                if (Match(TokenType.WITH))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after WITH in overlay");
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        if (Match(TokenType.COLOR))
                        {
                            Consume(TokenType.EQUALS, "Expected = after COLOR");
                            color = Consume(TokenType.STRING_LITERAL, "Expected color string").Value;
                        }
                        else if (_parser.Current.Type == TokenType.IDENTIFIER && _parser.Current.Value.Equals("LABEL", StringComparison.OrdinalIgnoreCase))
                        {
                            Advance();
                            Consume(TokenType.EQUALS, "Expected = after LABEL");
                            label = Consume(TokenType.STRING_LITERAL, "Expected label string").Value;
                        }
                        else break;
                        Match(TokenType.COMMA);
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close WITH");
                }

                result.Add(new VisualOverlay
                {
                    OverlayType = overlayType,
                    Parameter   = parameter,
                    LineStyle   = lineStyle,
                    Color       = color,
                    Label       = label
                });
                Match(TokenType.COMMA);
            }
            return result;
        }

        private (List<TableSummaryItem>, TableSummaryOptions) ParseSummaryClause()
        {
            var summaries = new List<TableSummaryItem>();
            bool grandTotalRow = false, grandTotalCol = false, sumRow = false, sumCol = false;
            List<string>? specificCols = null;

            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (Match(TokenType.GRAND_TOTAL))
                {
                    Match(TokenType.EQUALS);
                    var val = ParseOnOffValue();
                    grandTotalRow = val;
                    grandTotalCol = val;
                }
                else if (Match(TokenType.GRAND_TOTAL_ROW))
                {
                    Match(TokenType.EQUALS);
                    grandTotalRow = ParseOnOffValue();
                }
                else if (Match(TokenType.GRAND_TOTAL_COLUMN))
                {
                    Match(TokenType.EQUALS);
                    grandTotalCol = ParseOnOffValue();
                }
                else if (Match(TokenType.SUMMARIZE_ROW))
                {
                    Match(TokenType.EQUALS);
                    sumRow = ParseOnOffValue();
                }
                else if (Match(TokenType.SUMMARIZE_COLUMN))
                {
                    Match(TokenType.EQUALS);
                    sumCol = ParseOnOffValue();
                    if (ReportCheck(TokenType.LPAREN))
                    {
                        Advance();
                        specificCols = new List<string>();
                        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                        {
                            specificCols.Add(ConsumeIdentifier("Expected column name").Value);
                            Match(TokenType.COMMA);
                        }
                        Consume(TokenType.RPAREN, "Expected ')' to close column list");
                    }
                }
                else if (_parser.Current.Type == TokenType.IDENTIFIER || _parser.Current.Type == TokenType.SUM || _parser.Current.Type == TokenType.AVG || _parser.Current.Type == TokenType.COUNT || _parser.Current.Type == TokenType.MIN || _parser.Current.Type == TokenType.MAX)
                {
                    var agg = Advance().Value.ToUpperInvariant();
                    Consume(TokenType.LPAREN, $"Expected '(' after {agg}");
                    string col;
                    if (_parser.Current.Type == TokenType.STAR)
                    {
                        _parser.Advance();
                        col = "*";
                    }
                    else
                    {
                        col = _parser.Advance().Value; // accept keywords (e.g. Returns) as column names
                    }
                    Consume(TokenType.RPAREN, $"Expected ')' after column in aggregate");
                    string? alias = null;
                    if (Match(TokenType.AS))
                    {
                        // Accept identifiers or quoted string literals as aliases (e.g. AS 'Total Revenue')
                        if (_parser.Current.Type == TokenType.STRING_LITERAL || _parser.IsIdentifier(_parser.Current))
                            alias = _parser.Advance().Value;
                        else
                            alias = ConsumeIdentifier("Expected alias after AS").Value;
                    }
                    summaries.Add(new TableSummaryItem(agg, col, alias));
                }
                else
                {
                    throw new SyntaxException($"Unexpected token '{_parser.Current.Value}' in SUMMARY clause", _parser.Current.Line, _parser.Current.Column);
                }
                Match(TokenType.COMMA);
            }

            return (summaries, new TableSummaryOptions
            {
                GrandTotalRow = grandTotalRow,
                GrandTotalColumn = grandTotalCol,
                SummarizeRow = sumRow,
                SummarizeColumn = sumCol,
                SpecificColumns = specificCols
            });
        }

        private void ParseStyleClause(Dictionary<string, string> styles, ref string? styleName)
        {
            if (Match(TokenType.EQUALS))
                styleName = ConsumeIdentifier("Expected style name after STYLE =").Value;
            else
            {
                Consume(TokenType.LPAREN, "Expected '(' or '=' after STYLE");
                ParseStyleBody(styles);
                Consume(TokenType.RPAREN, "Expected ')' to close STYLE");
            }
        }

        private void ParseStyleBody(Dictionary<string, string> styles)
        {
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                // Accept any token as the start of a style key (keywords like THEME, TRUE, etc.)
                var keyTok = _parser.IsIdentifier(_parser.Current) || LanguageMetadata.IsKeyword(_parser.Current.Value)
                    ? _parser.Advance()
                    : throw new SyntaxException("Expected style key", _parser.Current.Line, _parser.Current.Column);
                var key = keyTok.Value;
                // Consume hyphenated segments: BACKGROUND - COLOR → "BACKGROUND-COLOR"
                while (_parser.Current.Type == TokenType.MINUS &&
                       (_parser.IsIdentifier(_parser.Peek) || LanguageMetadata.IsKeyword(_parser.Peek.Value)))
                {
                    Advance(); // consume '-'
                    key += "-" + _parser.Advance().Value;
                }
                Consume(TokenType.EQUALS, $"Expected '=' after style key '{key}'");
                string val;
                val = _parser.Current.Value;
                Advance();
                styles[key] = val;
                Match(TokenType.COMMA);
            }
        }
    }
}
