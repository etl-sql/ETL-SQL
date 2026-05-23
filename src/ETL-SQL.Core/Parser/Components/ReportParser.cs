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
            Expression? title = null, subtitle = null;
            bool titleMd = false, subtitleMd = false;
            Expression? defaultValue = null;
            string? styleName = null;
            Expression? placeholder = null;
            TooltipDefinition? tooltip = null;
            var mappings        = new List<VisualMapping>();
            var options         = new List<VisualOption>();
            var axisOptions     = new List<AxisOptions>();
            var actions         = new List<VisualAction>();
            var interactions    = new List<VisualInteraction>();
            var styles          = new Dictionary<string, string>();
            var typedSeries     = new List<TypedSeries>();
            var formattingRules = new List<FormattingRule>();
            var overlays        = new List<VisualOverlay>();
            var summaries       = new List<TableSummaryItem>();
            string? labelPosition = null;
            double? min = null, max = null;
            int? decimals = null;
            TableSummaryOptions? summaryOptions = null;
            var fetchMode = VisualFetchMode.Auto;

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
                else if (Match(TokenType.VISIBLE))
                {
                    Match(TokenType.EQUALS);
                    options.Add(new VisualOption { Key = "VISIBLE", Value = ParseOnOffValue() });
                }
                else if (Match(TokenType.FETCH))
                {
                    Match(TokenType.EQUALS);
                    fetchMode = ParseVisualFetchMode();
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
                else if (Match(TokenType.INTERACTIONS))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after INTERACTIONS");
                    interactions.AddRange(ParseInteractions());
                    Consume(TokenType.RPAREN, "Expected ')' to close INTERACTIONS");
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
                    placeholder = ParseVisualProperty("PLACEHOLDER");
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

            ValidateVisualActionTriggers(visualType, actions, startToken);

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
                Interactions    = interactions,
                TypedSeries     = typedSeries,
                FormattingRules = formattingRules,
                Overlays        = overlays,
                Summaries       = summaries,
                SummaryOptions  = summaryOptions,
                Styles          = styles,
                FetchMode       = fetchMode,
                StyleName       = styleName,
                Tooltip         = tooltip,
                Mode            = mode,
                Line            = startToken.Line,
                Column          = startToken.Column
            };
        }

        // ── CREATE PAGE ───────────────────────────────────────────────────────

        private PageMode ParsePageMode()
        {
            if (Match(TokenType.DASHBOARD)) return PageMode.Dashboard;
            if (Match(TokenType.PAGINATED)) return PageMode.Paginated;

            throw new SyntaxException(
                "Expected DASHBOARD or PAGINATED after CREATE PAGE <name> AS.",
                _parser.Current.Line,
                _parser.Current.Column);
        }

        public Statement ParseCreatePage(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
        {
            var name = ConsumeIdentifier("Expected page name").Value;
            Consume(TokenType.AS, "Expected AS after page name");

            var pageMode = ParsePageMode();
            Consume(TokenType.LPAREN, "Expected '(' after page mode");
            
            string? visibility = "ON";
            string? structure = null;
            var slotMap    = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var pageStyles = new Dictionary<string, string>();
            string? pageStyleName = null;
            Expression? title = null, subtitle = null;
            bool titleMd = false, subtitleMd = false;
            TooltipDefinition? tooltip = null;
            int refreshSecs = 0;

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
                else if (IsCurrentValue("GAP"))
                {
                    Advance();
                    Consume(TokenType.EQUALS, "Expected '=' after GAP");
                    pageStyles["GAP"] = ConsumeReportOptionValue();
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
                else if (Match(TokenType.VISIBLE))
                {
                    Match(TokenType.EQUALS);
                    visibility = ParseOnOffValue();
                }
                else if (Match(TokenType.REFRESH))
                {
                    Match(TokenType.EQUALS);
                    int.TryParse(ConsumeReportOptionValue(), out refreshSecs);
                }
                else if (Match(TokenType.LAYOUT))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after LAYOUT");
                    bool isPinnable = true;
                    ParseContainerLayout(ref structure, slotMap, pageStyles, ref isPinnable);
                    Consume(TokenType.RPAREN, "Expected ')' to close LAYOUT");
                }
                else
                {
                    throw new SyntaxException(
                        $"Unexpected token '{_parser.Current.Value}' in CREATE PAGE body",
                        _parser.Current.Line, _parser.Current.Column);
                }
                Match(TokenType.COMMA);
            }
            Consume(TokenType.RPAREN, "Expected ')' to close CREATE PAGE");
            if (Match(TokenType.WITH))
                throw new SyntaxException("CREATE PAGE no longer supports WITH (...). Use REFRESH = <seconds> inside the page body.", _parser.Previous.Line, _parser.Previous.Column);

            Match(TokenType.SEMICOLON);

            if (structure == null)
                throw new SyntaxException($"CREATE PAGE '{name}' is missing a STRUCTURE clause.", startToken.Line, startToken.Column);

            return new CreatePageStatement
            {
                Name            = name,
                PageMode        = pageMode,
                Structure       = structure,
                SlotMap         = slotMap,
                Styles          = pageStyles,
                StyleName       = pageStyleName,
                Title           = title,
                TitleIsMarkdown = titleMd,
                Subtitle        = subtitle,
                SubtitleIsMarkdown = subtitleMd,
                Tooltip         = tooltip,
                Visibility      = visibility,
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
            if (!tableName.StartsWith("&"))
                throw new SyntaxException("CREATE DATASET names must use the &dataset form", startToken.Line, startToken.Column);

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
                    compress = ParseOnOffValue() == "ON";
                }
                else if (Match(TokenType.ENCRYPT))
                {
                    Match(TokenType.EQUALS);
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
                var raw = ConsumeIdentifier("Expected container type after AS").Value.ToUpperInvariant();
                containerType = raw is "BOX" or "SCROLL" or "DRAWER" or "SIDEBAR" or "TABS" or "ACCORDION" or "MODAL" or "POPOVER" or "LAYER"
                    ? raw
                    : throw new SyntaxException(
                        $"Unknown container type '{raw}'. Expected BOX, SCROLL, DRAWER, SIDEBAR, TABS, ACCORDION, MODAL, POPOVER, or LAYER.",
                        _parser.Previous.Line,
                        _parser.Previous.Column);
            }

            Consume(TokenType.LPAREN, "Expected '(' after container type");

            string? containerStyleName = null;
            Expression? title = null, subtitle = null;
            bool titleMd = false, subtitleMd = false;
            TooltipDefinition? tooltip = null;
            string? structure = null;
            var slotMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var styles  = new Dictionary<string, string>();
            bool isCollapsible = containerType == "DRAWER";
            bool isPinnable = true;
            string? visibility = "ON";
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
                else if (Match(TokenType.VISIBLE))
                {
                    Match(TokenType.EQUALS);
                    visibility = ParseOnOffValue();
                }
                else if (Match(TokenType.ICON))
                {
                    Consume(TokenType.EQUALS, "Expected '=' after ICON");
                    icon = Consume(TokenType.STRING_LITERAL, "Expected string literal for ICON").Value;
                }
                else if (Match(TokenType.LAYOUT))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after LAYOUT");
                    ParseContainerLayout(ref structure, slotMap, styles, ref isPinnable);
                    Consume(TokenType.RPAREN, "Expected ')' to close LAYOUT");
                }
                else if (Match(TokenType.OPTIONS))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after OPTIONS");
                    ParseContainerOptions(ref visibility, ref icon);
                    Consume(TokenType.RPAREN, "Expected ')' to close OPTIONS");
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
                Visibility         = visibility,
                Icon               = icon,
                IsPinnable         = isPinnable,
                Mode               = mode,
                Line               = startToken.Line,
                Column             = startToken.Column
            };
        }

        private void ParseContainerLayout(
            ref string? structure,
            Dictionary<string, string> slotMap,
            Dictionary<string, string> styles,
            ref bool isPinnable)
        {
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
                        var slot = Consume(TokenType.STRING_LITERAL, "Expected slot letter (e.g. 'A')").Value;
                        Consume(TokenType.EQUALS, "Expected '=' in MAP entry");
                        var visual = ConsumeIdentifier("Expected visual or container name after '='").Value;
                        slotMap[slot] = visual;
                        Match(TokenType.COMMA);
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close MAP");
                }
                else if (Match(TokenType.PINNABLE))
                {
                    Consume(TokenType.EQUALS, "Expected '=' after PINNABLE");
                    isPinnable = ParseOnOffValue() == "ON";
                }
                else
                {
                    var key = ConsumeIdentifier("Expected layout option name").Value.ToUpperInvariant();
                    Match(TokenType.EQUALS);
                    styles[key] = ConsumeReportOptionValue();
                }
                Match(TokenType.COMMA);
            }
        }

        private void ParseContainerOptions(ref string? visibility, ref string? icon)
        {
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (Match(TokenType.VISIBLE))
                {
                    throw new SyntaxException("CREATE CONTAINER VISIBLE is now a top-level clause. Use VISIBLE = ON|OFF outside OPTIONS (...).", _parser.Previous.Line, _parser.Previous.Column);
                }
                else if (Match(TokenType.ICON))
                {
                    throw new SyntaxException("CREATE CONTAINER ICON is now a top-level clause. Use ICON = 'name' outside OPTIONS (...).", _parser.Previous.Line, _parser.Previous.Column);
                }
                else
                {
                    throw new SyntaxException(
                        $"Unexpected token '{_parser.Current.Value}' in CREATE CONTAINER OPTIONS",
                        _parser.Current.Line,
                        _parser.Current.Column);
                }
            }
        }

        private bool IsCurrentValue(string value) =>
            string.Equals(_parser.Current.Value, value, StringComparison.OrdinalIgnoreCase);

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

            if (Match(TokenType.WITH) || ReportCheck(TokenType.PAGES))
            {
                throw new SyntaxException(
                    "Expected end of CREATE NAVIGATION after ')'.",
                    _parser.Current.Line,
                    _parser.Current.Column);
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

            const string buttonType = "BUTTON";
            Consume(TokenType.LPAREN, "Expected '(' after AS. Put behavior in ACTIONS (ON_CLICK = ...).");

            Expression? title      = null;
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
                    title = ParseExpression();
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

            ValidateButtonActionTriggers(actions, startToken);

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
            Expression? title = null, subtitle = null;
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
                    title = ParseExpression();
                }
                else if (Match(TokenType.SUBTITLE))
                {
                    Match(TokenType.EQUALS);
                    subtitle = ParseExpression();
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
            if (Match(TokenType.GANTT))        return VisualType.Gantt;
            if (Match(TokenType.SANKEY))       return VisualType.Sankey;
            if (Match(TokenType.SUNBURST))     return VisualType.Sunburst;
            if (Match(TokenType.NETWORK))      return VisualType.Network;
            if (Match(TokenType.TRELLIS))      return VisualType.Trellis;
            if (Match(TokenType.MATRIX))       return VisualType.Matrix;
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
                    "GANTT"        => VisualType.Gantt,
                    "SANKEY"       => VisualType.Sankey,
                    "SUNBURST"     => VisualType.Sunburst,
                    "NETWORK"      => VisualType.Network,
                    "TRELLIS"      => VisualType.Trellis,
                    "MATRIX"       => VisualType.Matrix,
                    "CHECKBOX"     => VisualType.Checkbox,
                    "TEXTBOX"      => VisualType.Textbox,
                    "NUMBERBOX"    => VisualType.Numberbox,
                    _ => throw new SyntaxException(
                             $"Unknown visual type '{val}'.",
                             _parser.Previous.Line, _parser.Previous.Column)
                };
            }

            throw new SyntaxException(
                $"Expected visual type (BAR, LINE, SCATTER, PIE, TABLE, CARD, SLICER, HEATMAP, DONUT, HBAR, BOXPLOT, TREEMAP, TEXT, COMBO, DATEPICKER, RELDATEPICKER, SLIDER, MULTISELECT, SEARCH, GAUGE, FUNNEL, WATERFALL, BUBBLE, RADAR, CANDLESTICK, MAP, GANTT, SANKEY, SUNBURST, NETWORK, TRELLIS, MATRIX, CHECKBOX, TEXTBOX, NUMBERBOX) but got '{_parser.Current.Value}'",
                _parser.Current.Line, _parser.Current.Column);
        }

        private static void ValidateVisualActionTriggers(VisualType visualType, List<VisualAction> actions, Token startToken)
        {
            if (actions.Count == 0)
                return;

            if (IsPassiveVisual(visualType))
            {
                throw new SyntaxException(
                    $"{visualType.ToString().ToUpperInvariant()} visuals do not support ACTIONS. Use a BUTTON for clickable behavior.",
                    startToken.Line,
                    startToken.Column);
            }

            var expectedTrigger = IsControlVisual(visualType) ? "ON_CHANGE" : "ON_CLICK";
            foreach (var action in actions)
            {
                if (!string.Equals(action.Trigger, expectedTrigger, StringComparison.OrdinalIgnoreCase))
                {
                    throw new SyntaxException(
                        $"{visualType.ToString().ToUpperInvariant()} visuals only support ACTIONS ({expectedTrigger} = ...).",
                        startToken.Line,
                        startToken.Column);
                }
            }
        }

        private static void ValidateButtonActionTriggers(List<VisualAction> actions, Token startToken)
        {
            foreach (var action in actions)
            {
                if (!string.Equals(action.Trigger, "ON_CLICK", StringComparison.OrdinalIgnoreCase))
                {
                    throw new SyntaxException(
                        "BUTTON actions only support ACTIONS (ON_CLICK = ...).",
                        startToken.Line,
                        startToken.Column);
                }
            }
        }

        private static bool IsControlVisual(VisualType visualType) => visualType is
            VisualType.Slicer
            or VisualType.DatePicker
            or VisualType.RelDatePicker
            or VisualType.Slider
            or VisualType.MultiSelect
            or VisualType.Search
            or VisualType.Checkbox
            or VisualType.Textbox
            or VisualType.Numberbox;

        private static bool IsPassiveVisual(VisualType visualType) => visualType is
            VisualType.Text
            or VisualType.Image;

        private VisualFetchMode ParseVisualFetchMode()
        {
            var raw = ConsumeIdentifier("Expected AUTO, ON_LOAD, or ON_RUN after FETCH =").Value.ToUpperInvariant();
            return raw switch
            {
                "AUTO" => VisualFetchMode.Auto,
                "ON_LOAD" => VisualFetchMode.OnLoad,
                "ON_RUN" => VisualFetchMode.OnRun,
                _ => throw new SyntaxException(
                    $"Unknown FETCH mode '{raw}'. Expected AUTO, ON_LOAD, or ON_RUN.",
                    _parser.Previous.Line,
                    _parser.Previous.Column)
            };
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

        private (Expression? Value, bool IsMarkdown) ParseVisualPropertyWithMd(string propertyName)
        {
            Match(TokenType.EQUALS);
            bool isMarkdown = false;
            if (Match(TokenType.LPAREN))
            {
                isMarkdown = true;
                var expr = _parser.ParseExpression();
                Consume(TokenType.RPAREN, $"Expected ')' after {propertyName}");
                return (expr, isMarkdown);
            }
            return (_parser.ParseExpression(), false);
        }

        private Expression? ParseVisualProperty(string propertyName) => ParseVisualPropertyWithMd(propertyName).Value;

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
                return TooltipDefinition.Text(ParseExpression());

            return TooltipDefinition.Container(
                ConsumeIdentifier("Expected string, container name, or '(' for TOOLTIP").Value);
        }

        private VisualMapping ParseSparklineMapping()
        {
            Advance(); // consume SPARKLINE
            Consume(TokenType.LPAREN, "Expected '(' after SPARKLINE");
            var cols = new List<string>();
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                cols.Add(ConsumeIdentifier("Expected column name in SPARKLINE").Value);
                Match(TokenType.COMMA);
            }
            Consume(TokenType.RPAREN, "Expected ')' after SPARKLINE columns");

            string sparklineType = "line";
            if (_parser.Current.Type == TokenType.LINE)
            { sparklineType = "line"; Advance(); }
            else if (_parser.Current.Type == TokenType.IDENTIFIER)
            {
                var t = _parser.Current.Value.ToUpperInvariant();
                if (t is "LINE" or "BAR" or "AREA") { sparklineType = t.ToLower(); Advance(); }
            }

            string? displayName = null;
            if (Match(TokenType.AS))
            {
                displayName = _parser.Current.Type == TokenType.STRING_LITERAL
                    ? Advance().Value
                    : ConsumeIdentifier("Expected alias after AS").Value;
            }

            return new VisualMapping
            {
                Role            = "SPARKLINE",
                Column          = "$sparkline",
                SparklineColumns = cols,
                SparklineType   = sparklineType,
                DisplayName     = displayName
            };
        }

        private IEnumerable<VisualMapping> ParseMappings()
        {
            var result = new List<VisualMapping>();
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                // SPARKLINE(col1, col2, ...) [LINE|BAR|AREA] [AS 'alias']
                if (_parser.Current.Type == TokenType.SPARKLINE)
                {
                    result.Add(ParseSparklineMapping());
                    Match(TokenType.COMMA);
                    continue;
                }

                var nameToken = _parser.Current;
                if (nameToken.Type == TokenType.RPAREN) break;
                Advance();
                var name = nameToken.Value;

                if (_parser.Current.Type == TokenType.EQUALS)
                {
                    // ROLE = column syntax (MATRIX, charts, etc.)
                    Advance();
                    var column = ConsumeIdentifier($"Expected column name after '=' for role '{name}'").Value;
                    result.Add(new VisualMapping { Role = name.ToUpperInvariant(), Column = column });
                }
                else
                {
                    // TABLE column syntax: col_name [FORMAT 'fmt'] [ALIGN 'dir'] [DATA_BAR [COLOR 'c']]
                    //                                [COLOR_SCALE FROM 'c1' TO 'c2']
                    //                                [IMAGE [WIDTH n] | HYPERLINK [LABEL 'text']]
                    //                                [AS 'alias']
                    string? format = null, align = null, displayName = null;
                    string? dataBarColor = null, colorScaleFrom = null, colorScaleTo = null;
                    string? cellRenderer = null, hyperlinkLabel = null;
                    int? imageWidth = null;
                    bool dataBar = false;
                    while (!ReportCheck(TokenType.RPAREN) && !ReportCheck(TokenType.COMMA) && !ReportAtEnd())
                    {
                        if (_parser.Current.Type == TokenType.FORMAT ||
                            (_parser.Current.Type == TokenType.IDENTIFIER &&
                             _parser.Current.Value.Equals("FORMAT", StringComparison.OrdinalIgnoreCase)))
                        {
                            Advance();
                            format = Consume(TokenType.STRING_LITERAL, "Expected format string after FORMAT").Value;
                        }
                        else if (_parser.Current.Type == TokenType.IDENTIFIER &&
                                 _parser.Current.Value.Equals("ALIGN", StringComparison.OrdinalIgnoreCase))
                        {
                            Advance();
                            align = Consume(TokenType.STRING_LITERAL, "Expected alignment value after ALIGN").Value;
                        }
                        else if (_parser.Current.Type == TokenType.DATA_BAR)
                        {
                            Advance();
                            dataBar = true;
                            if (_parser.Current.Type == TokenType.COLOR)
                            {
                                Advance();
                                dataBarColor = Consume(TokenType.STRING_LITERAL, "Expected color string after DATA_BAR COLOR").Value;
                            }
                        }
                        else if (_parser.Current.Type == TokenType.COLOR_SCALE)
                        {
                            Advance();
                            if (_parser.Current.Type == TokenType.FROM)
                            {
                                Advance();
                                colorScaleFrom = Consume(TokenType.STRING_LITERAL, "Expected start color after FROM").Value;
                            }
                            if (_parser.Current.Type == TokenType.TO)
                            {
                                Advance();
                                colorScaleTo = Consume(TokenType.STRING_LITERAL, "Expected end color after TO").Value;
                            }
                        }
                        else if (_parser.Current.Type == TokenType.IMAGE)
                        {
                            Advance();
                            cellRenderer = "image";
                            if (_parser.Current.Type == TokenType.IDENTIFIER &&
                                _parser.Current.Value.Equals("WIDTH", StringComparison.OrdinalIgnoreCase))
                            {
                                Advance();
                                if (_parser.Current.Type == TokenType.NUMBER &&
                                    int.TryParse(_parser.Current.Value, out var w))
                                { imageWidth = w; Advance(); }
                            }
                        }
                        else if (_parser.Current.Type == TokenType.HYPERLINK)
                        {
                            Advance();
                            cellRenderer = "hyperlink";
                            if (_parser.Current.Type == TokenType.IDENTIFIER &&
                                _parser.Current.Value.Equals("LABEL", StringComparison.OrdinalIgnoreCase))
                            {
                                Advance();
                                hyperlinkLabel = Consume(TokenType.STRING_LITERAL, "Expected label string after LABEL").Value;
                            }
                        }
                        else if (Match(TokenType.AS))
                        {
                            displayName = _parser.Current.Type == TokenType.STRING_LITERAL
                                ? Advance().Value
                                : ConsumeIdentifier("Expected alias after AS").Value;
                        }
                        else break;
                    }
                    result.Add(new VisualMapping
                    {
                        Role          = name.ToUpperInvariant(),
                        Column        = name,
                        Format        = format,
                        Align         = align,
                        DisplayName   = displayName,
                        DataBar       = dataBar,
                        DataBarColor  = dataBarColor,
                        ColorScaleFrom = colorScaleFrom,
                        ColorScaleTo  = colorScaleTo,
                        CellRenderer  = cellRenderer,
                        ImageWidth    = imageWidth,
                        HyperlinkLabel = hyperlinkLabel
                    });
                }
                Match(TokenType.COMMA);
            }
            return result;
        }

        private IEnumerable<VisualInteraction> ParseInteractions()
        {
            var result = new List<VisualInteraction>();
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                var key = ConsumeIdentifier("Expected interaction key").Value.ToUpperInvariant();
                Consume(TokenType.EQUALS, $"Expected '=' after interaction key '{key}'");
                var value = ConsumeReportOptionValue().ToUpperInvariant();
                result.Add(new VisualInteraction { Key = key, Value = value });
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
                    if (key is "CROSS_VISUAL_ACTION" or "CROSS_FILTER")
                    {
                        throw new SyntaxException(
                            $"Unexpected option '{key}'. Use INTERACTIONS (ON_SELECT = HIGHLIGHT|FILTER|NONE) for cross-visual behavior.",
                            keyToken.Line,
                            keyToken.Column);
                    }
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
            if (_parser.IsIdentifier(_parser.Current) ||
                t == TokenType.STRING_LITERAL || t == TokenType.NUMBER  ||
                t == TokenType.TRUE           || t == TokenType.FALSE   ||
                t == TokenType.ON             || t == TokenType.OFF     ||
                t == TokenType.FILTER         ||
                t == TokenType.TOP            || t == TokenType.BOTTOM  ||
                t == TokenType.LEFT           || t == TokenType.RIGHT   ||
                t == TokenType.GRID           || t == TokenType.DATA_LABELS ||
                t == TokenType.NONE           || t == TokenType.HEADER  || t == TokenType.FOOTER ||
                t == TokenType.ALL            ||
                t == TokenType.CENTER         || t == TokenType.FONT_SIZE ||
                t == TokenType.INSIDE         || t == TokenType.INSIDE_TOP || t == TokenType.INSIDE_BOTTOM ||
                t == TokenType.INSIDE_LEFT    || t == TokenType.INSIDE_RIGHT ||
                t == TokenType.INSIDE_TOP_LEFT || t == TokenType.INSIDE_TOP_RIGHT ||
                t == TokenType.INSIDE_BOTTOM_LEFT || t == TokenType.INSIDE_BOTTOM_RIGHT ||
                t == TokenType.DATA_LABELS_POSITION || t == TokenType.FONT_FAMILY ||
                t == TokenType.FONT_WEIGHT    || t == TokenType.GAUGE_STYLE ||
                t == TokenType.SHOW_NO_DATA_PLACEHOLDER ||
                t == TokenType.VISIBLE        ||
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

        private string ParseOnOffValue()
        {
            if (ReportCheck(TokenType.VARIABLE)) return _parser.Advance().Value;
            var value = _parser.Current.Value;
            _parser.Advance();
            bool isOn = value.Equals("ON", StringComparison.OrdinalIgnoreCase) ||
                        value.Equals("TRUE", StringComparison.OrdinalIgnoreCase) ||
                        value == "1";
            return isOn ? "ON" : "OFF";
        }

        private Token ConsumeIdentifierOrVariable(string message)
        {
            if (ReportCheck(TokenType.IDENTIFIER) || ReportCheck(TokenType.VARIABLE) || ReportCheck(TokenType.VALUE)) return _parser.Advance();
            throw new SyntaxException(message, _parser.Current.Line, _parser.Current.Column);
        }

        // Accepts column names (including DAY/MONTH/YEAR etc.), variables, and string literals for action value expressions.
        private Token ConsumeValueExpr(string message)
        {
            if (ReportCheck(TokenType.STRING_LITERAL) || ReportCheck(TokenType.VARIABLE))
                return _parser.Advance();
            return ConsumeIdentifier(message); // handles identifiers and keyword-as-column (DAY, MONTH, etc.)
        }

        private Token ConsumeIdentifierOrString(string message)
        {
            if (ReportCheck(TokenType.IDENTIFIER) || ReportCheck(TokenType.STRING_LITERAL) || 
                ReportCheck(TokenType.VARIABLE) || ReportCheck(TokenType.VALUE) ||
                ReportCheck(TokenType.ON) || ReportCheck(TokenType.OFF)) return _parser.Advance();
            throw new SyntaxException(message, _parser.Current.Line, _parser.Current.Column);
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

                if (Match(TokenType.LPAREN))
                {
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        var action = ParseSingleAction(trigger);
                        if (action != null) result.Add(action);
                        Match(TokenType.COMMA);
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close action list");
                }
                else
                {
                    var action = ParseSingleAction(trigger);
                    if (action != null) result.Add(action);
                }

                Match(TokenType.COMMA);
            }

            return result;
        }

        private VisualAction? ParseSingleAction(string trigger)
        {
            VisualAction? action = null;
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
                    var paramName = ConsumeIdentifierOrVariable("Expected parameter name").Value;
                    if (!paramName.StartsWith("@")) paramName = "@" + paramName;
                    Match(TokenType.COMMA);
                    var valueExpr = ConsumeValueExpr("Expected value expression").Value;
                    Consume(TokenType.RPAREN, "Expected ')' to close SET_PARAMETER");
                    action = new SetParameterAction { Trigger = trigger, ParameterName = paramName, ValueExpression = valueExpr };
                }
                else if (Match(TokenType.RUN_SCRIPT))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after RUN_SCRIPT");
                    var scriptPath = ConsumeReportOptionValue();
                    var actionParams = new Dictionary<string, string>();
                    while (Match(TokenType.COMMA))
                    {
                        var pName = ConsumeIdentifierOrVariable("Expected parameter name").Value;
                        if (!pName.StartsWith("@")) pName = "@" + pName;
                        Consume(TokenType.EQUALS, "Expected '=' after parameter name");
                        var pVal = ConsumeValueExpr("Expected column name or expression").Value;
                        actionParams[pName] = pVal;
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close RUN_SCRIPT");
                    action = new RunScriptAction { Trigger = trigger, ScriptPath = scriptPath, Parameters = actionParams };
                }
                else if (Match(TokenType.DRILL_REPORT))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after DRILL_REPORT");
                    Advance(); // skip "FILE" identifier
                    Consume(TokenType.EQUALS, "Expected '=' after FILE");
                    var targetReport = ConsumeReportOptionValue();
                    var actionParams = new Dictionary<string, string>();
                    if (Match(TokenType.COMMA))
                    {
                        Advance(); // skip "PARAMETERS"
                        Consume(TokenType.LPAREN, "Expected '(' to open parameter list");
                        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                        {
                            var pName = ConsumeIdentifierOrVariable("Expected parameter name").Value;
                            if (!pName.StartsWith("@")) pName = "@" + pName;
                            Consume(TokenType.EQUALS, "Expected '=' after parameter name");
                            var pVal = ConsumeValueExpr("Expected column name or expression").Value;
                            actionParams[pName] = pVal;
                            Match(TokenType.COMMA);
                        }
                        Consume(TokenType.RPAREN, "Expected ')' to close parameter list");
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close DRILL_REPORT");
                    action = new DrillReportAction { Trigger = trigger, TargetReport = targetReport, Parameters = actionParams };
                }
                else if (Match(TokenType.CLEAR_FILTERS))
                {
                    action = new ClearFiltersAction { Trigger = trigger };
                }
                else if (Match(TokenType.APPLY_PARAMETERS))
                {
                    action = new ApplyParametersAction { Trigger = trigger };
                }
                else if (TryParseReportCommandAction(trigger, out var commandAction))
                {
                    action = commandAction;
                }
                else if (Match(TokenType.NAVIGATE_PAGE))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after NAVIGATE_PAGE");
                    var targetPage = ConsumeIdentifierOrString("Expected target page name").Value;
                    Consume(TokenType.RPAREN, "Expected ')' to close NAVIGATE_PAGE");
                    action = new NavigatePageAction { Trigger = trigger, TargetPage = targetPage };
                }
                else if (Match(TokenType.REFRESH_VISUALS))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after REFRESH_VISUALS");
                    var targets = new List<string>();
                    do
                    {
                        targets.Add(ConsumeIdentifierOrString("Expected visual name").Value);
                    }
                    while (Match(TokenType.COMMA));
                    Consume(TokenType.RPAREN, "Expected ')' to close REFRESH_VISUALS");
                    action = new RefreshVisualsAction { Trigger = trigger, Targets = targets };
                }
                else if (Match(TokenType.SET_UI_STATE))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after SET_UI_STATE");
                    var targets = new List<string>();
                    if (Match(TokenType.LPAREN))
                    {
                        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                        {
                            targets.Add(ConsumeIdentifierOrString("Expected target name or TAG:name").Value);
                            Match(TokenType.COMMA);
                        }
                        Consume(TokenType.RPAREN, "Expected ')' to close target list");
                    }
                    else
                    {
                        targets.Add(ConsumeIdentifierOrString("Expected target name or TAG:name").Value);
                    }

                    Consume(TokenType.COMMA, "Expected ',' after targets");
                    var key = ConsumeIdentifierOrString("Expected state key (e.g. VISIBLE)").Value;
                    Consume(TokenType.COMMA, "Expected ',' after key");
                    var val = ConsumeIdentifierOrString("Expected state value (e.g. ON)").Value;
                    Consume(TokenType.RPAREN, "Expected ')' to close SET_UI_STATE");

                    action = new SetUiStateAction
                    {
                        Trigger = trigger,
                        Targets = targets,
                        Key = key,
                        Value = val
                    };
                }
                else
                {
                    throw new SyntaxException(
                        $"Expected DRILL_DOWN, DRILL_IN, SET_PARAMETER, CLEAR_FILTERS, APPLY_PARAMETERS, BACK, REFRESH_REPORT, REFRESH_VISUALS, EXPORT_CSV, EXPORT_EXCEL, EXPORT_PDF, NAVIGATE_PAGE, or SET_UI_STATE after {trigger} =",
                        _parser.Current.Line, _parser.Current.Column);
                }

                return action;
        }

        private bool TryParseReportCommandAction(string trigger, out ReportCommandAction? action)
        {
            action = null;
            var token = _parser.Current;
            var command = token.Value.ToUpperInvariant();
            var manifestType = command switch
            {
                "BACK" => "BACK",
                "REFRESH_REPORT" => "REFRESH",
                "EXPORT_CSV" => "EXPORT_CSV",
                "EXPORT_EXCEL" => "EXPORT_EXCEL",
                "EXPORT_PDF" => "EXPORT_PDF",
                _ => null
            };

            if (manifestType == null)
                return false;

            Advance();
            action = new ReportCommandAction { Trigger = trigger, Command = manifestType };
            return true;
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
                Match(TokenType.WHEN);
                var condition = _parser.ParseExpression();
                Consume(TokenType.THEN, "Expected THEN after formatting condition");
                var color = Consume(TokenType.STRING_LITERAL, "Expected color string after THEN").Value;
                string? fontColor = null;
                if (_parser.Current.Type == TokenType.FONT_COLOR)
                {
                    Advance();
                    fontColor = Consume(TokenType.STRING_LITERAL, "Expected color string after FONT_COLOR").Value;
                }
                result.Add(new FormattingRule { Condition = condition, Color = color, FontColor = fontColor });
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
                    grandTotalRow = val == "ON";
                    grandTotalCol = val == "ON";
                }
                else if (Match(TokenType.GRAND_TOTAL_ROW))
                {
                    Match(TokenType.EQUALS);
                    grandTotalRow = ParseOnOffValue() == "ON";
                }
                else if (Match(TokenType.GRAND_TOTAL_COLUMN))
                {
                    Match(TokenType.EQUALS);
                    grandTotalCol = ParseOnOffValue() == "ON";
                }
                else if (Match(TokenType.SUMMARIZE_ROW))
                {
                    Match(TokenType.EQUALS);
                    sumRow = ParseOnOffValue() == "ON";
                }
                else if (Match(TokenType.SUMMARIZE_COLUMN))
                {
                    Match(TokenType.EQUALS);
                    sumCol = ParseOnOffValue() == "ON";
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
