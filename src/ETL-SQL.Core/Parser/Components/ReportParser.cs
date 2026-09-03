using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser.Components;

public class ReportParser : ParserComponent
{
    public ReportParser(IParser parser, StatementParser parent) : base(parser, parent) { }

    private bool ReportCheck(TokenType t) => _parser.Current.Type == t;
    private bool ReportAtEnd() => _parser.Current.Type == TokenType.EOF;

    // ── CREATE VISUAL ─────────────────────────────────────────────────────

    public Statement ParseCreateVisual(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
    {
        var name = ConsumeIdentifier("Expected visual name after CREATE VISUAL").Value;
        Consume(TokenType.AS, "Expected AS after visual name");
        var visualType = ParseVisualType();
        Consume(TokenType.LPAREN, "Expected '(' after visual type");

        VisualSourceExpression? source = null;
        Expression? title = null, subtitle = null;
        bool titleMd = false, subtitleMd = false;
        TitleDefinition? titleDef = null, subtitleDef = null;
        Expression? defaultValue = null;
        string? styleName = null;
        Expression? placeholder = null;
        TooltipDefinition? tooltip = null;
        var mappings = new List<VisualMapping>();
        var options = new List<VisualOption>();
        var axisOptions = new List<AxisOptions>();
        var actions = new List<VisualAction>();
        var interactions = new List<VisualInteraction>();
        var styles = new Dictionary<string, string>();
        var typedSeries = new List<TypedSeries>();
        var formattingRules = new List<FormattingRule>();
        var overlays = new List<VisualOverlay>();
        var summaries = new List<TableSummaryItem>();
        string? labelPosition = null;
        double? min = null, max = null;
        int? decimals = null;
        TableSummaryOptions? summaryOptions = null;
        var fetchMode = VisualFetchMode.Auto;
        PrintLayoutOverride? printLayout = null;
        RowDetailDefinition? rowDetail = null;
        CascadeDefinition? cascade = null;
        AdvancedChartDefinition? advancedChart = null;
        string? htmlTemplate = null;
        string? htmlCss = null;
        string? htmlFallback = null;
        HtmlVisualMode htmlMode = HtmlVisualMode.Single;
        var palette = ImmutableArray<string>.Empty;

        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            if (Match(TokenType.SOURCE))
            {
                Match(TokenType.EQUALS);
                source = ParseVisualSource();
            }
            else if (Match(TokenType.TITLE))
            {
                (title, titleMd, titleDef) = ParseVisualPropertyWithMd("TITLE");
            }
            else if (Match(TokenType.SUBTITLE))
            {
                (subtitle, subtitleMd, subtitleDef) = ParseVisualPropertyWithMd("SUBTITLE");
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
                ParseStyleClause(styles, ref styleName, ref palette);
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
            else if (IsCurrentValue("PRINT_LAYOUT"))
            {
                Advance();
                printLayout = ParsePrintLayoutOverride();
            }
            else if (IsCurrentValue("ROW_DETAIL"))
            {
                Advance();
                rowDetail = ParseRowDetailDefinition();
            }
            else if (IsCurrentValue("CASCADE"))
            {
                Advance();
                cascade = ParseCascadeDefinition();
            }
            else if (IsCurrentValue("CHART"))
            {
                Advance();
                advancedChart = ParseAdvancedChartDefinition();
            }
            else if (Match(TokenType.TEMPLATE))
            {
                Match(TokenType.EQUALS);
                htmlTemplate = Consume(TokenType.STRING_LITERAL, "Expected template string after TEMPLATE =").Value;
            }
            else if (IsCurrentValue("MODE"))
            {
                Advance();
                Match(TokenType.EQUALS);
                var modeVal = ConsumeReportOptionValue().ToUpperInvariant();
                htmlMode = modeVal switch
                {
                    "SINGLE" => HtmlVisualMode.Single,
                    "REPEATER" => HtmlVisualMode.Repeater,
                    _ => throw new SyntaxException(
                        $"Unknown HTML visual MODE '{modeVal}'. Expected SINGLE or REPEATER.",
                        _parser.Previous.Line, _parser.Previous.Column)
                };
            }
            else if (IsCurrentValue("FALLBACK"))
            {
                Advance();
                Match(TokenType.EQUALS);
                htmlFallback = Consume(TokenType.STRING_LITERAL, "Expected fallback string after FALLBACK =").Value;
            }
            else
            {
                throw new SyntaxException(
                    $"Unexpected token '{_parser.Current.Value}' inside CREATE VISUAL body. Type: {_parser.Current.Type}, MatchRowDetail: {IsCurrentValue("ROW_DETAIL")}",
                    _parser.Current.Line, _parser.Current.Column);
            }
            Match(TokenType.COMMA);
        }

        Consume(TokenType.RPAREN, "Expected ')' to close CREATE VISUAL");
        Match(TokenType.SEMICOLON);

        ValidateVisualActionTriggers(visualType, actions, startToken);

        if (visualType == VisualType.Custom && advancedChart == null)
            throw new SyntaxException($"CUSTOM visual '{name}' requires a CHART clause.", startToken.Line, startToken.Column);
        if (visualType != VisualType.Custom && advancedChart != null)
            throw new SyntaxException("CHART is only valid on CUSTOM visuals.", startToken.Line, startToken.Column);
        if (visualType == VisualType.Custom && (mappings.Count > 0 || typedSeries.Count > 0 || overlays.Count > 0 || formattingRules.Count > 0))
            throw new SyntaxException("CUSTOM visuals use CHART encodings and cannot use MAPPINGS, SERIES, OVERLAYS, or FORMATTING.", startToken.Line, startToken.Column);

        HtmlTemplateDefinition? htmlTemplateDef = null;
        if (visualType == VisualType.Html)
        {
            if (htmlTemplate == null)
                throw new SyntaxException($"HTML visual '{name}' requires a TEMPLATE clause.", startToken.Line, startToken.Column);
            if (mappings.Count > 0 || typedSeries.Count > 0 || overlays.Count > 0 || formattingRules.Count > 0)
                throw new SyntaxException("HTML visuals cannot use MAPPINGS, SERIES, OVERLAYS, or FORMATTING.", startToken.Line, startToken.Column);
            if (advancedChart != null)
                throw new SyntaxException("HTML visuals cannot use the CHART clause.", startToken.Line, startToken.Column);
            if (cascade != null)
                throw new SyntaxException("HTML visuals cannot use CASCADE.", startToken.Line, startToken.Column);
            if (htmlMode == HtmlVisualMode.Repeater && source == null)
                throw new SyntaxException($"HTML visual '{name}' with MODE = REPEATER requires a SOURCE clause.", startToken.Line, startToken.Column);

            styles.TryGetValue("CSS", out htmlCss);
            if (htmlCss != null) styles.Remove("CSS");

            htmlTemplateDef = new HtmlTemplateDefinition
            {
                Template = htmlTemplate,
                Css = htmlCss,
                Fallback = htmlFallback,
                Mode = htmlMode
            };
        }
        else
        {
            if (htmlTemplate != null)
                throw new SyntaxException("TEMPLATE is only valid on HTML visuals.", startToken.Line, startToken.Column);
        }

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
                || visualType == VisualType.Image
                || visualType == VisualType.Html)
                source = new VisualSourceExpression();
            else
                throw new SyntaxException($"CREATE VISUAL '{name}' is missing a SOURCE clause.", startToken.Line, startToken.Column);
        }

        return new CreateVisualStatement
        {
            Name = name,
            VisualType = visualType,
            Title = title,
            TitleIsMarkdown = titleMd,
            TitleDefinition = titleDef,
            Subtitle = subtitle,
            SubtitleIsMarkdown = subtitleMd,
            SubtitleDefinition = subtitleDef,
            DefaultValue = defaultValue,
            LabelPosition = labelPosition,
            Min = min,
            Max = max,
            Decimals = decimals,
            Placeholder = placeholder,
            Source = source,
            Mappings = mappings,
            Options = options,
            AxisOptions = axisOptions,
            Actions = actions,
            Interactions = interactions,
            TypedSeries = typedSeries,
            FormattingRules = formattingRules,
            Overlays = overlays,
            Summaries = summaries,
            SummaryOptions = summaryOptions,
            Styles = styles,
            Palette = palette,
            FetchMode = fetchMode,
            StyleName = styleName,
            Tooltip = tooltip,
            Mode = mode,
            PrintLayout = printLayout,
            RowDetail = rowDetail,
            Cascade = cascade,
            AdvancedChart = advancedChart,
            HtmlTemplate = htmlTemplateDef,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    private CascadeDefinition ParseCascadeDefinition()
    {
        Consume(TokenType.LPAREN, "Expected '(' after CASCADE");
        CascadeMode? mode = null;
        var parents = new List<CascadeParentBinding>();
        var invalid = CascadeInvalidSelectionPolicy.Clear;
        var nullSelection = CascadeNullSelectionPolicy.All;
        var allValue = "*";
        var multiSelect = CascadeMultiSelectPolicy.Any;

        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            if (ReportCheck(TokenType.COMMA) || ReportCheck(TokenType.RPAREN) || ReportAtEnd())
                throw new SyntaxException("Expected CASCADE option.", _parser.Current.Line, _parser.Current.Column);
            var option = Advance().Value.ToUpperInvariant();
            if (option == "PARENTS")
            {
                Consume(TokenType.LPAREN, "Expected '(' after CASCADE PARENTS");
                while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                {
                    var parameter = ConsumeIdentifierOrVariable("Expected parent parameter").Value;
                    if (!parameter.StartsWith('@')) parameter = "@" + parameter;
                    Consume(TokenType.EQUALS, "Expected '=' between parent parameter and source column");
                    var column = ConsumeIdentifier("Expected source column for parent parameter").Value;
                    parents.Add(new CascadeParentBinding(parameter, column));
                    if (!Match(TokenType.COMMA)) break;
                }
                Consume(TokenType.RPAREN, "Expected ')' after CASCADE PARENTS");
            }
            else
            {
                Consume(TokenType.EQUALS, $"Expected '=' after CASCADE option '{option}'");
                if (ReportCheck(TokenType.COMMA) || ReportCheck(TokenType.RPAREN) || ReportAtEnd())
                    throw new SyntaxException($"Expected value for CASCADE option '{option}'.", _parser.Current.Line, _parser.Current.Column);
                var value = Advance().Value;
                switch (option)
                {
                    case "MODE":
                        mode = value.ToUpperInvariant() switch
                        {
                            "LOCAL" => CascadeMode.Local,
                            "LIVE" => CascadeMode.Live,
                            _ => throw new SyntaxException("CASCADE MODE must be LOCAL or LIVE.", _parser.Current.Line, _parser.Current.Column)
                        };
                        break;
                    case "INVALID":
                        invalid = value.ToUpperInvariant() switch
                        {
                            "CLEAR" => CascadeInvalidSelectionPolicy.Clear,
                            "FIRST" => CascadeInvalidSelectionPolicy.First,
                            "ERROR" => CascadeInvalidSelectionPolicy.Error,
                            _ => throw new SyntaxException("CASCADE INVALID must be CLEAR, FIRST, or ERROR.", _parser.Current.Line, _parser.Current.Column)
                        };
                        break;
                    case "NULL":
                        nullSelection = value.ToUpperInvariant() switch
                        {
                            "ALL" => CascadeNullSelectionPolicy.All,
                            "MATCH" => CascadeNullSelectionPolicy.Match,
                            _ => throw new SyntaxException("CASCADE NULL must be ALL or MATCH.", _parser.Current.Line, _parser.Current.Column)
                        };
                        break;
                    case "ALL_VALUE": allValue = value; break;
                    case "MULTISELECT":
                        multiSelect = value.ToUpperInvariant() switch
                        {
                            "ANY" => CascadeMultiSelectPolicy.Any,
                            "ALL" => CascadeMultiSelectPolicy.All,
                            _ => throw new SyntaxException("CASCADE MULTISELECT must be ANY or ALL.", _parser.Current.Line, _parser.Current.Column)
                        };
                        break;
                    default:
                        throw new SyntaxException($"Unknown CASCADE option '{option}'.", _parser.Current.Line, _parser.Current.Column);
                }
            }
            Match(TokenType.COMMA);
        }

        Consume(TokenType.RPAREN, "Expected ')' to close CASCADE");
        if (!mode.HasValue)
            throw new SyntaxException("CASCADE requires MODE = LOCAL or MODE = LIVE.", _parser.Current.Line, _parser.Current.Column);
        if (mode == CascadeMode.Local && parents.Count == 0)
            throw new SyntaxException("CASCADE MODE = LOCAL requires PARENTS mappings.", _parser.Current.Line, _parser.Current.Column);
        if (mode == CascadeMode.Live && parents.Count > 0)
            throw new SyntaxException("CASCADE PARENTS is only valid with MODE = LOCAL.", _parser.Current.Line, _parser.Current.Column);

        return new CascadeDefinition
        {
            Mode = mode.Value,
            Parents = parents,
            InvalidSelection = invalid,
            NullSelection = nullSelection,
            AllValue = allValue,
            MultiSelect = multiSelect
        };
    }

    private AdvancedChartDefinition ParseAdvancedChartDefinition()
    {
        var chartStart = _parser.Previous;
        Consume(TokenType.LPAREN, "Expected '(' after CHART");
        AdvancedChartCoordinate? coordinate = null;
        var scales = new List<AdvancedChartScale>();
        var encodings = new List<AdvancedChartEncoding>();
        var layers = new List<AdvancedChartLayer>();
        AdvancedChartFacet? facet = null;
        var resolution = new AdvancedChartResolution();

        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            if (IsCurrentValue("COORDINATE"))
            {
                Advance();
                coordinate = ParseAdvancedChartCoordinate();
            }
            else if (IsCurrentValue("SCALES"))
            {
                Advance();
                scales.AddRange(ParseAdvancedChartScales());
            }
            else if (IsCurrentValue("ENCODINGS"))
            {
                Advance();
                encodings.AddRange(ParseAdvancedChartEncodings());
            }
            else if (IsCurrentValue("LAYERS"))
            {
                Advance();
                layers.AddRange(ParseAdvancedChartLayers());
            }
            else if (IsCurrentValue("FACET"))
            {
                Advance();
                facet = ParseAdvancedChartFacet();
            }
            else if (IsCurrentValue("RESOLVE"))
            {
                Advance();
                resolution = ParseAdvancedChartResolution();
            }
            else
            {
                throw new SyntaxException($"Unexpected CHART clause '{_parser.Current.Value}'.",
                    _parser.Current.Line, _parser.Current.Column);
            }
            Match(TokenType.COMMA);
        }

        Consume(TokenType.RPAREN, "Expected ')' to close CHART");
        var chartEnd = _parser.Previous;
        if (coordinate == null)
            throw new SyntaxException("CHART requires COORDINATE.", _parser.Current.Line, _parser.Current.Column);
        if (layers.Count == 0)
            throw new SyntaxException("CHART requires at least one LAYER.", _parser.Current.Line, _parser.Current.Column);

        return new AdvancedChartDefinition
        {
            Coordinate = coordinate,
            Scales = scales.ToImmutableArray(),
            Encodings = encodings.ToImmutableArray(),
            Layers = layers.ToImmutableArray(),
            Facet = facet,
            Resolution = resolution,
            Line = chartStart.Line,
            Column = chartStart.Column,
            EndLine = chartEnd.EndLine,
            EndColumn = chartEnd.EndColumn,
            StartOffset = chartStart.Offset,
            EndOffset = chartEnd.EndOffset
        };
    }

    private AdvancedChartCoordinate ParseAdvancedChartCoordinate()
    {
        var start = _parser.Current;
        Consume(TokenType.LPAREN, "Expected '(' after COORDINATE");
        AdvancedChartCoordinateKind? kind = null;
        decimal? startAngle = null, endAngle = null, innerRadius = null, aspectRatio = null;
        AdvancedChartGeographicProjection? projection = null;
        string? mapName = null, mapFile = null, featureKey = null;
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            var option = ConsumeAdvancedWord("Expected COORDINATE option").ToUpperInvariant();
            Consume(TokenType.EQUALS, $"Expected '=' after COORDINATE {option}");
            if (option == "TYPE")
            {
                kind = ConsumeAdvancedWord("Expected coordinate type").ToUpperInvariant() switch
                {
                    "CARTESIAN" => AdvancedChartCoordinateKind.Cartesian,
                    "TRANSPOSED_CARTESIAN" => AdvancedChartCoordinateKind.TransposedCartesian,
                    "POLAR" => AdvancedChartCoordinateKind.Polar,
                    "GEOGRAPHIC" => AdvancedChartCoordinateKind.Geographic,
                    var value => throw new SyntaxException($"Unsupported coordinate type '{value}'.",
                        _parser.Previous.Line, _parser.Previous.Column)
                };
            }
            else if (option is "PROJECTION" or "MAP_NAME" or "MAP_FILE" or "FEATURE_KEY")
            {
                switch (option)
                {
                    case "PROJECTION":
                        projection = ConsumeAdvancedWord("Expected geographic projection").ToUpperInvariant() switch
                        {
                            "EQUIRECTANGULAR" => AdvancedChartGeographicProjection.Equirectangular,
                            "MERCATOR" => AdvancedChartGeographicProjection.Mercator,
                            var value => throw new SyntaxException($"Unsupported geographic projection '{value}'.",
                                _parser.Previous.Line, _parser.Previous.Column)
                        };
                        break;
                    case "MAP_NAME": mapName = Consume(TokenType.STRING_LITERAL, "Expected MAP_NAME string").Value; break;
                    case "MAP_FILE": mapFile = Consume(TokenType.STRING_LITERAL, "Expected MAP_FILE string").Value; break;
                    case "FEATURE_KEY": featureKey = Consume(TokenType.STRING_LITERAL, "Expected FEATURE_KEY string").Value; break;
                }
            }
            else
            {
                var value = decimal.Parse(ParseSignedNumberText(), CultureInfo.InvariantCulture);
                switch (option)
                {
                    case "START_ANGLE": startAngle = value; break;
                    case "END_ANGLE": endAngle = value; break;
                    case "INNER_RADIUS": innerRadius = value; break;
                    case "ASPECT_RATIO": aspectRatio = value; break;
                    default:
                        throw new SyntaxException($"Unknown COORDINATE option '{option}'.",
                        _parser.Previous.Line, _parser.Previous.Column);
                }
            }
            Match(TokenType.COMMA);
        }
        Consume(TokenType.RPAREN, "Expected ')' after COORDINATE");
        var end = _parser.Previous;
        if (!kind.HasValue)
            throw new SyntaxException("COORDINATE requires TYPE.", _parser.Current.Line, _parser.Current.Column);
        return new AdvancedChartCoordinate
        {
            Kind = kind.Value,
            StartAngle = startAngle,
            EndAngle = endAngle,
            InnerRadius = innerRadius,
            AspectRatio = aspectRatio,
            Projection = projection,
            MapName = mapName,
            MapFile = mapFile,
            FeatureKey = featureKey,
            Line = start.Line,
            Column = start.Column,
            EndLine = end.EndLine,
            EndColumn = end.EndColumn
        };
    }

    private IEnumerable<AdvancedChartScale> ParseAdvancedChartScales()
    {
        Consume(TokenType.LPAREN, "Expected '(' after SCALES");
        var scales = new List<AdvancedChartScale>();
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            var start = _parser.Current;
            var name = ConsumeIdentifier("Expected scale name").Value;
            Consume(TokenType.EQUALS, $"Expected '=' after scale '{name}'");
            var kind = ParseAdvancedScaleKind(ConsumeAdvancedWord("Expected scale type"));
            Consume(TokenType.LPAREN, $"Expected '(' after scale type for '{name}'");
            AdvancedChartChannel? channel = null;
            var includeZero = false;
            Expression? minimum = null, maximum = null;
            var reverse = false;
            int? majorTickCount = null;
            decimal? tickInterval = null;
            var minorTicks = false;
            string? labelRotation = null;
            int? labelSkip = null;
            var outerPadding = 0m;
            var order = AdvancedChartSortDirection.Source;
            var explicitOrder = new List<Expression>();
            AdvancedChartColorRange? colorRange = null;
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                var option = ConsumeAdvancedWord("Expected scale option").ToUpperInvariant();
                Consume(TokenType.EQUALS, $"Expected '=' after scale option '{option}'");
                switch (option)
                {
                    case "CHANNEL": channel = ParseAdvancedChannel(ConsumeAdvancedWord("Expected scale channel")); break;
                    case "INCLUDE_ZERO": includeZero = ParseAdvancedOnOff(); break;
                    case "MIN": minimum = NormalizeAdvancedScalar(_parser.ParseExpression()); break;
                    case "MAX": maximum = NormalizeAdvancedScalar(_parser.ParseExpression()); break;
                    case "REVERSE": reverse = ParseAdvancedOnOff(); break;
                    case "MAJOR_TICK_COUNT":
                        majorTickCount = int.Parse(Consume(TokenType.NUMBER, "Expected integer MAJOR_TICK_COUNT").Value, CultureInfo.InvariantCulture);
                        break;
                    case "TICK_INTERVAL":
                        tickInterval = decimal.Parse(ParseSignedNumberText(), CultureInfo.InvariantCulture);
                        break;
                    case "MINOR_TICKS": minorTicks = ParseAdvancedOnOff(); break;
                    case "LABEL_ROTATION":
                        labelRotation = ConsumeAdvancedWord("Expected AUTO, 0, 45, or 90 for LABEL_ROTATION").ToUpperInvariant();
                        break;
                    case "LABEL_SKIP":
                        var skip = ConsumeAdvancedWord("Expected AUTO or a positive integer for LABEL_SKIP").ToUpperInvariant();
                        labelSkip = skip == "AUTO" ? null : int.Parse(skip, CultureInfo.InvariantCulture);
                        break;
                    case "OUTER_PADDING": outerPadding = decimal.Parse(ParseSignedNumberText(), CultureInfo.InvariantCulture); break;
                    case "ORDER":
                        if (Match(TokenType.LPAREN))
                        {
                            if (!ReportCheck(TokenType.RPAREN))
                            {
                                explicitOrder.Add(_parser.ParseExpression());
                                while (Match(TokenType.COMMA)) explicitOrder.Add(_parser.ParseExpression());
                            }
                            Consume(TokenType.RPAREN, "Expected ')' after explicit scale ORDER");
                        }
                        else order = ParseAdvancedSort(ConsumeAdvancedWord("Expected scale ORDER value"));
                        break;
                    case "RANGE": colorRange = ParseAdvancedColorRange(); break;
                    default:
                        throw new SyntaxException($"Unknown scale option '{option}'.",
                        _parser.Previous.Line, _parser.Previous.Column);
                }
                Match(TokenType.COMMA);
            }
            Consume(TokenType.RPAREN, $"Expected ')' after scale '{name}'");
            var scaleEnd = _parser.Previous;
            if (!channel.HasValue)
                throw new SyntaxException($"Scale '{name}' requires CHANNEL.", start.Line, start.Column);
            scales.Add(new AdvancedChartScale
            {
                Name = name,
                Kind = kind,
                Channel = channel.Value,
                IncludeZero = includeZero,
                Minimum = minimum,
                Maximum = maximum,
                Reverse = reverse,
                MajorTickCount = majorTickCount,
                TickInterval = tickInterval,
                MinorTicks = minorTicks,
                LabelRotation = labelRotation,
                LabelSkip = labelSkip,
                OuterPadding = outerPadding,
                Order = order,
                ExplicitOrder = explicitOrder.ToImmutableArray(),
                ColorRange = colorRange,
                Line = start.Line,
                Column = start.Column,
                EndLine = scaleEnd.EndLine,
                EndColumn = scaleEnd.EndColumn
            });
            Match(TokenType.COMMA);
        }
        Consume(TokenType.RPAREN, "Expected ')' after SCALES");
        return scales;
    }

    private AdvancedChartColorRange ParseAdvancedColorRange()
    {
        var start = _parser.Current;
        var kind = ConsumeAdvancedWord("Expected GRADIENT or DIVERGING after RANGE").ToUpperInvariant() switch
        {
            "GRADIENT" => AdvancedChartColorRangeKind.Gradient,
            "DIVERGING" => AdvancedChartColorRangeKind.Diverging,
            var value => throw new SyntaxException($"Unsupported color RANGE '{value}'.", _parser.Previous.Line, _parser.Previous.Column)
        };
        Consume(TokenType.LPAREN, "Expected '(' after color RANGE kind");
        Expression? low = null, mid = null, high = null, midpoint = null, nullColor = null;
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            var option = ConsumeAdvancedWord("Expected color RANGE option").ToUpperInvariant();
            Consume(TokenType.EQUALS, $"Expected '=' after color RANGE {option}");
            var value = _parser.ParseExpression();
            switch (option)
            {
                case "LOW": low = value; break;
                case "MID": mid = value; break;
                case "HIGH": high = value; break;
                case "MIDPOINT": midpoint = value; break;
                case "NULL_COLOR": nullColor = value; break;
                default: throw new SyntaxException($"Unknown color RANGE option '{option}'.", _parser.Previous.Line, _parser.Previous.Column);
            }
            Match(TokenType.COMMA);
        }
        Consume(TokenType.RPAREN, "Expected ')' after color RANGE");
        var end = _parser.Previous;
        if (low is null || high is null)
            throw new SyntaxException("Color RANGE requires LOW and HIGH.", _parser.Current.Line, _parser.Current.Column);
        if (kind == AdvancedChartColorRangeKind.Diverging && (mid is null || midpoint is null))
            throw new SyntaxException("DIVERGING color RANGE requires MID and MIDPOINT.", _parser.Current.Line, _parser.Current.Column);
        return new AdvancedChartColorRange
        {
            Kind = kind,
            Low = low,
            Mid = mid,
            High = high,
            Midpoint = midpoint,
            NullColor = nullColor,
            Line = start.Line,
            Column = start.Column,
            EndLine = end.EndLine,
            EndColumn = end.EndColumn
        };
    }

    private IEnumerable<AdvancedChartLayer> ParseAdvancedChartLayers()
    {
        Consume(TokenType.LPAREN, "Expected '(' after LAYERS");
        var layers = new List<AdvancedChartLayer>();
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            var start = _parser.Current;
            var name = ConsumeIdentifier("Expected layer name").Value;
            Consume(TokenType.EQUALS, $"Expected '=' after layer '{name}'");
            var mark = ParseAdvancedMark(ConsumeAdvancedWord("Expected mark type"));
            Consume(TokenType.LPAREN, $"Expected '(' after mark type for '{name}'");
            var zIndex = layers.Count;
            var inheritEncodings = true;
            var bandSize = .75m;
            var tickThickness = .15m;
            var tickOrientation = AdvancedChartTickOrientation.Auto;
            var position = new AdvancedChartPosition();
            var encodings = new List<AdvancedChartEncoding>();
            var styles = new List<AdvancedChartStyle>();
            var conditions = new List<AdvancedChartCondition>();
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                if (IsCurrentValue("Z_INDEX"))
                {
                    Advance();
                    Consume(TokenType.EQUALS, "Expected '=' after Z_INDEX");
                    zIndex = int.Parse(ParseSignedNumberText(), CultureInfo.InvariantCulture);
                }
                else if (IsCurrentValue("INHERIT_ENCODINGS"))
                {
                    Advance();
                    Consume(TokenType.EQUALS, "Expected '=' after INHERIT_ENCODINGS");
                    inheritEncodings = ParseAdvancedOnOff();
                }
                else if (IsCurrentValue("BAND_SIZE"))
                {
                    Advance();
                    Consume(TokenType.EQUALS, "Expected '=' after BAND_SIZE");
                    bandSize = decimal.Parse(ParseSignedNumberText(), CultureInfo.InvariantCulture);
                }
                else if (IsCurrentValue("POSITION"))
                {
                    Advance();
                    Consume(TokenType.EQUALS, "Expected '=' after POSITION");
                    position = ParseAdvancedPosition();
                }
                else if (IsCurrentValue("THICKNESS"))
                {
                    Advance();
                    Consume(TokenType.EQUALS, "Expected '=' after THICKNESS");
                    tickThickness = decimal.Parse(ParseSignedNumberText(), CultureInfo.InvariantCulture);
                }
                else if (IsCurrentValue("ORIENTATION"))
                {
                    Advance();
                    Consume(TokenType.EQUALS, "Expected '=' after ORIENTATION");
                    tickOrientation = ParseAdvancedTickOrientation(ConsumeAdvancedWord("Expected AUTO, HORIZONTAL, or VERTICAL"));
                }
                else if (IsCurrentValue("ENCODINGS"))
                {
                    Advance();
                    encodings.AddRange(ParseAdvancedChartEncodings());
                }
                else if (Match(TokenType.STYLE))
                {
                    styles.AddRange(ParseAdvancedChartStyles());
                }
                else if (IsCurrentValue("CONDITIONS"))
                {
                    Advance();
                    conditions.AddRange(ParseAdvancedChartConditions());
                }
                else
                {
                    throw new SyntaxException($"Unexpected option '{_parser.Current.Value}' in layer '{name}'.",
                        _parser.Current.Line, _parser.Current.Column);
                }
                Match(TokenType.COMMA);
            }
            Consume(TokenType.RPAREN, $"Expected ')' after layer '{name}'");
            var layerEnd = _parser.Previous;
            layers.Add(new AdvancedChartLayer
            {
                Name = name,
                Mark = mark,
                ZIndex = zIndex,
                InheritEncodings = inheritEncodings,
                BandSize = bandSize,
                TickThickness = tickThickness,
                TickOrientation = tickOrientation,
                Position = position,
                Encodings = encodings.ToImmutableArray(),
                Styles = styles.ToImmutableArray(),
                Conditions = conditions.ToImmutableArray(),
                Line = start.Line,
                Column = start.Column,
                EndLine = layerEnd.EndLine,
                EndColumn = layerEnd.EndColumn
            });
            Match(TokenType.COMMA);
        }
        Consume(TokenType.RPAREN, "Expected ')' after LAYERS");
        return layers;
    }

    private AdvancedChartPosition ParseAdvancedPosition()
    {
        var start = _parser.Current;
        var kind = ConsumeAdvancedWord("Expected IDENTITY, JITTER, or NUDGE after POSITION").ToUpperInvariant() switch
        {
            "IDENTITY" => AdvancedChartPositionKind.Identity,
            "JITTER" => AdvancedChartPositionKind.Jitter,
            "NUDGE" => AdvancedChartPositionKind.Nudge,
            var value => throw new SyntaxException($"Unknown POSITION adjustment '{value}'.", start.Line, start.Column)
        };
        if (kind == AdvancedChartPositionKind.Identity)
            return new AdvancedChartPosition
            {
                Kind = kind,
                Line = start.Line,
                Column = start.Column,
                EndLine = _parser.Previous.EndLine,
                EndColumn = _parser.Previous.EndColumn
            };

        Consume(TokenType.LPAREN, $"Expected '(' after POSITION {kind.ToString().ToUpperInvariant()}");
        decimal x = 0m, y = 0m;
        string? key = null;
        var seed = 0;
        var unit = kind == AdvancedChartPositionKind.Nudge ? AdvancedChartPositionUnit.Data : AdvancedChartPositionUnit.Band;
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            var option = ConsumeAdvancedWord("Expected position adjustment option").ToUpperInvariant();
            Consume(TokenType.EQUALS, $"Expected '=' after position option '{option}'");
            switch (option)
            {
                case "X": x = decimal.Parse(ParseSignedNumberText(), CultureInfo.InvariantCulture); break;
                case "Y": y = decimal.Parse(ParseSignedNumberText(), CultureInfo.InvariantCulture); break;
                case "KEY": key = ConsumeIdentifier("Expected stable key field").Value; break;
                case "SEED": seed = int.Parse(ParseSignedNumberText(), CultureInfo.InvariantCulture); break;
                case "UNIT":
                    unit = ConsumeAdvancedWord("Expected DATA, BAND, or EM").ToUpperInvariant() switch
                    {
                        "DATA" => AdvancedChartPositionUnit.Data,
                        "BAND" => AdvancedChartPositionUnit.Band,
                        "EM" => AdvancedChartPositionUnit.Em,
                        var value => throw new SyntaxException($"Unknown NUDGE unit '{value}'.", _parser.Previous.Line, _parser.Previous.Column)
                    };
                    break;
                default: throw new SyntaxException($"Unknown POSITION option '{option}'.", _parser.Previous.Line, _parser.Previous.Column);
            }
            Match(TokenType.COMMA);
        }
        Consume(TokenType.RPAREN, "Expected ')' after POSITION adjustment");
        if (kind == AdvancedChartPositionKind.Jitter && key is null)
            throw new SyntaxException("POSITION JITTER requires KEY.", start.Line, start.Column);
        if (kind == AdvancedChartPositionKind.Jitter && unit != AdvancedChartPositionUnit.Band)
            throw new SyntaxException("POSITION JITTER uses scale/band-relative amplitudes and does not accept UNIT.", start.Line, start.Column);
        if (kind == AdvancedChartPositionKind.Nudge && (key is not null || seed != 0))
            throw new SyntaxException("POSITION NUDGE does not accept KEY or SEED.", start.Line, start.Column);
        return new AdvancedChartPosition
        {
            Kind = kind,
            X = x,
            Y = y,
            KeyField = key,
            Seed = seed,
            Unit = unit,
            Line = start.Line,
            Column = start.Column,
            EndLine = _parser.Previous.EndLine,
            EndColumn = _parser.Previous.EndColumn
        };
    }

    private static Expression NormalizeAdvancedScalar(Expression expression)
    {
        if (expression is not BinaryExpression
            {
                Operator: TokenType.MINUS,
                Left: LiteralExpression { Value: decimal left },
                Right: LiteralExpression right
            } binary || left != 0m || right.Value is not (byte or sbyte or short or ushort or int or uint or long or ulong or float or double or decimal))
            return expression;
        return new LiteralExpression(-Convert.ToDecimal(right.Value, CultureInfo.InvariantCulture), TokenType.NUMBER)
        {
            Line = binary.Line,
            Column = binary.Column,
            EndLine = binary.EndLine,
            EndColumn = binary.EndColumn
        };
    }

    private IEnumerable<AdvancedChartEncoding> ParseAdvancedChartEncodings()
    {
        Consume(TokenType.LPAREN, "Expected '(' after ENCODINGS");
        var encodings = new List<AdvancedChartEncoding>();
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            var start = _parser.Current;
            var channel = ParseAdvancedChannel(ConsumeAdvancedWord("Expected encoding channel"));
            Consume(TokenType.EQUALS, "Expected '=' after encoding channel");
            AdvancedChartBindingSource source;
            if (IsAdvancedConstantBinding())
            {
                var kind = IsCurrentValue("DATUM") ? AdvancedChartBindingSourceKind.Datum : AdvancedChartBindingSourceKind.Value;
                Advance();
                Consume(TokenType.LPAREN, $"Expected '(' after {kind.ToString().ToUpperInvariant()}");
                var expression = NormalizeAdvancedScalar(_parser.ParseExpression());
                if (expression is not LiteralExpression and not VariableExpression)
                    throw new SyntaxException($"{kind.ToString().ToUpperInvariant()} accepts only a scalar literal or declared variable.", start.Line, start.Column);
                Consume(TokenType.RPAREN, $"Expected ')' after {kind.ToString().ToUpperInvariant()} source");
                source = new AdvancedChartBindingSource
                {
                    Kind = kind,
                    Constant = expression,
                    Line = start.Line,
                    Column = start.Column,
                    EndLine = _parser.Previous.EndLine,
                    EndColumn = _parser.Previous.EndColumn
                };
            }
            else
            {
                var fieldToken = ConsumeIdentifier("Expected source column, DATUM(...), or VALUE(...) for encoding");
                var field = fieldToken.Value;
                source = new AdvancedChartBindingSource
                {
                    Kind = AdvancedChartBindingSourceKind.Field,
                    Field = field,
                    Line = fieldToken.Line,
                    Column = fieldToken.Column,
                    EndLine = fieldToken.EndLine,
                    EndColumn = fieldToken.EndColumn
                };
            }
            Consume(TokenType.LPAREN, "Expected '(' after encoding binding source");
            AdvancedChartDataKind? dataKind = null;
            string? scale = null, format = null;
            var axis = AdvancedChartAxisRole.None;
            var sort = AdvancedChartSortDirection.Source;
            var stack = AdvancedChartStackMode.None;
            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
            {
                var option = ConsumeAdvancedWord("Expected encoding option").ToUpperInvariant();
                Consume(TokenType.EQUALS, $"Expected '=' after encoding option '{option}'");
                switch (option)
                {
                    case "TYPE": dataKind = ParseAdvancedDataKind(ConsumeAdvancedWord("Expected encoding TYPE")); break;
                    case "SCALE": scale = ConsumeIdentifier("Expected scale name").Value; break;
                    case "AXIS": axis = ParseAdvancedAxis(ConsumeAdvancedWord("Expected AXIS value")); break;
                    case "SORT": sort = ParseAdvancedSort(ConsumeAdvancedWord("Expected SORT value")); break;
                    case "FORMAT": format = Consume(TokenType.STRING_LITERAL, "Expected format string").Value; break;
                    case "STACK": stack = ParseAdvancedStack(ConsumeAdvancedWord("Expected NONE, ZERO, or NORMALIZE")); break;
                    default:
                        throw new SyntaxException($"Unknown encoding option '{option}'.",
                        _parser.Previous.Line, _parser.Previous.Column);
                }
                Match(TokenType.COMMA);
            }
            Consume(TokenType.RPAREN, "Expected ')' after encoding options");
            var encodingEnd = _parser.Previous;
            if (!dataKind.HasValue)
                throw new SyntaxException($"Encoding {channel} requires TYPE.", start.Line, start.Column);
            encodings.Add(new AdvancedChartEncoding
            {
                Channel = channel,
                Source = source,
                DataKind = dataKind.Value,
                Scale = scale,
                Axis = axis,
                Sort = sort,
                Format = format,
                Stack = stack,
                Line = start.Line,
                Column = start.Column,
                EndLine = encodingEnd.EndLine,
                EndColumn = encodingEnd.EndColumn
            });
            Match(TokenType.COMMA);
        }
        Consume(TokenType.RPAREN, "Expected ')' after ENCODINGS");
        return encodings;
    }

    private bool IsAdvancedConstantBinding()
    {
        if ((!IsCurrentValue("DATUM") && !IsCurrentValue("VALUE")) || _parser.Peek.Type != TokenType.LPAREN)
            return false;
        var depth = 0;
        for (var distance = 1; ; distance++)
        {
            var token = _parser.LookAhead(distance);
            if (token.Type is TokenType.EOF or TokenType.SEMICOLON) return false;
            if (token.Type == TokenType.LPAREN) depth++;
            else if (token.Type == TokenType.RPAREN && --depth == 0)
                return _parser.LookAhead(distance + 1).Type == TokenType.LPAREN;
        }
    }

    private IEnumerable<AdvancedChartStyle> ParseAdvancedChartStyles()
    {
        Consume(TokenType.LPAREN, "Expected '(' after layer STYLE");
        var styles = new List<AdvancedChartStyle>();
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            var styleStart = _parser.Current;
            var name = ConsumeAdvancedWord("Expected portable style name").ToUpperInvariant();
            Consume(TokenType.EQUALS, $"Expected '=' after style '{name}'");
            styles.Add(new AdvancedChartStyle(name, _parser.ParseExpression())
            {
                Line = styleStart.Line,
                Column = styleStart.Column,
                EndLine = _parser.Previous.EndLine,
                EndColumn = _parser.Previous.EndColumn
            });
            Match(TokenType.COMMA);
        }
        Consume(TokenType.RPAREN, "Expected ')' after layer STYLE");
        return styles;
    }

    private IEnumerable<AdvancedChartCondition> ParseAdvancedChartConditions()
    {
        Consume(TokenType.LPAREN, "Expected '(' after CONDITIONS");
        var conditions = new List<AdvancedChartCondition>();
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            var start = _parser.Current;
            var channel = ParseAdvancedConditionChannel(ConsumeAdvancedWord("Expected condition channel"));
            Consume(TokenType.WHEN, "Expected WHEN after condition channel");
            var predicate = _parser.ParseExpression();
            Consume(TokenType.THEN, "Expected THEN after condition predicate");
            var whenTrue = _parser.ParseExpression();
            Expression? whenFalse = null;
            if (Match(TokenType.ELSE)) whenFalse = _parser.ParseExpression();
            conditions.Add(new AdvancedChartCondition
            {
                Channel = channel,
                Predicate = predicate,
                WhenTrue = whenTrue,
                WhenFalse = whenFalse,
                Line = start.Line,
                Column = start.Column,
                EndLine = _parser.Previous.EndLine,
                EndColumn = _parser.Previous.EndColumn
            });
            Match(TokenType.COMMA);
        }
        Consume(TokenType.RPAREN, "Expected ')' after CONDITIONS");
        return conditions;
    }

    private AdvancedChartFacet ParseAdvancedChartFacet()
    {
        var start = _parser.Current;
        Consume(TokenType.LPAREN, "Expected '(' after FACET");
        string? row = null, column = null, wrap = null;
        int? columns = null;
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            var option = ConsumeAdvancedWord("Expected ROW or COLUMN in FACET").ToUpperInvariant();
            Consume(TokenType.EQUALS, $"Expected '=' after FACET {option}");
            if (option == "COLUMNS")
            {
                columns = int.Parse(ParseSignedNumberText(), CultureInfo.InvariantCulture);
            }
            else
            {
                var field = ConsumeIdentifier($"Expected source column after FACET {option}").Value;
                if (option == "ROW") row = field;
                else if (option == "COLUMN") column = field;
                else if (option == "WRAP") wrap = field;
                else throw new SyntaxException($"Unknown FACET option '{option}'.", _parser.Previous.Line, _parser.Previous.Column);
            }
            Match(TokenType.COMMA);
        }
        Consume(TokenType.RPAREN, "Expected ')' after FACET");
        var end = _parser.Previous;
        if (row == null && column == null && wrap == null)
            throw new SyntaxException("FACET requires ROW, COLUMN, or WRAP.", _parser.Current.Line, _parser.Current.Column);
        if (wrap is not null && (row is not null || column is not null))
            throw new SyntaxException("FACET WRAP is mutually exclusive with ROW and COLUMN.", _parser.Current.Line, _parser.Current.Column);
        if (columns is not null && wrap is null)
            throw new SyntaxException("FACET COLUMNS requires WRAP.", _parser.Current.Line, _parser.Current.Column);
        if (columns is < 1 or > 12)
            throw new SyntaxException("FACET COLUMNS must be between 1 and 12.", _parser.Current.Line, _parser.Current.Column);
        return new AdvancedChartFacet
        {
            RowField = row,
            ColumnField = column,
            WrapField = wrap,
            Columns = columns,
            Line = start.Line,
            Column = start.Column,
            EndLine = end.EndLine,
            EndColumn = end.EndColumn
        };
    }

    private AdvancedChartResolution ParseAdvancedChartResolution()
    {
        var start = _parser.Current;
        Consume(TokenType.LPAREN, "Expected '(' after RESOLVE");
        var x = AdvancedChartResolutionMode.Shared;
        var y = AdvancedChartResolutionMode.Shared;
        var color = AdvancedChartResolutionMode.Shared;
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            var channel = ConsumeAdvancedWord("Expected X, Y, or COLOR in RESOLVE").ToUpperInvariant();
            Consume(TokenType.EQUALS, $"Expected '=' after RESOLVE {channel}");
            var mode = ParseAdvancedResolution(ConsumeAdvancedWord("Expected SHARED or INDEPENDENT"));
            if (channel == "X") x = mode;
            else if (channel == "Y") y = mode;
            else if (channel == "COLOR") color = mode;
            else throw new SyntaxException($"Unknown RESOLVE channel '{channel}'.", _parser.Previous.Line, _parser.Previous.Column);
            Match(TokenType.COMMA);
        }
        Consume(TokenType.RPAREN, "Expected ')' after RESOLVE");
        return new AdvancedChartResolution
        {
            X = x,
            Y = y,
            Color = color,
            Line = start.Line,
            Column = start.Column,
            EndLine = _parser.Previous.EndLine,
            EndColumn = _parser.Previous.EndColumn
        };
    }

    private string ConsumeAdvancedWord(string message)
    {
        if (ReportAtEnd() || ReportCheck(TokenType.COMMA) || ReportCheck(TokenType.LPAREN) ||
            ReportCheck(TokenType.RPAREN) || ReportCheck(TokenType.EQUALS))
            throw new SyntaxException(message, _parser.Current.Line, _parser.Current.Column);
        return Advance().Value;
    }

    private bool ParseAdvancedOnOff() => ConsumeAdvancedWord("Expected ON or OFF").ToUpperInvariant() switch
    {
        "ON" => true,
        "OFF" => false,
        var value => throw new SyntaxException($"Expected ON or OFF, got '{value}'.", _parser.Previous.Line, _parser.Previous.Column)
    };

    private AdvancedChartTickOrientation ParseAdvancedTickOrientation(string value) => value.ToUpperInvariant() switch
    {
        "AUTO" => AdvancedChartTickOrientation.Auto,
        "HORIZONTAL" => AdvancedChartTickOrientation.Horizontal,
        "VERTICAL" => AdvancedChartTickOrientation.Vertical,
        _ => throw new SyntaxException($"Expected AUTO, HORIZONTAL, or VERTICAL, got '{value}'.", _parser.Previous.Line, _parser.Previous.Column)
    };

    private AdvancedChartMarkKind ParseAdvancedMark(string value) => value.ToUpperInvariant() switch
    {
        "RECT" => AdvancedChartMarkKind.Rect,
        "LINE" => AdvancedChartMarkKind.Line,
        "AREA" => AdvancedChartMarkKind.Area,
        "POINT" => AdvancedChartMarkKind.Point,
        "RULE" => AdvancedChartMarkKind.Rule,
        "ARC" => AdvancedChartMarkKind.Arc,
        "TEXT" => AdvancedChartMarkKind.Text,
        "TICK" => AdvancedChartMarkKind.Tick,
        _ => throw new SyntaxException($"Unknown advanced mark '{value}'.", _parser.Previous.Line, _parser.Previous.Column)
    };

    private AdvancedChartScaleKind ParseAdvancedScaleKind(string value) => value.ToUpperInvariant() switch
    {
        "LINEAR" => AdvancedChartScaleKind.Linear,
        "LOGARITHMIC" => AdvancedChartScaleKind.Logarithmic,
        "TIME" => AdvancedChartScaleKind.Time,
        "BAND" => AdvancedChartScaleKind.Band,
        "POINT" => AdvancedChartScaleKind.Point,
        "ORDINAL" => AdvancedChartScaleKind.Ordinal,
        "IDENTITY" => AdvancedChartScaleKind.Identity,
        _ => throw new SyntaxException($"Unknown scale type '{value}'.", _parser.Previous.Line, _parser.Previous.Column)
    };

    private AdvancedChartChannel ParseAdvancedChannel(string value) => value.ToUpperInvariant() switch
    {
        "X" => AdvancedChartChannel.X,
        "X2" => AdvancedChartChannel.X2,
        "X_START" => AdvancedChartChannel.XStart,
        "X_END" => AdvancedChartChannel.XEnd,
        "X_OFFSET" => AdvancedChartChannel.XOffset,
        "Y" => AdvancedChartChannel.Y,
        "Y2" => AdvancedChartChannel.Y2,
        "Y_START" => AdvancedChartChannel.YStart,
        "Y_END" => AdvancedChartChannel.YEnd,
        "Y_OFFSET" => AdvancedChartChannel.YOffset,
        "LOW" => AdvancedChartChannel.Low,
        "Q1" => AdvancedChartChannel.Q1,
        "MEDIAN" => AdvancedChartChannel.Median,
        "Q3" => AdvancedChartChannel.Q3,
        "HIGH" => AdvancedChartChannel.High,
        "OPEN" => AdvancedChartChannel.Open,
        "CLOSE" => AdvancedChartChannel.Close,
        "ERROR_LOW" => AdvancedChartChannel.ErrorLow,
        "ERROR_HIGH" => AdvancedChartChannel.ErrorHigh,
        "CONFIDENCE_LOW" => AdvancedChartChannel.ConfidenceLow,
        "CONFIDENCE_HIGH" => AdvancedChartChannel.ConfidenceHigh,
        "COLOR" => AdvancedChartChannel.Color,
        "SIZE" => AdvancedChartChannel.Size,
        "SHAPE" => AdvancedChartChannel.Shape,
        "THETA" => AdvancedChartChannel.Theta,
        "RADIUS" => AdvancedChartChannel.Radius,
        "LONGITUDE" => AdvancedChartChannel.Longitude,
        "LATITUDE" => AdvancedChartChannel.Latitude,
        "REGION" => AdvancedChartChannel.Region,
        "ROUTE" => AdvancedChartChannel.Route,
        "TEXT" => AdvancedChartChannel.Text,
        "TOOLTIP" => AdvancedChartChannel.Tooltip,
        "DETAIL" => AdvancedChartChannel.Detail,
        _ => throw new SyntaxException($"Unknown encoding channel '{value}'.", _parser.Previous.Line, _parser.Previous.Column)
    };

    private AdvancedChartStackMode ParseAdvancedStack(string value) => value.ToUpperInvariant() switch
    {
        "NONE" => AdvancedChartStackMode.None,
        "ZERO" => AdvancedChartStackMode.Zero,
        "NORMALIZE" => AdvancedChartStackMode.Normalize,
        _ => throw new SyntaxException($"Unknown STACK mode '{value}'.", _parser.Previous.Line, _parser.Previous.Column)
    };

    private AdvancedChartDataKind ParseAdvancedDataKind(string value) => value.ToUpperInvariant() switch
    {
        "QUANTITATIVE" => AdvancedChartDataKind.Quantitative,
        "TEMPORAL" => AdvancedChartDataKind.Temporal,
        "NOMINAL" => AdvancedChartDataKind.Nominal,
        "ORDINAL" => AdvancedChartDataKind.Ordinal,
        _ => throw new SyntaxException($"Unknown encoding type '{value}'.", _parser.Previous.Line, _parser.Previous.Column)
    };

    private AdvancedChartAxisRole ParseAdvancedAxis(string value) => value.ToUpperInvariant() switch
    {
        "NONE" => AdvancedChartAxisRole.None,
        "PRIMARY" => AdvancedChartAxisRole.Primary,
        "SECONDARY" => AdvancedChartAxisRole.Secondary,
        _ => throw new SyntaxException($"Unknown axis role '{value}'.", _parser.Previous.Line, _parser.Previous.Column)
    };

    private AdvancedChartSortDirection ParseAdvancedSort(string value) => value.ToUpperInvariant() switch
    {
        "SOURCE" => AdvancedChartSortDirection.Source,
        "ASCENDING" => AdvancedChartSortDirection.Ascending,
        "DESCENDING" => AdvancedChartSortDirection.Descending,
        _ => throw new SyntaxException($"Unknown sort direction '{value}'.", _parser.Previous.Line, _parser.Previous.Column)
    };

    private AdvancedChartResolutionMode ParseAdvancedResolution(string value) => value.ToUpperInvariant() switch
    {
        "SHARED" => AdvancedChartResolutionMode.Shared,
        "INDEPENDENT" => AdvancedChartResolutionMode.Independent,
        _ => throw new SyntaxException($"Unknown resolution mode '{value}'.", _parser.Previous.Line, _parser.Previous.Column)
    };

    private AdvancedChartConditionChannel ParseAdvancedConditionChannel(string value) => value.ToUpperInvariant() switch
    {
        "COLOR" => AdvancedChartConditionChannel.Color,
        "OPACITY" => AdvancedChartConditionChannel.Opacity,
        "SIZE" => AdvancedChartConditionChannel.Size,
        "SHAPE" => AdvancedChartConditionChannel.Shape,
        "TEXT" => AdvancedChartConditionChannel.Text,
        _ => throw new SyntaxException($"Unknown condition channel '{value}'.", _parser.Previous.Line, _parser.Previous.Column)
    };

    // ── CREATE PAGE ───────────────────────────────────────────────────────

    private PageLayoutDefinition ParsePageLayoutDefinition()
    {
        Consume(TokenType.LPAREN, "Expected '(' after PAGE_LAYOUT or PRINT_LAYOUT");
        var layout = new PageLayoutDefinition();
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            if (IsCurrentValue("PAGE_SIZE") || IsCurrentValue("SIZE"))
            {
                Advance();
                Match(TokenType.EQUALS);
                layout = layout with { PageSize = ConsumeReportOptionValue() };
            }
            else if (Match(TokenType.ORIENTATION))
            {
                Match(TokenType.EQUALS);
                layout = layout with { Orientation = ConsumeReportOptionValue() };
            }
            else if (IsCurrentValue("CUSTOM_WIDTH"))
            {
                Advance();
                Match(TokenType.EQUALS);
                layout = layout with { CustomWidth = decimal.Parse(ConsumeReportOptionValue()) };
            }
            else if (IsCurrentValue("CUSTOM_HEIGHT"))
            {
                Advance();
                Match(TokenType.EQUALS);
                layout = layout with { CustomHeight = decimal.Parse(ConsumeReportOptionValue()) };
            }
            else if (IsCurrentValue("MARGINS") || Match(TokenType.MARGIN))
            {
                if (IsCurrentValue("MARGINS") || IsCurrentValue("MARGIN")) Advance();
                Match(TokenType.EQUALS);
                Consume(TokenType.LPAREN, "Expected '(' for margins");
                var top = decimal.Parse(ConsumeReportOptionValue());
                Match(TokenType.COMMA);
                var right = decimal.Parse(ConsumeReportOptionValue());
                Match(TokenType.COMMA);
                var bottom = decimal.Parse(ConsumeReportOptionValue());
                Match(TokenType.COMMA);
                var left = decimal.Parse(ConsumeReportOptionValue());
                Consume(TokenType.RPAREN, "Expected ')' for margins");
                layout = layout with { MarginTop = top, MarginRight = right, MarginBottom = bottom, MarginLeft = left };
            }
            else if (IsCurrentValue("UNITS") || IsCurrentValue("UNIT"))
            {
                Advance();
                Match(TokenType.EQUALS);
                layout = layout with { Units = ConsumeReportOptionValue() };
            }
            else if (IsCurrentValue("OVERFLOW"))
            {
                Advance();
                Match(TokenType.EQUALS);
                layout = layout with { Overflow = ConsumeReportOptionValue() };
            }
            else
            {
                throw new SyntaxException($"Unexpected layout option '{_parser.Current.Value}'", _parser.Current.Line, _parser.Current.Column);
            }
            Match(TokenType.COMMA);
        }
        Consume(TokenType.RPAREN, "Expected ')' to close layout definitions");
        return layout;
    }

    private RowDetailDefinition ParseRowDetailDefinition()
    {
        Consume(TokenType.LPAREN, "Expected '(' after ROW_DETAIL");
        var def = new RowDetailDefinition { TargetName = string.Empty };

        while (!Match(TokenType.RPAREN))
        {
            if (IsCurrentValue("TARGET"))
            {
                Advance();
                Match(TokenType.EQUALS);
                def = def with { TargetName = ConsumeIdentifierOrString("Expected target visual name").Value };
            }
            else if (Match(TokenType.MAPPINGS) || IsCurrentValue("BINDINGS") || IsCurrentValue("MAP"))
            {
                // If it was BINDINGS or MAP, we need to advance over it.
                // Match(TokenType.MAPPINGS) already advanced.
                if (IsCurrentValue("BINDINGS") || IsCurrentValue("MAP") || _parser.Current.Type == TokenType.IDENTIFIER)
                {
                    Advance();
                }
                Consume(TokenType.LPAREN, "Expected '(' after BINDINGS");
                while (!Match(TokenType.RPAREN))
                {
                    var paramName = Consume(TokenType.VARIABLE, "Expected parameter (e.g., @childParam)").Value.TrimStart('@');
                    Match(TokenType.EQUALS);
                    var colName = ConsumeIdentifierOrString("Expected parent column name").Value;
                    def.Bindings.Add(new RowDetailBinding(colName, paramName));
                    Match(TokenType.COMMA);
                }
            }
            else if (IsCurrentValue("LIMIT"))
            {
                Advance();
                Match(TokenType.EQUALS);
                var limitStr = Consume(TokenType.NUMBER, "Expected number for LIMIT").Value;
                if (int.TryParse(limitStr, out var limitVal))
                {
                    def = def with { Limit = limitVal };
                }
            }
            else
            {
                throw new SyntaxException($"Unexpected token '{_parser.Current.Value}' in ROW_DETAIL", _parser.Current.Line, _parser.Current.Column);
            }
            Match(TokenType.COMMA);
        }

        if (string.IsNullOrWhiteSpace(def.TargetName))
            throw new SyntaxException("ROW_DETAIL requires a TARGET = <name>", _parser.Current.Line, _parser.Current.Column);

        return def;
    }

    private PrintLayoutOverride ParsePrintLayoutOverride()
    {
        Consume(TokenType.LPAREN, "Expected '(' after PRINT_LAYOUT");
        var layout = new PrintLayoutOverride();
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            if (IsCurrentValue("PAGE_BREAK_BEFORE"))
            {
                Advance();
                Match(TokenType.EQUALS);
                layout = layout with { PageBreakBefore = ParseOnOffValue() == "ON" };
            }
            else if (IsCurrentValue("PAGE_BREAK_AFTER"))
            {
                Advance();
                Match(TokenType.EQUALS);
                layout = layout with { PageBreakAfter = ParseOnOffValue() == "ON" };
            }
            else if (IsCurrentValue("KEEP_TOGETHER"))
            {
                Advance();
                Match(TokenType.EQUALS);
                layout = layout with { KeepTogether = ParseOnOffValue() == "ON" };
            }
            else if (IsCurrentValue("EXCLUDE_FROM_PRINT") || IsCurrentValue("EXCLUDE"))
            {
                Advance();
                Match(TokenType.EQUALS);
                layout = layout with { ExcludeFromPrint = ParseOnOffValue() == "ON" };
            }
            else
            {
                throw new SyntaxException($"Unexpected print layout override '{_parser.Current.Value}'", _parser.Current.Line, _parser.Current.Column);
            }
            Match(TokenType.COMMA);
        }
        Consume(TokenType.RPAREN, "Expected ')' to close PRINT_LAYOUT");
        return layout;
    }

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
        var slotMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var pageStyles = new Dictionary<string, string>();
        string? pageStyleName = null;
        var pagePalette = ImmutableArray<string>.Empty;
        Expression? title = null, subtitle = null;
        bool titleMd = false, subtitleMd = false;
        TitleDefinition? titleDef = null, subtitleDef = null;
        TooltipDefinition? tooltip = null;
        int refreshSecs = 0;
        PageLayoutDefinition? printLayout = null;

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
                    var visual = ConsumeIdentifier("Expected visual name after '='").Value;
                    slotMap[slot] = visual;
                    Match(TokenType.COMMA);
                }
                Consume(TokenType.RPAREN, "Expected ')' to close MAP");
            }
            else if (Match(TokenType.STYLE))
            {
                ParseStyleClause(pageStyles, ref pageStyleName, ref pagePalette);
            }
            else if (IsCurrentValue("GAP"))
            {
                Advance();
                Consume(TokenType.EQUALS, "Expected '=' after GAP");
                pageStyles["GAP"] = ConsumeReportOptionValue();
            }
            else if (Match(TokenType.TITLE))
            {
                (title, titleMd, titleDef) = ParseVisualPropertyWithMd("TITLE");
            }
            else if (Match(TokenType.SUBTITLE))
            {
                (subtitle, subtitleMd, subtitleDef) = ParseVisualPropertyWithMd("SUBTITLE");
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
            else if (IsCurrentValue("PRINT_LAYOUT") || IsCurrentValue("PAGE_LAYOUT"))
            {
                Advance();
                printLayout = ParsePageLayoutDefinition();
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
            Name = name,
            PageMode = pageMode,
            Structure = structure,
            SlotMap = slotMap,
            Styles = pageStyles,
            Palette = pagePalette,
            StyleName = pageStyleName,
            Title = title,
            TitleIsMarkdown = titleMd,
            TitleDefinition = titleDef,
            Subtitle = subtitle,
            SubtitleIsMarkdown = subtitleMd,
            SubtitleDefinition = subtitleDef,
            Tooltip = tooltip,
            Visibility = visibility,
            RefreshIntervalSeconds = refreshSecs,
            Mode = mode,
            PrintLayout = printLayout,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    // ── CREATE DATASET ────────────────────────────────────────────────────

    public Statement ParseCreateDataset(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
    {
        var tableName = ConsumeIdentifier("Expected &datasetName after CREATE DATASET").Value;
        if (!tableName.StartsWith("&"))
            throw new SyntaxException("CREATE DATASET names must use the &dataset form", startToken.Line, startToken.Column);

        string? ttl = null;
        bool compress = false;
        var encryptionMode = DatasetEncryptionMode.MachineBound;
        string? encryptionPassword = null;
        string? keyFile = null;
        var accessLevel = ETL_SQL.Core.Data.DatasetAccessLevel.Private;

        while (!ReportCheck(TokenType.AS) && !ReportAtEnd())
        {
            if (Match(TokenType.REFRESH))
            {
                throw new SyntaxException(
                    "CREATE DATASET ... REFRESH EVERY has been retired. Keep TTL on the dataset, " +
                    "then use CREATE SCHEDULE and CREATE JOB ... FOR REPORT and attach them " +
                    "with ALTER JOB ... ADD SCHEDULE.",
                    _parser.Previous.Line, _parser.Previous.Column);
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
                    "MACHINE" => DatasetEncryptionMode.MachineBound,
                    "PASSWORD" => DatasetEncryptionMode.Password,
                    "KEYFILE" => DatasetEncryptionMode.KeyFile,
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
            TempTableName = tableName,
            RefreshInterval = null,
            Ttl = ttl,
            Compress = compress,
            EncryptionMode = encryptionMode,
            EncryptionPassword = encryptionPassword,
            KeyFile = keyFile,
            AccessLevel = accessLevel,
            SourceQuery = sourceSelect,
            Mode = mode,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    // ── EXPORT DATASET ────────────────────────────────────────────────────

    public Statement ParseExportDataset(Token startToken)
    {
        var name = ConsumeIdentifier("Expected &datasetName after EXPORT DATASET").Value;
        if (!name.StartsWith("&"))
            throw new SyntaxException("EXPORT DATASET names must use the &dataset form", startToken.Line, startToken.Column);

        Consume(TokenType.TO, "Expected TO after EXPORT DATASET name");
        var targetPath = Consume(TokenType.STRING_LITERAL, "Expected file path after TO").Value;

        var encryptionMode = DatasetEncryptionMode.None;
        string? password = null;
        string? keyFile = null;

        while (!ReportCheck(TokenType.SEMICOLON) && !ReportAtEnd())
        {
            if (Match(TokenType.ENCRYPT))
            {
                Match(TokenType.EQUALS);
                var modeVal = _parser.Current.Value.ToUpperInvariant();
                _parser.Advance();
                encryptionMode = modeVal switch
                {
                    "PASSWORD" => DatasetEncryptionMode.Password,
                    "KEYFILE" => DatasetEncryptionMode.KeyFile,
                    _ => throw new SyntaxException(
                        "EXPORT DATASET requires ENCRYPT = PASSWORD or KEYFILE (a transport credential)",
                        _parser.Previous.Line, _parser.Previous.Column)
                };
            }
            else if (Match(TokenType.PASSWORD))
            {
                Match(TokenType.EQUALS);
                password = Consume(TokenType.STRING_LITERAL, "Expected password string after PASSWORD =").Value;
            }
            else if (Match(TokenType.KEYFILE))
            {
                Match(TokenType.EQUALS);
                keyFile = Consume(TokenType.STRING_LITERAL, "Expected key file path after KEYFILE =").Value;
            }
            else
            {
                throw new SyntaxException(
                    $"Unexpected token '{_parser.Current.Value}' in EXPORT DATASET options",
                    _parser.Current.Line, _parser.Current.Column);
            }
        }

        Match(TokenType.SEMICOLON);

        return new ExportDatasetStatement
        {
            DatasetName = name,
            TargetPath = targetPath,
            EncryptionMode = encryptionMode,
            EncryptionPassword = password,
            KeyFile = keyFile,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    // ── PUBLISH DATASET ───────────────────────────────────────────────────

    public Statement ParsePublishDataset(Token startToken)
    {
        if (ReportCheck(TokenType.FROM))
            throw new SyntaxException(
                "PUBLISH DATASET FROM ... AS &name has been retired. Use PUBLISH DATASET &name FROM 'file.parquet'.",
                _parser.Current.Line,
                _parser.Current.Column);

        var name = ConsumeIdentifier("Expected &datasetName after PUBLISH DATASET").Value;
        if (!name.StartsWith("&"))
            throw new SyntaxException("PUBLISH DATASET names must use the &dataset form", startToken.Line, startToken.Column);

        Consume(TokenType.FROM, "Expected FROM after PUBLISH DATASET name");
        var sourcePath = Consume(TokenType.STRING_LITERAL, "Expected source file path after FROM").Value;

        string? targetFolder = null;
        var accessLevel = ETL_SQL.Core.Data.DatasetAccessLevel.Private;
        var encryptionMode = DatasetEncryptionMode.None;
        string? password = null;
        string? keyFile = null;

        while (!ReportCheck(TokenType.SEMICOLON) && !ReportAtEnd())
        {
            if (Match(TokenType.INTO))
            {
                targetFolder = Consume(TokenType.STRING_LITERAL, "Expected folder path after INTO").Value;
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
            else if (Match(TokenType.ENCRYPT))
            {
                Match(TokenType.EQUALS);
                var modeVal = _parser.Current.Value.ToUpperInvariant();
                _parser.Advance();
                encryptionMode = modeVal switch
                {
                    "PASSWORD" => DatasetEncryptionMode.Password,
                    "KEYFILE" => DatasetEncryptionMode.KeyFile,
                    _ => throw new SyntaxException(
                        "PUBLISH DATASET requires ENCRYPT = PASSWORD or KEYFILE (the transport credential the file was exported with)",
                        _parser.Previous.Line, _parser.Previous.Column)
                };
            }
            else if (Match(TokenType.PASSWORD))
            {
                Match(TokenType.EQUALS);
                password = Consume(TokenType.STRING_LITERAL, "Expected password string after PASSWORD =").Value;
            }
            else if (Match(TokenType.KEYFILE))
            {
                Match(TokenType.EQUALS);
                keyFile = Consume(TokenType.STRING_LITERAL, "Expected key file path after KEYFILE =").Value;
            }
            else
            {
                throw new SyntaxException(
                    $"Unexpected token '{_parser.Current.Value}' in PUBLISH DATASET options",
                    _parser.Current.Line, _parser.Current.Column);
            }
        }

        Match(TokenType.SEMICOLON);

        return new PublishDatasetStatement
        {
            SourcePath = sourcePath,
            DatasetName = name,
            TargetFolder = targetFolder,
            AccessLevel = accessLevel,
            EncryptionMode = encryptionMode,
            EncryptionPassword = password,
            KeyFile = keyFile,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    // ── CREATE STYLE ──────────────────────────────────────────────────────

    public Statement ParseCreateStyle(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
    {
        var name = ConsumeIdentifier("Expected style name after CREATE STYLE").Value;
        Consume(TokenType.AS, "Expected AS after style name");
        Consume(TokenType.LPAREN, "Expected '(' after AS");
        var styles = new Dictionary<string, string>();
        var palette = ImmutableArray<string>.Empty;
        ParseStyleBody(styles, ref palette);
        Consume(TokenType.RPAREN, "Expected ')' to close CREATE STYLE");
        Match(TokenType.SEMICOLON);
        return new CreateStyleStatement
        {
            Name = name,
            Styles = styles,
            Palette = palette,
            Mode = mode,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    public Statement ParseStyleStatement(Token startToken)
    {
        var styles = new Dictionary<string, string>();
        string? styleName = null;
        var palette = ImmutableArray<string>.Empty;
        ParseStyleClause(styles, ref styleName, ref palette);
        Match(TokenType.SEMICOLON);

        return new CreateStyleStatement
        {
            Name = "GLOBAL",
            Styles = styles,
            Palette = palette,
            StyleName = styleName,
            Mode = ObjectCreationMode.Create,
            Line = startToken.Line,
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
            Name = name,
            Options = options,
            Mode = mode,
            Line = startToken.Line,
            Column = startToken.Column
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
            Name = name,
            Properties = properties,
            Mode = mode,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    // ── CREATE CONTAINER ──────────────────────────────────────────────────

    public Statement ParseCreateContainer(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
    {
        var name = ConsumeIdentifier("Expected container name after CREATE CONTAINER").Value;
        Consume(TokenType.AS, "Expected AS after container name");

        string containerType;
        if (Match(TokenType.BOX)) containerType = "BOX";
        else if (Match(TokenType.SCROLL)) containerType = "SCROLL";
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
        TitleDefinition? titleDef = null, subtitleDef = null;
        TooltipDefinition? tooltip = null;
        string? structure = null;
        var slotMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var styles = new Dictionary<string, string>();
        var containerPalette = ImmutableArray<string>.Empty;
        bool isCollapsible = containerType == "DRAWER";
        bool isPinnable = true;
        string? visibility = "ON";
        string? icon = null;


        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            if (Match(TokenType.STYLE))
            {
                ParseStyleClause(styles, ref containerStyleName, ref containerPalette);
            }
            else if (Match(TokenType.TITLE))
            {
                (title, titleMd, titleDef) = ParseVisualPropertyWithMd("TITLE");
            }
            else if (Match(TokenType.SUBTITLE))
            {
                (subtitle, subtitleMd, subtitleDef) = ParseVisualPropertyWithMd("SUBTITLE");
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
            Name = name,
            ContainerType = containerType,
            Structure = structure,
            SlotMap = slotMap,
            Styles = styles,
            Palette = containerPalette,
            StyleName = containerStyleName,
            Title = title,
            TitleIsMarkdown = titleMd,
            TitleDefinition = titleDef,
            Subtitle = subtitle,
            SubtitleIsMarkdown = subtitleMd,
            SubtitleDefinition = subtitleDef,
            Tooltip = tooltip,
            IsCollapsible = isCollapsible,
            Visibility = visibility,
            Icon = icon,
            IsPinnable = isPinnable,
            Mode = mode,
            Line = startToken.Line,
            Column = startToken.Column
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
        if (Match(TokenType.NAV_TAB)) navType = NavigationType.Tab;
        else if (Match(TokenType.BUTTON)) navType = NavigationType.Button;
        else if (Match(TokenType.LINK_NAV)) navType = NavigationType.Link;
        else
        {
            var raw = ConsumeIdentifier("Expected TAB, BUTTON, or LINK after AS").Value.ToUpperInvariant();
            navType = raw switch
            {
                "TAB" => NavigationType.Tab,
                "BUTTON" => NavigationType.Button,
                "LINK" => NavigationType.Link,
                _ => NavigationType.Tab
            };
        }

        Consume(TokenType.LPAREN, "Expected '(' after navigation type");

        var orientation = NavigationOrientation.Horizontal;
        string? defaultPage = null;
        var pages = new List<string>();

        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            if ((_parser.Current.Type == TokenType.IDENTIFIER || _parser.Current.Type == TokenType.ORIENTATION) &&
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
            Name = name,
            NavType = navType,
            Orientation = orientation,
            DefaultPage = defaultPage,
            Pages = pages,
            Mode = mode,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    // ── CREATE BUTTON ─────────────────────────────────────────────────────

    public Statement ParseCreateButton(Token startToken, ObjectCreationMode mode = ObjectCreationMode.Create)
    {
        var name = ConsumeIdentifier("Expected button name after CREATE BUTTON").Value;
        Consume(TokenType.AS, "Expected AS after button name");

        const string buttonType = "BUTTON";
        Consume(TokenType.LPAREN, "Expected '(' after AS. Put behavior in ACTIONS (ON_CLICK = ...).");

        Expression? title = null;
        TooltipDefinition? tooltip = null;
        string? styleName = null;
        var options = new List<VisualOption>();
        var actions = new List<VisualAction>();
        var styles = new Dictionary<string, string>();
        var palette = ImmutableArray<string>.Empty;

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
                ParseStyleClause(styles, ref styleName, ref palette);
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
            Name = name,
            ButtonType = buttonType,
            Title = title,
            Tooltip = tooltip,
            Options = options,
            Actions = actions,
            Styles = styles,
            Palette = palette,
            StyleName = styleName,
            Mode = mode,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    // ── CREATE BOOKMARK ───────────────────────────────────────────────────

    public Statement ParseCreateBookmark(Token startToken)
    {
        var name = ConsumeIdentifier("Expected bookmark name after CREATE BOOKMARK").Value;
        Consume(TokenType.AS, "Expected AS after bookmark name");
        Consume(TokenType.LPAREN, "Expected '(' after AS");

        Expression? title = null;
        string? pageName = null;
        bool isDefault = false;
        var parameters = new List<BookmarkParameterAssignment>();
        var stateEntries = new List<BookmarkStateEntry>();
        var seenParams = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenState = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            if (Match(TokenType.TITLE))
            {
                Consume(TokenType.EQUALS, "Expected '=' after TITLE");
                title = ParseExpression();
            }
            else if (MatchIdentifier("PARAMETERS"))
            {
                Consume(TokenType.LPAREN, "Expected '(' after PARAMETERS");
                while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                {
                    var paramName = ConsumeIdentifierOrVariable("Expected parameter name").Value;
                    if (!paramName.StartsWith("@")) paramName = "@" + paramName;
                    if (!seenParams.Add(paramName))
                        throw new SyntaxException($"Bookmark parameter '{paramName}' is assigned more than once.", _parser.Previous.Line, _parser.Previous.Column);
                    Consume(TokenType.EQUALS, "Expected '=' after parameter name");
                    var value = ParseBookmarkParameterValue(paramName);
                    parameters.Add(new BookmarkParameterAssignment(paramName, value));
                    if (!Match(TokenType.COMMA)) break;
                }
                Consume(TokenType.RPAREN, "Expected ')' to close PARAMETERS");
            }
            else if (Match(TokenType.PAGE))
            {
                Consume(TokenType.EQUALS, "Expected '=' after PAGE");
                pageName = ConsumeIdentifier("Expected page name").Value;
            }
            else if (Match(TokenType.DEFAULT))
            {
                Consume(TokenType.EQUALS, "Expected '=' after DEFAULT");
                var defaultValue = Advance().Value;
                if (string.Equals(defaultValue, "ON", StringComparison.OrdinalIgnoreCase))
                    isDefault = true;
                else if (string.Equals(defaultValue, "OFF", StringComparison.OrdinalIgnoreCase))
                    isDefault = false;
                else
                    throw new SyntaxException($"DEFAULT must be ON or OFF, got '{defaultValue}'.", _parser.Previous.Line, _parser.Previous.Column);
            }
            else if (MatchIdentifier("STATE"))
            {
                Consume(TokenType.LPAREN, "Expected '(' after STATE");
                while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                {
                    var objectName = ConsumeIdentifier("Expected object name in STATE entry").Value;
                    Consume(TokenType.DOT, $"Expected '.PROPERTY' after object name '{objectName}' in STATE. Only ObjectName.VISIBLE and ObjectName.COLLAPSED are allowed.");
                    var propToken = ConsumeIdentifier("Expected VISIBLE or COLLAPSED after '.'");
                    var property = propToken.Value.ToUpperInvariant() switch
                    {
                        "VISIBLE" => BookmarkStateProperty.Visible,
                        "COLLAPSED" => BookmarkStateProperty.Collapsed,
                        _ => throw new SyntaxException($"Invalid STATE property '{propToken.Value}'. Only VISIBLE and COLLAPSED are allowed.", propToken.Line, propToken.Column)
                    };
                    if (Match(TokenType.DOT))
                        throw new SyntaxException("STATE references may only be 'ObjectName.PROPERTY'; nested properties are not allowed.", _parser.Previous.Line, _parser.Previous.Column);
                    Consume(TokenType.EQUALS, "Expected '=' after state property");
                    var valueToken = Advance();
                    var on = valueToken.Value.ToUpperInvariant() switch
                    {
                        "ON" => true,
                        "OFF" => false,
                        _ => throw new SyntaxException($"STATE value for '{objectName}.{property.ToString().ToUpperInvariant()}' must be ON or OFF, got '{valueToken.Value}'.", valueToken.Line, valueToken.Column)
                    };
                    var key = $"{objectName}.{property}";
                    if (!seenState.Add(key))
                        throw new SyntaxException($"STATE entry '{objectName}.{property.ToString().ToUpperInvariant()}' is set more than once.", propToken.Line, propToken.Column);
                    stateEntries.Add(new BookmarkStateEntry(objectName, property, on));
                    if (!Match(TokenType.COMMA)) break;
                }
                Consume(TokenType.RPAREN, "Expected ')' to close STATE");
            }
            else
            {
                throw new SyntaxException($"Unexpected token '{_parser.Current.Value}' in CREATE BOOKMARK body. Expected TITLE, PARAMETERS, PAGE, STATE, or DEFAULT.", _parser.Current.Line, _parser.Current.Column);
            }
            Match(TokenType.COMMA);
        }

        Consume(TokenType.RPAREN, "Expected ')' to close CREATE BOOKMARK");
        Match(TokenType.SEMICOLON);

        return new CreateBookmarkStatement
        {
            Name = name,
            Title = title,
            PageName = pageName,
            IsDefault = isDefault,
            Parameters = parameters,
            StateEntries = stateEntries,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    /// <summary>
    /// Parses one bookmark parameter value, constraining it to a typed scalar literal (number, string,
    /// boolean, or NULL) or a declared variable reference. This keeps bookmark PARAMETERS a state
    /// declaration rather than a hidden transformation engine, while retaining the value's type.
    /// </summary>
    private Expression ParseBookmarkParameterValue(string paramName)
    {
        var start = _parser.Current;
        var expr = ParseExpression();
        var normalized = NormalizeBookmarkValue(expr);
        if (normalized is null)
            throw new SyntaxException(
                $"Bookmark parameter '{paramName}' must be a typed scalar literal (number, string, boolean, or NULL) or a declared variable reference.",
                start.Line, start.Column);
        return normalized;
    }

    private static Expression? NormalizeBookmarkValue(Expression expr) => expr switch
    {
        LiteralExpression => expr,
        VariableExpression => expr,
        // A negative number is parsed as (0 - <number literal>); collapse it to a signed literal.
        BinaryExpression
        {
            Operator: TokenType.MINUS,
            Left: LiteralExpression { Type: TokenType.NUMBER },
            Right: LiteralExpression { Type: TokenType.NUMBER, Value: decimal d }
        } b when IsZero(((LiteralExpression)b.Left).Value)
            => new LiteralExpression(-d, TokenType.NUMBER) { Line = expr.Line, Column = expr.Column },
        _ => null
    };

    private static bool IsZero(object? value) => value switch
    {
        decimal d => d == 0m,
        int i => i == 0,
        long l => l == 0,
        double db => db == 0,
        _ => false
    };

    // ── ALTER (Report Objects) ────────────────────────────────────────────

    /// <summary>
    /// A clause that may appear in an <c>ALTER &lt;report object&gt;</c> body. The enum name is the
    /// keyword, so diagnostics can list what a kind accepts without a second spelling table.
    /// </summary>
    private enum AlterClause
    {
        Source, Mappings, Options, Actions, Style, Title, Subtitle, Tooltip, Visible, Refresh, Icon
    }

    /// <summary>
    /// Report object kinds whose <c>ALTER</c> is implemented by
    /// <c>AlterReportObjectStatementHandler</c>, and the clauses each one accepts. Every other kind
    /// is refused here, and so is every clause a kind has no field for.
    /// </summary>
    /// <remarks>
    /// The handler's <c>default</c> arm threw "ALTER not yet implemented", which meant unsupported
    /// kinds parsed, linted and completed successfully, then failed at execution — the worst place
    /// to learn a statement is unsupported, since a report script may already have run half its
    /// work. The parser is the only stage that can say so before anything happens.
    /// <para>
    /// The per-kind clause lists exist for the same reason one level down. A single visual-shaped
    /// body was previously parsed for every kind, so <c>ALTER PAGE p (SOURCE = ...)</c> parsed and
    /// the handler then silently discarded the clause: the statement reported success having
    /// changed nothing. A clause is listed only where the handler actually patches it.
    /// </para>
    /// <para>
    /// This table mirrors the handler's switch; adding a kind or a patched field there means adding
    /// it here, and the tests pin both directions.
    /// </para>
    /// </remarks>
    private static readonly (ReportObjectType Type, AlterClause[] Clauses)[] AlterableReportObjects =
    [
        (ReportObjectType.Visual,
            [AlterClause.Source, AlterClause.Mappings, AlterClause.Options, AlterClause.Actions,
             AlterClause.Style, AlterClause.Title, AlterClause.Subtitle, AlterClause.Tooltip]),
        (ReportObjectType.Page,
            [AlterClause.Style, AlterClause.Title, AlterClause.Subtitle, AlterClause.Tooltip,
             AlterClause.Visible, AlterClause.Refresh]),
        (ReportObjectType.Container,
            [AlterClause.Style, AlterClause.Title, AlterClause.Subtitle, AlterClause.Tooltip,
             AlterClause.Visible, AlterClause.Icon]),
        (ReportObjectType.Button,
            [AlterClause.Options, AlterClause.Actions, AlterClause.Style, AlterClause.Title,
             AlterClause.Tooltip]),
        (ReportObjectType.Template, [AlterClause.Options]),
    ];

    /// <summary>
    /// The canonical recreate form per kind, used in the refusal diagnostic. The kinds differ —
    /// <c>STYLE</c> takes no <c>AS</c>, <c>NAVIGATION</c> names its type after <c>AS</c> — and a
    /// message that suggests a form the parser rejects is worse than no suggestion at all.
    /// </summary>
    private static string RecreateForm(ReportObjectType type) => type switch
    {
        ReportObjectType.Style => "CREATE OR REPLACE STYLE <name> (...)",
        ReportObjectType.Navigation => "CREATE OR REPLACE NAVIGATION <name> AS TAB|BUTTON|LINK (...)",
        ReportObjectType.Dataset => "CREATE OR REPLACE DATASET &<name> AS (SELECT ...)",
        _ => $"CREATE OR REPLACE {type.ToString().ToUpperInvariant()} <name> AS (...)"
    };

    public Statement ParseAlterReportObject(ReportObjectType type)
    {
        var startToken = _parser.Previous;

        var supported = Array.Find(AlterableReportObjects, e => e.Type == type).Clauses;
        if (supported is null)
            throw new SyntaxException(
                $"ALTER is not supported for {type.ToString().ToUpperInvariant()}. " +
                $"Supported: {string.Join(", ", AlterableReportObjects.Select(e => e.Type.ToString().ToUpperInvariant()))}. " +
                $"Recreate the object instead — {RecreateForm(type)}.",
                startToken.Line, startToken.Column);

        var name = ConsumeIdentifier($"Expected {type} name after ALTER {type}").Value;
        Consume(TokenType.LPAREN, $"Expected '(' after {type} name");

        VisualSourceExpression? source = null;
        var mappings = new List<VisualMapping>();
        var options = new List<VisualOption>();
        var axisOptions = new List<AxisOptions>();
        var actions = new List<VisualAction>();
        var styles = new Dictionary<string, string>();
        string? styleName = null;
        Expression? title = null, subtitle = null;
        bool titleMd = false, subtitleMd = false;
        TooltipDefinition? tooltip = null;
        string? visibility = null;
        int? refreshSecs = null;
        string? icon = null;

        // The clause keyword has already been consumed when this runs, so the position reported is
        // the offending keyword itself.
        void RequireClause(AlterClause clause)
        {
            if (Array.IndexOf(supported, clause) >= 0) return;

            throw new SyntaxException(
                $"ALTER {type.ToString().ToUpperInvariant()} does not support " +
                $"{clause.ToString().ToUpperInvariant()}. Supported clauses: " +
                $"{string.Join(", ", supported.Select(c => c.ToString().ToUpperInvariant()))}. " +
                $"To change anything else, recreate the object — {RecreateForm(type)}.",
                _parser.Previous.Line, _parser.Previous.Column);
        }

        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            if (Match(TokenType.SOURCE))
            {
                RequireClause(AlterClause.Source);
                Match(TokenType.EQUALS);
                source = ParseVisualSource();
            }
            else if (Match(TokenType.TITLE))
            {
                RequireClause(AlterClause.Title);
                Match(TokenType.EQUALS);
                title = ParseExpression();
            }
            else if (Match(TokenType.SUBTITLE))
            {
                RequireClause(AlterClause.Subtitle);
                Match(TokenType.EQUALS);
                subtitle = ParseExpression();
            }
            else if (Match(TokenType.TOOLTIP))
            {
                RequireClause(AlterClause.Tooltip);
                tooltip = ParseTooltipDefinition();
            }
            else if (Match(TokenType.MAPPINGS))
            {
                RequireClause(AlterClause.Mappings);
                Consume(TokenType.LPAREN, "Expected '(' after MAPPINGS");
                mappings.AddRange(ParseMappings());
                Consume(TokenType.RPAREN, "Expected ')' to close MAPPINGS");
            }
            else if (Match(TokenType.OPTIONS))
            {
                RequireClause(AlterClause.Options);
                Consume(TokenType.LPAREN, "Expected '(' after OPTIONS");
                ParseOptions(options, axisOptions);
                Consume(TokenType.RPAREN, "Expected ')' to close OPTIONS");
            }
            else if (Match(TokenType.ACTIONS))
            {
                RequireClause(AlterClause.Actions);
                Consume(TokenType.LPAREN, "Expected '(' after ACTIONS");
                actions.AddRange(ParseActions());
                Consume(TokenType.RPAREN, "Expected ')' to close ACTIONS");
            }
            else if (Match(TokenType.STYLE))
            {
                RequireClause(AlterClause.Style);
                ParseStyleClause(styles, ref styleName);
            }
            else if (Match(TokenType.VISIBLE))
            {
                RequireClause(AlterClause.Visible);
                Match(TokenType.EQUALS);
                visibility = ParseOnOffValue();
            }
            else if (Match(TokenType.REFRESH))
            {
                RequireClause(AlterClause.Refresh);
                Match(TokenType.EQUALS);
                var raw = ConsumeReportOptionValue();
                // CREATE PAGE swallows an unparseable interval and silently means "off". ALTER is a
                // patch, so the same silence would report success and leave the old interval in
                // place — the one outcome the author cannot see.
                if (!int.TryParse(raw, out var parsedRefresh) || parsedRefresh < 0)
                    throw new SyntaxException(
                        $"ALTER PAGE REFRESH expects a whole number of seconds (0 disables it), not '{raw}'.",
                        _parser.Previous.Line, _parser.Previous.Column);
                refreshSecs = parsedRefresh;
            }
            else if (Match(TokenType.ICON))
            {
                RequireClause(AlterClause.Icon);
                Consume(TokenType.EQUALS, "Expected '=' after ICON");
                icon = Consume(TokenType.STRING_LITERAL, "Expected string literal for ICON").Value;
            }
            else
            {
                throw new SyntaxException($"Unexpected token '{_parser.Current.Value}' in ALTER {type} body", _parser.Current.Line, _parser.Current.Column);
            }
            Match(TokenType.COMMA);
        }

        Consume(TokenType.RPAREN, $"Expected ')' to close ALTER {type}");
        Match(TokenType.SEMICOLON);

        if (type == ReportObjectType.Button)
            ValidateButtonActionTriggers(actions, startToken);

        return new AlterReportObjectStatement
        {
            ObjectType = type,
            Name = name,
            Source = source,
            Mappings = mappings.Count > 0 ? mappings : null,
            Options = options.Count > 0 ? options : null,
            AxisOptions = axisOptions.Count > 0 ? axisOptions : null,
            Actions = actions.Count > 0 ? actions : null,
            Styles = styles.Count > 0 ? styles : null,
            StyleName = styleName,
            Title = title,
            TitleIsMarkdown = titleMd,
            Subtitle = subtitle,
            SubtitleIsMarkdown = subtitleMd,
            Tooltip = tooltip,
            Visibility = visibility,
            RefreshIntervalSeconds = refreshSecs,
            Icon = icon,
            Line = startToken.Line,
            Column = startToken.Column
        };
    }

    // ── Private helpers ───────────────────────────────────────────────────

    private VisualType ParseVisualType()
    {
        if (Match(TokenType.BAR)) return VisualType.Bar;
        if (Match(TokenType.LINE)) return VisualType.Line;
        if (Match(TokenType.SCATTER)) return VisualType.Scatter;
        if (Match(TokenType.PIE)) return VisualType.Pie;
        if (Match(TokenType.TABLE_VISUAL)) return VisualType.Table;
        if (Match(TokenType.TABLE)) return VisualType.Table;
        if (Match(TokenType.CARD)) return VisualType.Card;
        if (Match(TokenType.SLICER)) return VisualType.Slicer;
        if (Match(TokenType.HEATMAP)) return VisualType.HeatMap;
        if (Match(TokenType.DONUT)) return VisualType.Donut;
        if (Match(TokenType.HBAR)) return VisualType.HorizontalBar;
        if (Match(TokenType.BOXPLOT)) return VisualType.BoxPlot;
        if (Match(TokenType.TREEMAP)) return VisualType.Treemap;
        if (Match(TokenType.TEXT)) return VisualType.Text;
        if (Match(TokenType.COMBO)) return VisualType.Combo;
        if (Match(TokenType.DATEPICKER)) return VisualType.DatePicker;
        if (Match(TokenType.RELDATEPICKER)) return VisualType.RelDatePicker;
        if (Match(TokenType.SLIDER)) return VisualType.Slider;
        if (Match(TokenType.MULTISELECT)) return VisualType.MultiSelect;
        if (Match(TokenType.SEARCH)) return VisualType.Search;
        if (Match(TokenType.GAUGE)) return VisualType.Gauge;
        if (Match(TokenType.FUNNEL)) return VisualType.Funnel;
        if (Match(TokenType.WATERFALL)) return VisualType.Waterfall;
        if (Match(TokenType.IMAGE)) return VisualType.Image;
        if (Match(TokenType.BUBBLE)) return VisualType.Bubble;
        if (Match(TokenType.RADAR)) return VisualType.Radar;
        if (Match(TokenType.CANDLESTICK)) return VisualType.Candlestick;
        if (Match(TokenType.GANTT)) return VisualType.Gantt;
        if (Match(TokenType.SANKEY)) return VisualType.Sankey;
        if (Match(TokenType.SUNBURST)) return VisualType.Sunburst;
        if (Match(TokenType.NETWORK)) return VisualType.Network;
        if (Match(TokenType.TRELLIS)) return VisualType.Trellis;
        if (Match(TokenType.MATRIX)) return VisualType.Matrix;
        if (Match(TokenType.CHECKBOX)) return VisualType.Checkbox;
        if (Match(TokenType.TEXTBOX)) return VisualType.Textbox;
        if (Match(TokenType.NUMBERBOX)) return VisualType.Numberbox;
        if (IsCurrentValue("CUSTOM")) { Advance(); return VisualType.Custom; }
        if (IsCurrentValue("HTML")) { Advance(); return VisualType.Html; }

        // MAP token already exists for container MAP() clauses; match it here only when
        // ParseVisualType() is called (i.e. after AS in CREATE VISUAL ... AS MAP).
        if (Match(TokenType.MAP)) return VisualType.Map;

        if (_parser.Current.Type == TokenType.IDENTIFIER)
        {
            var val = _parser.Current.Value.ToUpperInvariant();
            Advance();
            return val switch
            {
                "BAR" => VisualType.Bar,
                "LINE" => VisualType.Line,
                "SCATTER" => VisualType.Scatter,
                "PIE" => VisualType.Pie,
                "TABLE" => VisualType.Table,
                "CARD" => VisualType.Card,
                "SLICER" => VisualType.Slicer,
                "HEATMAP" => VisualType.HeatMap,
                "DONUT" => VisualType.Donut,
                "HBAR" => VisualType.HorizontalBar,
                "BOXPLOT" => VisualType.BoxPlot,
                "TREEMAP" => VisualType.Treemap,
                "TEXT" => VisualType.Text,
                "COMBO" => VisualType.Combo,
                "DATEPICKER" => VisualType.DatePicker,
                "RELDATEPICKER" => VisualType.RelDatePicker,
                "SLIDER" => VisualType.Slider,
                "MULTISELECT" => VisualType.MultiSelect,
                "SEARCH" => VisualType.Search,
                "GAUGE" => VisualType.Gauge,
                "FUNNEL" => VisualType.Funnel,
                "WATERFALL" => VisualType.Waterfall,
                "BUBBLE" => VisualType.Bubble,
                "RADAR" => VisualType.Radar,
                "CANDLESTICK" => VisualType.Candlestick,
                "MAP" => VisualType.Map,
                "GANTT" => VisualType.Gantt,
                "SANKEY" => VisualType.Sankey,
                "SUNBURST" => VisualType.Sunburst,
                "NETWORK" => VisualType.Network,
                "TRELLIS" => VisualType.Trellis,
                "MATRIX" => VisualType.Matrix,
                "CHECKBOX" => VisualType.Checkbox,
                "TEXTBOX" => VisualType.Textbox,
                "NUMBERBOX" => VisualType.Numberbox,
                "CUSTOM" => VisualType.Custom,
                "HTML" => VisualType.Html,
                _ => throw new SyntaxException(
                         $"Unknown visual type '{val}'.",
                         _parser.Previous.Line, _parser.Previous.Column)
            };
        }

        throw new SyntaxException(
            $"Expected visual type (BAR, LINE, SCATTER, PIE, TABLE, CARD, SLICER, HEATMAP, DONUT, HBAR, BOXPLOT, TREEMAP, TEXT, COMBO, DATEPICKER, RELDATEPICKER, SLIDER, MULTISELECT, SEARCH, GAUGE, FUNNEL, WATERFALL, BUBBLE, RADAR, CANDLESTICK, MAP, GANTT, SANKEY, SUNBURST, NETWORK, TRELLIS, MATRIX, CHECKBOX, TEXTBOX, NUMBERBOX, CUSTOM, HTML) but got '{_parser.Current.Value}'",
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
                var subLexer = new Lexer(val);
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

    private TitleDefinition ParseTitleDefinition(string propertyName)
    {
        if (Match(TokenType.EQUALS))
        {
            if (ReportCheck(TokenType.LPAREN))
            {
                Advance(); // consume '('
                if (IsTitleBlockStart())
                {
                    var def = ParseTitleBlockBody();
                    Consume(TokenType.RPAREN, $"Expected ')' after {propertyName}");
                    return def;
                }
                var expr = _parser.ParseExpression();
                Consume(TokenType.RPAREN, $"Expected ')' after {propertyName}");
                return new TitleDefinition { Text = expr, IsMarkdown = true };
            }
            var simpleExpr = _parser.ParseExpression();
            bool simpleMd = false;
            if (Match(TokenType.MARKDOWN)) simpleMd = true;
            return new TitleDefinition { Text = simpleExpr, IsMarkdown = simpleMd };
        }

        if (Match(TokenType.LPAREN))
        {
            if (IsTitleBlockStart())
            {
                var def = ParseTitleBlockBody();
                Consume(TokenType.RPAREN, $"Expected ')' after {propertyName}");
                return def;
            }
            var expr = _parser.ParseExpression();
            Consume(TokenType.RPAREN, $"Expected ')' after {propertyName}");
            return new TitleDefinition { Text = expr, IsMarkdown = true };
        }

        var bareExpr = _parser.ParseExpression();
        bool bareMd = false;
        if (Match(TokenType.MARKDOWN)) bareMd = true;
        return new TitleDefinition { Text = bareExpr, IsMarkdown = bareMd };
    }

    private bool IsTitleBlockStart()
    {
        var val = _parser.Current.Value.ToUpperInvariant();
        if (val is "TEXT" or "CONTENT" or "VALUE" or "TITLE" or "COLOR" or "TEXT_COLOR" or "FONT"
            or "FONT_FAMILY" or "SIZE" or "FONT_SIZE" or "WEIGHT" or "FONT_WEIGHT" or "ALIGN"
            or "TEXT_ALIGN" or "MARKDOWN" or "IS_MARKDOWN" or "FONT_STYLE")
        {
            return _parser.Peek.Type == TokenType.EQUALS || _parser.Peek.Type == TokenType.MINUS;
        }
        return false;
    }

    private TitleDefinition ParseTitleBlockBody()
    {
        Expression? text = null;
        bool isMd = false;
        string? color = null;
        string? font = null;
        string? size = null;
        string? weight = null;
        string? align = null;

        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            var keyTok = _parser.IsIdentifier(_parser.Current) || LanguageMetadata.IsKeyword(_parser.Current.Value)
                ? _parser.Advance()
                : throw new SyntaxException("Expected title property key", _parser.Current.Line, _parser.Current.Column);
            var key = keyTok.Value.ToUpperInvariant();
            while (_parser.Current.Type == TokenType.MINUS &&
                   (_parser.IsIdentifier(_parser.Peek) || LanguageMetadata.IsKeyword(_parser.Peek.Value)))
            {
                Advance(); // consume '-'
                key += "_" + _parser.Advance().Value.ToUpperInvariant();
            }

            Consume(TokenType.EQUALS, $"Expected '=' after title property '{key}'");

            switch (key)
            {
                case "TEXT" or "CONTENT" or "VALUE" or "TITLE":
                    text = _parser.ParseExpression();
                    if (Match(TokenType.MARKDOWN)) isMd = true;
                    break;
                case "COLOR" or "TEXT_COLOR":
                    color = _parser.Advance().Value;
                    break;
                case "FONT" or "FONT_FAMILY":
                    font = _parser.Advance().Value;
                    break;
                case "SIZE" or "FONT_SIZE":
                    size = _parser.Advance().Value;
                    break;
                case "WEIGHT" or "FONT_WEIGHT":
                    weight = _parser.Advance().Value;
                    break;
                case "ALIGN" or "TEXT_ALIGN":
                    align = _parser.Advance().Value;
                    break;
                case "MARKDOWN" or "IS_MARKDOWN":
                    var mdVal = _parser.Advance().Value.ToUpperInvariant();
                    isMd = mdVal is "ON" or "TRUE" or "1" or "YES";
                    break;
                default:
                    _parser.Advance();
                    break;
            }

            Match(TokenType.COMMA);
        }

        return new TitleDefinition
        {
            Text = text,
            IsMarkdown = isMd,
            Color = color,
            Font = font,
            Size = size,
            Weight = weight,
            Align = align
        };
    }

    private (Expression? Value, bool IsMarkdown, TitleDefinition? Definition) ParseVisualPropertyWithMd(string propertyName)
    {
        var def = ParseTitleDefinition(propertyName);
        return (def.Text, def.IsMarkdown, def);
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
            Role = "SPARKLINE",
            Column = "$sparkline",
            SparklineColumns = cols,
            SparklineType = sparklineType,
            DisplayName = displayName
        };
    }

    private VisualMapping ParseCardSparklineMapping()
    {
        Advance(); // SPARKLINE
        Consume(TokenType.EQUALS, "Expected '=' after SPARKLINE");
        var source = ConsumeIdentifier("Expected a temp table or dataset after SPARKLINE =").Value;
        Consume(TokenType.LPAREN, "Expected '(' after the CARD sparkline source");
        string? x = null, y = null;
        var type = "line";
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            var key = ConsumeIdentifier("Expected X, Y, or TYPE in CARD SPARKLINE").Value.ToUpperInvariant();
            Consume(TokenType.EQUALS, $"Expected '=' after CARD SPARKLINE {key}");
            if (key == "TYPE")
            {
                type = ConsumeIdentifier("Expected LINE, BAR, or AREA after TYPE =").Value.ToLowerInvariant();
                if (type is not "line" and not "bar" and not "area")
                    throw new SyntaxException("CARD SPARKLINE TYPE must be LINE, BAR, or AREA", _parser.Previous.Line, _parser.Previous.Column);
            }
            else
            {
                var column = ConsumeIdentifier($"Expected a column name after {key} =").Value;
                if (key == "X") x = column;
                else if (key == "Y") y = column;
                else throw new SyntaxException($"Unsupported CARD SPARKLINE option '{key}'", _parser.Previous.Line, _parser.Previous.Column);
            }
            Match(TokenType.COMMA);
        }
        Consume(TokenType.RPAREN, "Expected ')' after CARD SPARKLINE options");
        if (x is null || y is null)
            throw new SyntaxException("CARD SPARKLINE requires both X and Y mappings", _parser.Current.Line, _parser.Current.Column);

        return new VisualMapping
        {
            Role = "SPARKLINE",
            Column = y,
            SparklineSource = source,
            SparklineXColumn = x,
            SparklineYColumn = y,
            SparklineType = type
        };
    }

    private IEnumerable<VisualMapping> ParseMappings()
    {
        var result = new List<VisualMapping>();
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            // SPARKLINE(col1, col2, ...) [LINE|BAR|AREA] [AS 'alias']
            if (_parser.Current.Type == TokenType.SPARKLINE && _parser.Peek.Type == TokenType.EQUALS)
            {
                result.Add(ParseCardSparklineMapping());
                Match(TokenType.COMMA);
                continue;
            }
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
                //                                [AS 'alias'] [HIDDEN]
                string? format = null, align = null, displayName = null;
                string? dataBarColor = null, colorScaleFrom = null, colorScaleTo = null;
                string? cellRenderer = null, hyperlinkLabel = null, progressColor = null;
                int? imageWidth = null;
                bool dataBar = false, hidden = false, progressBar = false;
                decimal? progressMinimum = null, progressMaximum = null;
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
                    else if (IsCurrentValue("HIDDEN"))
                    {
                        Advance();
                        hidden = true;
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
                    else if (IsCurrentValue("PROGRESS_BAR"))
                    {
                        Advance();
                        progressBar = true;
                        Consume(TokenType.LPAREN, "Expected '(' after PROGRESS_BAR");
                        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                        {
                            var option = ConsumeIdentifier("Expected MIN, MAX, or COLOR in PROGRESS_BAR").Value.ToUpperInvariant();
                            Consume(TokenType.EQUALS, $"Expected '=' after PROGRESS_BAR {option}");
                            if (option == "COLOR")
                            {
                                progressColor = Consume(TokenType.STRING_LITERAL, "Expected a color string after PROGRESS_BAR COLOR =").Value;
                            }
                            else
                            {
                                var raw = _parser.Current.Type is TokenType.NUMBER or TokenType.MINUS
                                    ? ParseSignedNumberText()
                                    : throw new SyntaxException($"Expected a numeric value after PROGRESS_BAR {option} =", _parser.Current.Line, _parser.Current.Column);
                                if (!decimal.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var numeric))
                                    throw new SyntaxException($"Invalid PROGRESS_BAR {option} value '{raw}'", _parser.Previous.Line, _parser.Previous.Column);
                                if (option == "MIN") progressMinimum = numeric;
                                else if (option == "MAX") progressMaximum = numeric;
                                else throw new SyntaxException($"Unsupported PROGRESS_BAR option '{option}'", _parser.Previous.Line, _parser.Previous.Column);
                            }
                            Match(TokenType.COMMA);
                        }
                        Consume(TokenType.RPAREN, "Expected ')' after PROGRESS_BAR options");
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
                    Role = name.ToUpperInvariant(),
                    Column = name,
                    Format = format,
                    Align = align,
                    DisplayName = displayName,
                    DataBar = dataBar,
                    DataBarColor = dataBarColor,
                    ColorScaleFrom = colorScaleFrom,
                    ColorScaleTo = colorScaleTo,
                    CellRenderer = cellRenderer,
                    ImageWidth = imageWidth,
                    HyperlinkLabel = hyperlinkLabel,
                    ProgressBar = progressBar,
                    ProgressMinimum = progressMinimum,
                    ProgressMaximum = progressMaximum,
                    ProgressColor = progressColor,
                    Hidden = hidden
                });
            }
            Match(TokenType.COMMA);
        }
        return result;
    }

    private string ParseSignedNumberText()
    {
        var sign = Match(TokenType.MINUS) ? "-" : string.Empty;
        return sign + Consume(TokenType.NUMBER, "Expected a number").Value;
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
            bool isY2 = _parser.Current.Type == TokenType.IDENTIFIER && _parser.Current.Value.Equals("Y2_AXIS", StringComparison.OrdinalIgnoreCase);

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
            else if (isY2)
            {
                Advance();
                var axisOpts = new AxisOptions { Axis = "Y2" };
                Consume(TokenType.LPAREN, "Expected '(' after Y2_AXIS");
                ParseAxisOptionBody(axisOpts.Options);
                Consume(TokenType.RPAREN, "Expected ')' to close Y2_AXIS");
                axisOptions.Add(axisOpts);
            }
            else if (Match(TokenType.COLORS))
            {
                Match(TokenType.EQUALS);
                if (Match(TokenType.LPAREN))
                {
                    var posList = new List<string>();
                    var isPositional = false;
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        if (!isPositional && posList.Count == 0 && (_parser.Current.Type == TokenType.STRING_LITERAL || _parser.IsIdentifier(_parser.Current)))
                        {
                            var lookahead = _parser.Peek;
                            if (lookahead.Type != TokenType.EQUALS)
                            {
                                isPositional = true;
                            }
                        }

                        if (isPositional)
                        {
                            var colorVal = ConsumeReportOptionValue();
                            posList.Add(colorVal);
                        }
                        else
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
                        }
                        Match(TokenType.COMMA);
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close COLORS");

                    if (isPositional)
                    {
                        if (posList.Count == 2)
                        {
                            options.Add(new VisualOption { Key = "color:low", Value = posList[0] });
                            options.Add(new VisualOption { Key = "color:min", Value = posList[0] });
                            options.Add(new VisualOption { Key = "color:high", Value = posList[1] });
                            options.Add(new VisualOption { Key = "color:max", Value = posList[1] });
                        }
                        else if (posList.Count >= 3)
                        {
                            options.Add(new VisualOption { Key = "color:low", Value = posList[0] });
                            options.Add(new VisualOption { Key = "color:min", Value = posList[0] });
                            options.Add(new VisualOption { Key = "color:mid", Value = posList[1] });
                            options.Add(new VisualOption { Key = "color:high", Value = posList[2] });
                            options.Add(new VisualOption { Key = "color:max", Value = posList[2] });
                        }
                        options.Add(new VisualOption { Key = "COLORS", Value = string.Join(", ", posList) });
                    }
                }
                else
                {
                    var colorVal = ConsumeReportOptionValue();
                    options.Add(new VisualOption { Key = "COLORS", Value = colorVal });
                }
            }
            else if (Match(TokenType.DATA_LABELS))
            {
                Match(TokenType.EQUALS);
                if (ReportCheck(TokenType.WITH))
                {
                    throw new SyntaxException("DATA_LABELS requires a toggle value (ON or OFF) before WITH clause", _parser.Current.Line, _parser.Current.Column);
                }
                var val = ConsumeReportOptionValue();
                options.Add(new VisualOption { Key = "DATA_LABELS", Value = NormalizeBoolOptionValue(val) });
                if (Match(TokenType.WITH))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after WITH in DATA_LABELS");
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        var subKeyToken = _parser.Advance();
                        var subKey = subKeyToken.Value.ToUpperInvariant();
                        if (subKey == "LEADER_LINE")
                        {
                            Consume(TokenType.EQUALS, "Expected '=' after DATA_LABELS LEADER_LINE");
                        }
                        else
                        {
                            Match(TokenType.EQUALS);
                        }
                        var subVal = ConsumeReportOptionValue();
                        options.Add(new VisualOption
                        {
                            Key = "DATA_LABELS:" + subKey,
                            Value = subKey == "LEADER_LINE" ? NormalizeBoolOptionValue(subVal) : subVal
                        });
                        if (subKey == "LEADER_LINE" && Match(TokenType.WITH))
                        {
                            Consume(TokenType.LPAREN, "Expected '(' after WITH in DATA_LABELS LEADER_LINE");
                            while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                            {
                                var leaderKeyToken = _parser.Advance();
                                var leaderKey = leaderKeyToken.Value.ToUpperInvariant();
                                if (leaderKey is not ("COLOR" or "STYLE"))
                                {
                                    throw new SyntaxException($"Unknown LEADER_LINE option '{leaderKey}'. Valid options are COLOR and STYLE.", leaderKeyToken.Line, leaderKeyToken.Column);
                                }
                                Consume(TokenType.EQUALS, $"Expected '=' after LEADER_LINE {leaderKey}");
                                var leaderValue = ConsumeReportOptionValue();
                                options.Add(new VisualOption
                                {
                                    Key = "DATA_LABELS:LEADER_LINE:" + leaderKey,
                                    Value = leaderValue
                                });
                                Match(TokenType.COMMA);
                            }
                            Consume(TokenType.RPAREN, "Expected ')' to close DATA_LABELS LEADER_LINE WITH block");
                        }
                        Match(TokenType.COMMA);
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close DATA_LABELS WITH block");
                }
            }
            else if (IsCurrentValue("SERIES_LABELS"))
            {
                Advance();
                Consume(TokenType.EQUALS, "Expected '=' after SERIES_LABELS");
                if (ReportCheck(TokenType.WITH))
                {
                    throw new SyntaxException("SERIES_LABELS requires a toggle value (ON or OFF) before WITH clause", _parser.Current.Line, _parser.Current.Column);
                }
                var val = ConsumeReportOptionValue();
                options.Add(new VisualOption { Key = "SERIES_LABELS", Value = NormalizeBoolOptionValue(val) });
                if (Match(TokenType.WITH))
                {
                    Consume(TokenType.LPAREN, "Expected '(' after WITH in SERIES_LABELS");
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        var subKeyToken = _parser.Advance();
                        var subKey = subKeyToken.Value.ToUpperInvariant();
                        if (subKey is not ("POSITION"))
                        {
                            throw new SyntaxException($"Unknown SERIES_LABELS option '{subKey}'. Valid option is POSITION.", subKeyToken.Line, subKeyToken.Column);
                        }
                        Consume(TokenType.EQUALS, $"Expected '=' after SERIES_LABELS {subKey}");
                        options.Add(new VisualOption
                        {
                            Key = "SERIES_LABELS:" + subKey,
                            Value = ConsumeReportOptionValue()
                        });
                        Match(TokenType.COMMA);
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close SERIES_LABELS WITH block");
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
            else if (_parser.Current.Type == TokenType.IDENTIFIER && _parser.Current.Value.Equals("JITTER", StringComparison.OrdinalIgnoreCase) && (_parser.Peek.Type == TokenType.EQUALS || _parser.Peek.Type == TokenType.LPAREN))
            {
                Advance();
                if (Match(TokenType.EQUALS))
                {
                    var val = ConsumeReportOptionValue();
                    options.Add(new VisualOption { Key = "JITTER", Value = NormalizeBoolOptionValue(val) });
                    if (Match(TokenType.WITH))
                    {
                        Consume(TokenType.LPAREN, "Expected '(' after WITH in JITTER");
                        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                        {
                            var subKeyToken = _parser.Advance();
                            var subKey = subKeyToken.Value.ToUpperInvariant();
                            Consume(TokenType.EQUALS, $"Expected '=' after JITTER {subKey}");
                            var subVal = ConsumeReportOptionValue();
                            options.Add(new VisualOption { Key = "JITTER:" + subKey, Value = subVal });
                            Match(TokenType.COMMA);
                        }
                        Consume(TokenType.RPAREN, "Expected ')' to close JITTER WITH block");
                    }
                }
                else if (Match(TokenType.LPAREN))
                {
                    options.Add(new VisualOption { Key = "JITTER", Value = "ON" });
                    while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                    {
                        var subKeyToken = _parser.Advance();
                        var subKey = subKeyToken.Value.ToUpperInvariant();
                        Consume(TokenType.EQUALS, $"Expected '=' after JITTER {subKey}");
                        var subVal = ConsumeReportOptionValue();
                        options.Add(new VisualOption { Key = "JITTER:" + subKey, Value = subVal });
                        Match(TokenType.COMMA);
                    }
                    Consume(TokenType.RPAREN, "Expected ')' to close JITTER block");
                }
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
                if (key == "STACKED" && _parser.Current.Type == TokenType.NUMBER && _parser.Current.Value == "100" &&
                    _parser.Peek.Type == TokenType.IDENTIFIER && _parser.Peek.Value.Equals("PCT", StringComparison.OrdinalIgnoreCase))
                {
                    Advance();
                    Advance();
                    options.Add(new VisualOption { Key = key, Value = "100PCT" });
                    Match(TokenType.COMMA);
                    continue;
                }
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
            string val;
            if (_overlayKeywordTokens.Contains(_parser.Current.Type) || _parser.Current.Type == TokenType.ON || _parser.Current.Type == TokenType.OFF)
            {
                val = ConsumeReportOptionValue();
            }
            else
            {
                var expr = ParseExpression();
                val = expr is LiteralExpression lit ? lit.Value?.ToString() ?? "" : expr.ToSql();
            }
            opts.Add(new VisualOption { Key = key, Value = val });
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
            t == TokenType.STRING_LITERAL || t == TokenType.NUMBER ||
            t == TokenType.TRUE || t == TokenType.FALSE ||
            t == TokenType.ON || t == TokenType.OFF ||
            t == TokenType.FILTER ||
            t == TokenType.SOURCE ||
            t == TokenType.TOP || t == TokenType.BOTTOM ||
            t == TokenType.LEFT || t == TokenType.RIGHT ||
            t == TokenType.GRID || t == TokenType.DATA_LABELS ||
            t == TokenType.NONE || t == TokenType.HEADER || t == TokenType.FOOTER ||
            t == TokenType.ALL ||
            t == TokenType.START || t == TokenType.END ||
            t == TokenType.CENTER || t == TokenType.FONT_SIZE ||
            t == TokenType.INSIDE || t == TokenType.INSIDE_TOP || t == TokenType.INSIDE_BOTTOM ||
            t == TokenType.INSIDE_LEFT || t == TokenType.INSIDE_RIGHT ||
            t == TokenType.INSIDE_TOP_LEFT || t == TokenType.INSIDE_TOP_RIGHT ||
            t == TokenType.INSIDE_BOTTOM_LEFT || t == TokenType.INSIDE_BOTTOM_RIGHT ||
            t == TokenType.DATA_LABELS_POSITION || t == TokenType.FONT_FAMILY ||
            t == TokenType.FONT_WEIGHT || t == TokenType.GAUGE_STYLE ||
            t == TokenType.SHOW_NO_DATA_PLACEHOLDER ||
            t == TokenType.VISIBLE ||
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
            "TRUE" or "ON" or "1" => "ON",
            "FALSE" or "OFF" or "0" => "OFF",
            _ => val
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
            if (Match(TokenType.ON_CLICK)) trigger = "ON_CLICK";
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
        else if (Match(TokenType.APPLY_BOOKMARK))
        {
            Consume(TokenType.LPAREN, "Expected '(' after APPLY_BOOKMARK");
            var bookmarkName = ConsumeIdentifier("Expected bookmark name").Value;
            Consume(TokenType.RPAREN, "Expected ')' to close APPLY_BOOKMARK");
            action = new ApplyBookmarkAction { Trigger = trigger, BookmarkName = bookmarkName };
        }
        else
        {
            throw new SyntaxException(
                $"Expected DRILL_DOWN, DRILL_IN, SET_PARAMETER, CLEAR_FILTERS, APPLY_PARAMETERS, APPLY_BOOKMARK, BACK, REFRESH_REPORT, REFRESH_VISUALS, EXPORT_CSV, EXPORT_EXCEL, EXPORT_PDF, NAVIGATE_PAGE, or SET_UI_STATE after {trigger} =",
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
            if (Match(TokenType.BAR)) seriesType = "bar";
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
            double? parameter = null;
            string? forecastField = null;
            string? confidenceLowField = null;
            string? confidenceHighField = null;
            string? anomalyField = null;
            string? tableCalculationField = null;

            if (Match(TokenType.GOAL))
            {
                overlayType = OverlayType.Goal;
                Consume(TokenType.LPAREN, "Expected '(' after GOAL");
                parameter = double.Parse(Consume(TokenType.NUMBER, "Expected numeric value for GOAL").Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                Consume(TokenType.RPAREN, "Expected ')' after GOAL value");
            }
            else if (Match(TokenType.AVERAGE)) { overlayType = OverlayType.Average; }
            else if (Match(TokenType.MOVING_AVG))
            {
                overlayType = OverlayType.MovingAvg;
                Consume(TokenType.LPAREN, "Expected '(' after MOVING_AVG");
                parameter = double.Parse(Consume(TokenType.NUMBER, "Expected window size for MOVING_AVG").Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                Consume(TokenType.RPAREN, "Expected ')' after MOVING_AVG window");
            }
            else if (Match(TokenType.LINEAR)) { overlayType = OverlayType.Linear; }
            else if (Match(TokenType.EXPONENTIAL)) { overlayType = OverlayType.Exponential; }
            else if (Match(TokenType.LOGARITHMIC)) { overlayType = OverlayType.Logarithmic; }
            else if (Match(TokenType.POWER)) { overlayType = OverlayType.Power; }
            else if (Match(TokenType.POLYNOMIAL))
            {
                overlayType = OverlayType.Polynomial;
                Consume(TokenType.LPAREN, "Expected '(' after POLYNOMIAL");
                parameter = double.Parse(Consume(TokenType.NUMBER, "Expected degree for POLYNOMIAL").Value,
                    System.Globalization.CultureInfo.InvariantCulture);
                Consume(TokenType.RPAREN, "Expected ')' after POLYNOMIAL degree");
            }
            else if (IsCurrentValue("FORECAST"))
            {
                Advance();
                overlayType = OverlayType.Forecast;
                Consume(TokenType.LPAREN, "Expected '(' after FORECAST");
                forecastField = ConsumeIdentifier("Expected forecast field name").Value;
                Consume(TokenType.RPAREN, "Expected ')' after forecast field name");
            }
            else if (IsCurrentValue("RUNNING_TOTAL"))
            {
                Advance();
                overlayType = OverlayType.RunningTotal;
                Consume(TokenType.LPAREN, "Expected '(' after RUNNING_TOTAL");
                tableCalculationField = ConsumeIdentifier("Expected pre-computed running-total field name").Value;
                Consume(TokenType.RPAREN, "Expected ')' after running-total field name");
            }
            else if (IsCurrentValue("PERCENT_OF_TOTAL"))
            {
                Advance();
                overlayType = OverlayType.PercentOfTotal;
                Consume(TokenType.LPAREN, "Expected '(' after PERCENT_OF_TOTAL");
                tableCalculationField = ConsumeIdentifier("Expected pre-computed percent-of-total field name").Value;
                Consume(TokenType.RPAREN, "Expected ')' after percent-of-total field name");
            }
            else if (IsCurrentValue("REFERENCE_BAND"))
            {
                var referenceBandToken = Advance();
                result.Add(ParseReferenceBand(referenceBandToken));
                Match(TokenType.COMMA);
                continue;
            }
            else if (IsCurrentValue("REFERENCE_LINE"))
            {
                var refLineToken = Advance();
                Consume(TokenType.LPAREN, "Expected '(' after REFERENCE_LINE");

                // Reject leading comma
                if (ReportCheck(TokenType.COMMA))
                {
                    throw new SyntaxException("Unexpected ',' at start of REFERENCE_LINE", _parser.Current.Line, _parser.Current.Column);
                }

                bool hasValue = false;
                bool hasLabel = false;
                bool hasStyle = false;
                bool hasColor = false;
                double? refParameter = null;
                string? refLabel = null;
                var refLineStyle = OverlayLineStyle.Dashed;
                string? refColor = null;

                while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
                {
                    if (IsCurrentValue("VALUE"))
                    {
                        var valueToken = Advance();
                        if (hasValue)
                            throw new SyntaxException("Duplicate VALUE property in REFERENCE_LINE", valueToken.Line, valueToken.Column);
                        Consume(TokenType.EQUALS, "Expected '=' after VALUE");

                        var sign = Match(TokenType.MINUS) ? "-" : Match(TokenType.PLUS) ? "+" : string.Empty;
                        if (!ReportCheck(TokenType.NUMBER))
                            throw new SyntaxException($"Expected numeric value for VALUE in REFERENCE_LINE but got '{_parser.Current.Value}'", _parser.Current.Line, _parser.Current.Column);

                        var numToken = Consume(TokenType.NUMBER, "Expected numeric value for VALUE in REFERENCE_LINE");
                        var numStr = sign + numToken.Value;
                        if (!double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedVal) || !double.IsFinite(parsedVal))
                            throw new SyntaxException($"Expected finite numeric value for VALUE in REFERENCE_LINE but got '{numStr}'", numToken.Line, numToken.Column);

                        refParameter = parsedVal;
                        hasValue = true;
                    }
                    else if (IsCurrentValue("LABEL"))
                    {
                        var labelToken = Advance();
                        if (hasLabel)
                            throw new SyntaxException("Duplicate LABEL property in REFERENCE_LINE", labelToken.Line, labelToken.Column);
                        Consume(TokenType.EQUALS, "Expected '=' after LABEL");
                        refLabel = Consume(TokenType.STRING_LITERAL, "Expected string literal for LABEL in REFERENCE_LINE").Value;
                        hasLabel = true;
                    }
                    else if (IsCurrentValue("STYLE"))
                    {
                        var styleToken = Advance();
                        if (hasStyle)
                            throw new SyntaxException("Duplicate STYLE property in REFERENCE_LINE", styleToken.Line, styleToken.Column);
                        Consume(TokenType.EQUALS, "Expected '=' after STYLE");
                        if (_parser.Current.Type == TokenType.SOLID || IsCurrentValue("SOLID"))
                        {
                            Advance();
                            refLineStyle = OverlayLineStyle.Solid;
                        }
                        else if (_parser.Current.Type == TokenType.DASHED || IsCurrentValue("DASHED"))
                        {
                            Advance();
                            refLineStyle = OverlayLineStyle.Dashed;
                        }
                        else if (_parser.Current.Type == TokenType.DOTTED || IsCurrentValue("DOTTED"))
                        {
                            Advance();
                            refLineStyle = OverlayLineStyle.Dotted;
                        }
                        else
                        {
                            throw new SyntaxException($"Expected SOLID, DASHED, or DOTTED for STYLE in REFERENCE_LINE but got '{_parser.Current.Value}'", _parser.Current.Line, _parser.Current.Column);
                        }
                        hasStyle = true;
                    }
                    else if (_parser.Current.Type == TokenType.COLOR || IsCurrentValue("COLOR"))
                    {
                        var colorToken = Advance();
                        if (hasColor)
                            throw new SyntaxException("Duplicate COLOR property in REFERENCE_LINE", colorToken.Line, colorToken.Column);
                        Consume(TokenType.EQUALS, "Expected '=' after COLOR");
                        refColor = Consume(TokenType.STRING_LITERAL, "Expected string literal for COLOR in REFERENCE_LINE").Value;
                        hasColor = true;
                    }
                    else
                    {
                        throw new SyntaxException($"Unknown property '{_parser.Current.Value}' in REFERENCE_LINE. Expected VALUE, LABEL, STYLE, or COLOR.", _parser.Current.Line, _parser.Current.Column);
                    }

                    if (ReportCheck(TokenType.COMMA))
                    {
                        var commaToken = Advance();
                        if (ReportCheck(TokenType.RPAREN))
                        {
                            throw new SyntaxException("Trailing comma before ')' is not permitted in REFERENCE_LINE", commaToken.Line, commaToken.Column);
                        }
                        if (ReportCheck(TokenType.COMMA))
                        {
                            throw new SyntaxException("Consecutive commas are not permitted in REFERENCE_LINE", _parser.Current.Line, _parser.Current.Column);
                        }
                    }
                    else if (!ReportCheck(TokenType.RPAREN))
                    {
                        throw new SyntaxException($"Expected ',' or ')' after REFERENCE_LINE property but got '{_parser.Current.Value}'", _parser.Current.Line, _parser.Current.Column);
                    }
                }

                Consume(TokenType.RPAREN, "Expected ')' to close REFERENCE_LINE");
                if (!hasValue)
                    throw new SyntaxException("REFERENCE_LINE requires a VALUE property: REFERENCE_LINE (VALUE = number, ...)", refLineToken.Line, refLineToken.Column);

                result.Add(new VisualOverlay
                {
                    OverlayType = OverlayType.ReferenceLine,
                    Parameter = refParameter,
                    LineStyle = refLineStyle,
                    Color = refColor,
                    Label = refLabel
                });

                Match(TokenType.COMMA);
                continue;
            }
            else throw new SyntaxException(
                $"Expected overlay type (GOAL, AVERAGE, MOVING_AVG, RUNNING_TOTAL, PERCENT_OF_TOTAL, REFERENCE_LINE, REFERENCE_BAND, FORECAST, LINEAR, ...) but got '{_parser.Current.Value}'",
                _parser.Current.Line, _parser.Current.Column);

            Consume(TokenType.AS, "Expected AS after overlay type");
            OverlayLineStyle lineStyle;
            if (Match(TokenType.SOLID)) lineStyle = OverlayLineStyle.Solid;
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
                    else if (IsCurrentValue("LABEL"))
                    {
                        Advance();
                        Consume(TokenType.EQUALS, "Expected = after LABEL");
                        label = Consume(TokenType.STRING_LITERAL, "Expected label string").Value;
                    }
                    else if (IsCurrentValue("CONFIDENCE_LOW"))
                    {
                        Advance();
                        Consume(TokenType.EQUALS, "Expected = after CONFIDENCE_LOW");
                        confidenceLowField = ConsumeIdentifier("Expected confidence low field name").Value;
                    }
                    else if (IsCurrentValue("CONFIDENCE_HIGH"))
                    {
                        Advance();
                        Consume(TokenType.EQUALS, "Expected = after CONFIDENCE_HIGH");
                        confidenceHighField = ConsumeIdentifier("Expected confidence high field name").Value;
                    }
                    else if (IsCurrentValue("ANOMALY"))
                    {
                        Advance();
                        Consume(TokenType.EQUALS, "Expected = after ANOMALY");
                        anomalyField = ConsumeIdentifier("Expected anomaly field name").Value;
                    }
                    else break;
                    Match(TokenType.COMMA);
                }
                Consume(TokenType.RPAREN, "Expected ')' to close WITH");
            }

            result.Add(new VisualOverlay
            {
                OverlayType = overlayType,
                Parameter = parameter,
                LineStyle = lineStyle,
                Color = color,
                Label = label,
                ForecastField = forecastField,
                ConfidenceLowField = confidenceLowField,
                ConfidenceHighField = confidenceHighField,
                AnomalyField = anomalyField,
                TableCalculationField = tableCalculationField
            });
            Match(TokenType.COMMA);
        }
        return result;
    }

    private VisualOverlay ParseReferenceBand(Token referenceBandToken)
    {
        Consume(TokenType.LPAREN, "Expected '(' after REFERENCE_BAND");
        if (ReportCheck(TokenType.COMMA))
            throw new SyntaxException("Unexpected ',' at start of REFERENCE_BAND", _parser.Current.Line, _parser.Current.Column);

        double? low = null;
        double? high = null;
        string? color = null;
        string? label = null;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            var propertyToken = _parser.Current;
            var property = propertyToken.Value.ToUpperInvariant();
            if (property is not ("LOW" or "HIGH" or "COLOR" or "LABEL"))
                throw new SyntaxException($"Unknown property '{propertyToken.Value}' in REFERENCE_BAND. Expected LOW, HIGH, COLOR, or LABEL.", propertyToken.Line, propertyToken.Column);
            Advance();
            if (!seen.Add(property))
                throw new SyntaxException($"Duplicate {property} property in REFERENCE_BAND", propertyToken.Line, propertyToken.Column);
            Consume(TokenType.EQUALS, $"Expected '=' after {property}");

            switch (property)
            {
                case "LOW":
                    low = ParseFiniteOverlayNumber("LOW", "REFERENCE_BAND");
                    break;
                case "HIGH":
                    high = ParseFiniteOverlayNumber("HIGH", "REFERENCE_BAND");
                    break;
                case "COLOR":
                    color = Consume(TokenType.STRING_LITERAL, "Expected string literal for COLOR in REFERENCE_BAND").Value;
                    break;
                case "LABEL":
                    label = Consume(TokenType.STRING_LITERAL, "Expected string literal for LABEL in REFERENCE_BAND").Value;
                    break;
            }

            if (ReportCheck(TokenType.COMMA))
            {
                var commaToken = Advance();
                if (ReportCheck(TokenType.RPAREN))
                    throw new SyntaxException("Trailing comma before ')' is not permitted in REFERENCE_BAND", commaToken.Line, commaToken.Column);
                if (ReportCheck(TokenType.COMMA))
                    throw new SyntaxException("Consecutive commas are not permitted in REFERENCE_BAND", _parser.Current.Line, _parser.Current.Column);
            }
            else if (!ReportCheck(TokenType.RPAREN))
            {
                throw new SyntaxException($"Expected ',' or ')' after REFERENCE_BAND property but got '{_parser.Current.Value}'", _parser.Current.Line, _parser.Current.Column);
            }
        }

        Consume(TokenType.RPAREN, "Expected ')' to close REFERENCE_BAND");
        if (!low.HasValue || !high.HasValue)
            throw new SyntaxException("REFERENCE_BAND requires both LOW and HIGH properties: REFERENCE_BAND (LOW = number, HIGH = number, ...)", referenceBandToken.Line, referenceBandToken.Column);

        return new VisualOverlay
        {
            OverlayType = OverlayType.ReferenceBand,
            BandLow = low,
            BandHigh = high,
            Color = color,
            Label = label
        };
    }

    private double ParseFiniteOverlayNumber(string property, string overlay)
    {
        var sign = Match(TokenType.MINUS) ? "-" : Match(TokenType.PLUS) ? "+" : string.Empty;
        if (!ReportCheck(TokenType.NUMBER))
            throw new SyntaxException($"Expected numeric value for {property} in {overlay} but got '{_parser.Current.Value}'", _parser.Current.Line, _parser.Current.Column);
        var token = Consume(TokenType.NUMBER, $"Expected numeric value for {property} in {overlay}");
        var text = sign + token.Value;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw new SyntaxException($"Expected finite numeric value for {property} in {overlay} but got '{text}'", token.Line, token.Column);
        return value;
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

    private void ParseStyleClause(Dictionary<string, string> styles, ref string? styleName, ref ImmutableArray<string> palette)
    {
        if (Match(TokenType.EQUALS))
            styleName = ConsumeIdentifier("Expected style name after STYLE =").Value;
        else
        {
            Consume(TokenType.LPAREN, "Expected '(' or '=' after STYLE");
            ParseStyleBody(styles, ref palette);
            Consume(TokenType.RPAREN, "Expected ')' to close STYLE");
        }
    }

    private void ParseStyleClause(Dictionary<string, string> styles, ref string? styleName)
    {
        var dummyPalette = ImmutableArray<string>.Empty;
        ParseStyleClause(styles, ref styleName, ref dummyPalette);
    }

    private ImmutableArray<string> ParsePaletteSequence()
    {
        Consume(TokenType.LPAREN, "Expected '(' after PALETTE");
        if (ReportCheck(TokenType.RPAREN))
        {
            throw new SyntaxException("PALETTE sequence cannot be empty", _parser.Current.Line, _parser.Current.Column);
        }

        var paletteItems = new List<string>();
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            if (_parser.Current.Type is TokenType.STRING_LITERAL or TokenType.STRING)
            {
                paletteItems.Add(_parser.Advance().Value);
            }
            else if (_parser.Current.Type is TokenType.IDENTIFIER or TokenType.VARIABLE || LanguageMetadata.IsKeyword(_parser.Current.Value))
            {
                paletteItems.Add(_parser.Advance().Value);
            }
            else
            {
                throw new SyntaxException($"Expected color string in PALETTE sequence, got '{_parser.Current.Value}'", _parser.Current.Line, _parser.Current.Column);
            }

            if (!Match(TokenType.COMMA))
            {
                if (!ReportCheck(TokenType.RPAREN))
                {
                    throw new SyntaxException("Expected ',' or ')' in PALETTE sequence", _parser.Current.Line, _parser.Current.Column);
                }
            }
            else if (ReportCheck(TokenType.RPAREN))
            {
                // Trailing comma before ')'
                break;
            }
        }
        Consume(TokenType.RPAREN, "Expected ')' to close PALETTE sequence");
        return paletteItems.ToImmutableArray();
    }

    private void ParseStyleBody(Dictionary<string, string> styles, ref ImmutableArray<string> palette)
    {
        while (!ReportCheck(TokenType.RPAREN) && !ReportAtEnd())
        {
            // Accept any token as the start of a style key (keywords like THEME, TRUE, etc.)
            var keyTok = _parser.IsIdentifier(_parser.Current) || LanguageMetadata.IsKeyword(_parser.Current.Value)
                ? _parser.Advance()
                : throw new SyntaxException("Expected style key", _parser.Current.Line, _parser.Current.Column);
            var key = keyTok.Value;
            // Consume hyphenated or colon segments: BACKGROUND - COLOR → "BACKGROUND-COLOR", COLOR : Domestic → "COLOR:Domestic"
            while ((_parser.Current.Type == TokenType.MINUS || _parser.Current.Type == TokenType.COLON) &&
                   (_parser.IsIdentifier(_parser.Peek) || LanguageMetadata.IsKeyword(_parser.Peek.Value) || _parser.Peek.Type == TokenType.STRING_LITERAL))
            {
                var sep = _parser.Advance().Value; // '-' or ':'
                key += sep + _parser.Advance().Value;
            }
            Consume(TokenType.EQUALS, $"Expected '=' after style key '{key}'");

            if (key.Equals("PALETTE", StringComparison.OrdinalIgnoreCase))
            {
                palette = ParsePaletteSequence();
            }
            else
            {
                string val;
                val = _parser.Current.Value;
                Advance();
                styles[key] = val;
            }
            Match(TokenType.COMMA);
        }
    }

    private void ParseStyleBody(Dictionary<string, string> styles)
    {
        var dummyPalette = ImmutableArray<string>.Empty;
        ParseStyleBody(styles, ref dummyPalette);
    }
}
