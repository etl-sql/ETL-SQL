using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;

using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core.Parser
{
    /// <summary>
    /// Lexer for the ETL-SQL language. Converts source code into a stream of tokens.
    /// </summary>
    public class Lexer
    {
        private readonly string _source;
        private int _position;
        private int _line;
        private int _column;

        private static readonly Dictionary<string, TokenType> Keywords = InitializeKeywords();

        private static Dictionary<string, TokenType> InitializeKeywords()
        {
            var dict = new Dictionary<string, TokenType>(StringComparer.OrdinalIgnoreCase);
            
            // Add all Keywords from all categories
            foreach (var kw in LanguageMetadata.GetAllKeywords())
            {
                if (Enum.TryParse<TokenType>(kw, true, out var type))
                    dict[kw] = type;
            }
            
            // Add all Functions
            foreach (var f in LanguageMetadata.Functions)
            {
                if (Enum.TryParse<TokenType>(f, true, out var type))
                    dict[f] = type;
            }

            // Explicit mappings for safety or variations
            dict["GROUP"] = TokenType.GROUP;
            dict["BY"] = TokenType.BY;
            dict["ORDER"] = TokenType.ORDER;
            dict["ROLLUP"] = TokenType.ROLLUP;
            dict["CUBE"] = TokenType.CUBE;
            dict["GROUPING"] = TokenType.GROUPING;
            dict["FILE"] = TokenType.FILE;  // Reserved table name — not a keyword, but must lex as FILE for deprecation detection
            dict["SEND_FILE"] = TokenType.SEND_FILE;
            dict["RECEIVE_FILE"] = TokenType.RECEIVE_FILE;
            dict["FILE_SEND"] = TokenType.SEND_FILE;
            dict["FILE_RECEIVE"] = TokenType.RECEIVE_FILE;
            dict["SFTP"] = TokenType.SFTP;
            dict["FTP_CONN"] = TokenType.FTP_CONN;
            dict["AZURE_BLOB"] = TokenType.AZURE_BLOB;
            dict["CLOSE"] = TokenType.CLOSE;
            dict["EACH"] = TokenType.EACH;
            dict["FOREACH"] = TokenType.FOREACH;
            dict["RAISERROR"] = TokenType.RAISEERROR;
            dict["NUMBER"] = TokenType.NUMERIC;

            // ── Report-SQL keywords (Phase 9A) ─────────────────────────────
            // These are registered so the lexer produces typed tokens inside
            // CREATE VISUAL / CREATE PAGE / CREATE DATASET statements.
            // They are non-reserved: outside those contexts the parser treats them as identifiers.
            dict["VISUAL"]        = TokenType.VISUAL;
            dict["PAGE"]          = TokenType.PAGE;
            dict["DATASET"]       = TokenType.DATASET;
            dict["LAYOUT"]        = TokenType.LAYOUT;
            dict["MAPPINGS"]      = TokenType.MAPPINGS;
            dict["OPTIONS"]       = TokenType.OPTIONS;
            dict["ACTIONS"]       = TokenType.ACTIONS;
            dict["STRUCTURE"]     = TokenType.STRUCTURE;
            dict["MAP"]           = TokenType.MAP;
            dict["SERIES"]        = TokenType.SERIES;
            // SOURCE is already registered via LanguageMetadata (maps to TokenType.SOURCE)
            dict["BAR"]           = TokenType.BAR;
            dict["LINE"]          = TokenType.LINE;
            dict["SCATTER"]       = TokenType.SCATTER;
            dict["PIE"]           = TokenType.PIE;
            dict["SLICER"]        = TokenType.SLICER;
            dict["CARD"]          = TokenType.CARD;
            dict["HEATMAP"]       = TokenType.HEATMAP;
            dict["DONUT"]         = TokenType.DONUT;
            dict["HBAR"]          = TokenType.HBAR;
            dict["BOXPLOT"]       = TokenType.BOXPLOT;
            dict["TREEMAP"]       = TokenType.TREEMAP;
            dict["COLORS"]        = TokenType.COLORS;
            dict["BOTTOM"]        = TokenType.BOTTOM;   // position keyword; TOP/LEFT/RIGHT come from LanguageMetadata
            dict["ON_CLICK"]      = TokenType.ON_CLICK;
            dict["DRILL_DOWN"]    = TokenType.DRILL_DOWN;
            dict["DRILL_IN"]      = TokenType.DRILL_IN;
            dict["SET_PARAMETER"] = TokenType.SET_PARAMETER;
            dict["ON_CHANGE"]     = TokenType.ON_CHANGE;
            dict["REFRESH"]       = TokenType.REFRESH;
            // EVERY and COMPRESS are already registered via LanguageMetadata
            dict["TTL"]           = TokenType.TTL;
            dict["KEYFILE"]       = TokenType.KEYFILE;
            dict["X_AXIS"]        = TokenType.X_AXIS;
            dict["TEMPLATE"]      = TokenType.TEMPLATE;
            dict["TEMPLATE_PATH"] = TokenType.TEMPLATE_PATH;
            dict["Y_AXIS"]        = TokenType.Y_AXIS;
            dict["REPORT"]        = TokenType.REPORT;
            dict["EXPORT"]        = TokenType.EXPORT;
            dict["PAGES"]         = TokenType.PAGES;
            dict["DESCRIPTION"]   = TokenType.DESCRIPTION;

            // ── Report-SQL keywords (Phase 9.3) ────────────────────────────
            dict["STYLE"]      = TokenType.STYLE;
            dict["CONTAINER"]  = TokenType.CONTAINER;
            dict["BOX"]        = TokenType.BOX;
            dict["SCROLL"]     = TokenType.SCROLL;
            dict["NAVIGATION"] = TokenType.NAVIGATION;
            dict["COMBO"]      = TokenType.COMBO;
            dict["TAB"]         = TokenType.NAV_TAB;
            dict["BUTTON"]      = TokenType.BUTTON;
            dict["LINK"]        = TokenType.LINK_NAV;
            dict["DATEPICKER"]    = TokenType.DATEPICKER;
            dict["RELDATEPICKER"] = TokenType.RELDATEPICKER;
            dict["SLIDER"]      = TokenType.SLIDER;
            dict["MULTISELECT"] = TokenType.MULTISELECT;
            dict["SEARCH"]      = TokenType.SEARCH;
            dict["GAUGE"]        = TokenType.GAUGE;
            dict["FUNNEL"]       = TokenType.FUNNEL;
            dict["WATERFALL"]    = TokenType.WATERFALL;
            dict["RADAR"]        = TokenType.RADAR;
            dict["BUBBLE"]       = TokenType.BUBBLE;
            dict["CANDLESTICK"]  = TokenType.CANDLESTICK;
            dict["FORMATTING"]  = TokenType.FORMATTING;
            dict["EXPECT"]      = TokenType.EXPECT;
            dict["PLACEHOLDER"] = TokenType.PLACEHOLDER;
            dict["COLLAPSIBLE"] = TokenType.COLLAPSIBLE;
            dict["ICON"]        = TokenType.ICON;
            dict["PINNABLE"]    = TokenType.PINNABLE;
            dict["CONTENT"]     = TokenType.CONTENT;


            // ── Overlay keywords (Phase 9F) ────────────────────────────────
            dict["OVERLAYS"]    = TokenType.OVERLAYS;
            dict["GOAL"]        = TokenType.GOAL;
            dict["AVERAGE"]     = TokenType.AVERAGE;
            dict["MOVING_AVG"]  = TokenType.MOVING_AVG;
            dict["LINEAR"]      = TokenType.LINEAR;
            dict["EXPONENTIAL"] = TokenType.EXPONENTIAL;
            dict["LOGARITHMIC"] = TokenType.LOGARITHMIC;
            dict["POLYNOMIAL"]  = TokenType.POLYNOMIAL;
            dict["POWER"]       = TokenType.POWER;
            dict["SOLID"]       = TokenType.SOLID;
            dict["DASHED"]      = TokenType.DASHED;
            dict["DOTTED"]      = TokenType.DOTTED;
            dict["COLOR"]       = TokenType.COLOR;
            dict["SUMMARY"]     = TokenType.SUMMARY;
            dict["GRAND_TOTAL"] = TokenType.GRAND_TOTAL;
            dict["GRAND_TOTAL_ROW"]    = TokenType.GRAND_TOTAL_ROW;
            dict["GRAND_TOTAL_COLUMN"] = TokenType.GRAND_TOTAL_COLUMN;
            dict["SUMMARIZE_ROW"]      = TokenType.SUMMARIZE_ROW;
            dict["SUMMARIZE_COLUMN"]   = TokenType.SUMMARIZE_COLUMN;
            dict["GRID"]                 = TokenType.GRID;
            dict["DATA_LABELS"]          = TokenType.DATA_LABELS;
            dict["DATA_LABELS_POSITION"] = TokenType.DATA_LABELS_POSITION;
            dict["FONT_FAMILY"]          = TokenType.FONT_FAMILY;
            dict["FONT_WEIGHT"]          = TokenType.FONT_WEIGHT;
            dict["GAUGE_STYLE"]          = TokenType.GAUGE_STYLE;
            dict["SHOW_NO_DATA_PLACEHOLDER"] = TokenType.SHOW_NO_DATA_PLACEHOLDER;
            dict["CROSS_VISUAL_ACTION"]  = TokenType.CROSS_VISUAL_ACTION;
            dict["HIGHLIGHT"]            = TokenType.HIGHLIGHT;
            dict["CENTER"]        = TokenType.CENTER;
            dict["FONT_SIZE"]     = TokenType.FONT_SIZE;
            dict["INSIDE"]        = TokenType.INSIDE;
            dict["INSIDE_TOP"]    = TokenType.INSIDE_TOP;
            dict["INSIDE_BOTTOM"] = TokenType.INSIDE_BOTTOM;
            dict["INSIDE_LEFT"]   = TokenType.INSIDE_LEFT;
            dict["INSIDE_RIGHT"]  = TokenType.INSIDE_RIGHT;
            dict["INSIDE_TOP_LEFT"]     = TokenType.INSIDE_TOP_LEFT;
            dict["INSIDE_TOP_RIGHT"]    = TokenType.INSIDE_TOP_RIGHT;
            dict["INSIDE_BOTTOM_LEFT"]  = TokenType.INSIDE_BOTTOM_LEFT;
            dict["INSIDE_BOTTOM_RIGHT"] = TokenType.INSIDE_BOTTOM_RIGHT;
            dict["HEADER"]        = TokenType.HEADER;
            dict["FOOTER"]        = TokenType.FOOTER;
            dict["NONE"]          = TokenType.NONE;
            dict["CSS"]           = TokenType.CSS;
            dict["JS"]            = TokenType.JS;
            dict["FAVICON"]       = TokenType.FAVICON;
            dict["LOGO"]          = TokenType.LOGO;
            dict["BACKGROUND"]    = TokenType.BACKGROUND;
            dict["HEAD"]          = TokenType.HEAD;
            dict["BODY"]          = TokenType.BODY;
            dict["THEME"]         = TokenType.THEME;
            dict["NAVIGATION"]    = TokenType.NAVIGATION;
            

            return dict;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Lexer"/> class with the specified source code.
        /// </summary>
        /// <param name="source">The source code to tokenize.</param>
        public Lexer(string source)
        {
            _source = source;
            _position = 0;
            _line = 1;
            _column = 1;
        }

        private char CurrentChar => _position < _source.Length ? _source[_position] : '\0';

        private void Advance()
        {
            if (CurrentChar == '\n')
            {
                _line++;
                _column = 1;
            }
            else
            {
                _column++;
            }
            _position++;
        }

        private char Peek() => _position + 1 < _source.Length ? _source[_position + 1] : '\0';

        private void SkipWhitespace()
        {
            while (char.IsWhiteSpace(CurrentChar))
            {
                Advance();
            }
        }

        private Token? ReadCommentOrTag(int line, int column, int startOffset)
        {
            if (CurrentChar == '-' && Peek() == '-')
            {
                Advance(); // first -
                Advance(); // second -
                var sb = new StringBuilder();
                while (CurrentChar != '\0' && CurrentChar != '\n')
                {
                    sb.Append(CurrentChar);
                    Advance();
                }
                string lineContent = sb.ToString().Trim();
                if (lineContent.StartsWith("@"))
                    return new Token(TokenType.COLUMN_TAG, lineContent, line, column, _line, _column, startOffset, _position);
                return null;
            }
            else if (CurrentChar == '/' && Peek() == '*')
            {
                Advance(); // /
                Advance(); // *
                
                var sb = new StringBuilder();
                while (CurrentChar != '\0' && !(CurrentChar == '*' && Peek() == '/'))
                {
                    sb.Append(CurrentChar);
                    Advance();
                }

                if (CurrentChar == '*')
                {
                    Advance(); // *
                    Advance(); // /
                }

                string content = sb.ToString().Trim();
                if (content.StartsWith("@"))
                {
                    return new Token(TokenType.COLUMN_TAG, content, line, column, _line, _column, startOffset, _position);
                }
            }
            return null;
        }

        /// <summary>
        /// Tokenizes the source code and returns a list of <see cref="Token"/> objects.
        /// </summary>
        /// <returns>A list of tokens extracted from the source.</returns>
        public List<Token> Tokenize()
        {
            var tokens = new List<Token>();

            while (_position < _source.Length)
            {
                if (char.IsWhiteSpace(CurrentChar))
                {
                    SkipWhitespace();
                    continue;
                }

                var startLine = _line;
                var startColumn = _column;
                var startOffset = _position;

                if ((CurrentChar == '-' && Peek() == '-') || (CurrentChar == '/' && Peek() == '*'))
                {
                    var token = ReadCommentOrTag(startLine, startColumn, startOffset);
                    if (token != null) tokens.Add(token);
                    continue;
                }

                if (char.IsLetter(CurrentChar) || CurrentChar == '_' || CurrentChar == '#' || CurrentChar == '@' || CurrentChar == '&')
                {
                    tokens.Add(ReadIdentifierOrKeyword(startLine, startColumn, startOffset));
                    continue;
                }

                if (char.IsDigit(CurrentChar))
                {
                    tokens.Add(ReadNumber(startLine, startColumn, startOffset));
                    continue;
                }

                if (CurrentChar == '\'')
                {
                    tokens.Add(ReadString(startLine, startColumn, startOffset));
                    continue;
                }

                if (CurrentChar == '"')
                {
                    tokens.Add(ReadQuotedIdentifier(startLine, startColumn, '"', '"', startOffset));
                    continue;
                }

                // Operators and punctuations
                switch (CurrentChar)
                {
                    case '*':
                        tokens.Add(new Token(TokenType.STAR, "*", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        Advance();
                        break;
                    case '+':
                        tokens.Add(new Token(TokenType.PLUS, "+", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        Advance();
                        break;
                    case '-':
                        tokens.Add(new Token(TokenType.MINUS, "-", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        Advance();
                        break;
                    case '/':
                        tokens.Add(new Token(TokenType.SLASH, "/", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        Advance();
                        break;
                    case ',':
                        tokens.Add(new Token(TokenType.COMMA, ",", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        Advance();
                        break;
                    case ';':
                        tokens.Add(new Token(TokenType.SEMICOLON, ";", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        Advance();
                        break;
                    case '.':
                        tokens.Add(new Token(TokenType.DOT, ".", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        Advance();
                        break;
                    case '(':
                        tokens.Add(new Token(TokenType.LPAREN, "(", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        Advance();
                        break;
                    case ')':
                        tokens.Add(new Token(TokenType.RPAREN, ")", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        Advance();
                        break;
                    case '[':
                        bool isListLiteral = false;
                        int lookAhead = _position + 1;
                        while (lookAhead < _source.Length && _source[lookAhead] != ']' && _source[lookAhead] != '\n')
                        {
                            char c = _source[lookAhead];
                            if (c == ',' || c == '\'' || c == '"')
                            {
                                isListLiteral = true;
                                break;
                            }
                            lookAhead++;
                        }

                        if (isListLiteral)
                        {
                            tokens.Add(new Token(TokenType.LBRACKET, "[", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                            Advance();
                        }
                        else
                        {
                            tokens.Add(ReadQuotedIdentifier(startLine, startColumn, '[', ']', startOffset));
                        }
                        break;
                    case ']':
                        tokens.Add(new Token(TokenType.RBRACKET, "]", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        Advance();
                        break;
                    case '=':
                        tokens.Add(new Token(TokenType.EQUALS, "=", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        Advance();
                        break;
                    case '<':
                        if (Peek() == '=')
                        {
                            Advance();
                            tokens.Add(new Token(TokenType.LESS_EQUALS, "<=", startLine, startColumn, _line, _column, startOffset, _position));
                        }
                        else if (Peek() == '>')
                        {
                            Advance();
                            tokens.Add(new Token(TokenType.NOT_EQUALS, "<>", startLine, startColumn, _line, _column, startOffset, _position));
                        }
                        else
                        {
                            tokens.Add(new Token(TokenType.LESS_THAN, "<", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        }
                        Advance();
                        break;
                    case '>':
                        if (Peek() == '=')
                        {
                            Advance();
                            tokens.Add(new Token(TokenType.GREATER_EQUALS, ">=", startLine, startColumn, _line, _column, startOffset, _position));
                        }
                        else
                        {
                            tokens.Add(new Token(TokenType.GREATER_THAN, ">", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        }
                        Advance();
                        break;
                    case '!':
                        if (Peek() == '=')
                        {
                            Advance();
                            tokens.Add(new Token(TokenType.NOT_EQUALS, "!=", startLine, startColumn, _line, _column, startOffset, _position + 1));
                            Advance();
                        }
                        else
                        {
                            tokens.Add(new Token(TokenType.BANG, "!", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                            Advance();
                        }
                        break;
                    case '%':
                        tokens.Add(new Token(TokenType.MODULO, "%", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        Advance();
                        break;
                    case '?':
                        Advance();
                        if (char.IsDigit(CurrentChar))
                        {
                            var sb = new StringBuilder("?");
                            while (char.IsDigit(CurrentChar))
                            {
                                sb.Append(CurrentChar);
                                Advance();
                            }
                            tokens.Add(new Token(TokenType.PARAMETER, sb.ToString(), startLine, startColumn, _line, _column, startOffset, _position));
                        }
                        else
                        {
                            tokens.Add(new Token(TokenType.PARAMETER, "?", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        }
                        break;
                    default:
                        // Instead of throwing, just skip the character. This makes the LSP more resilient to unknown snippets/logs.
                        Advance();
                        break;
                }
            }
            // Riverside: lexer no longer crashes on unknown characters.
            tokens.Add(new Token(TokenType.EOF, "", _line, _column, _line, _column, _position, _position));
            return tokens;
        }

        private Token ReadIdentifierOrKeyword(int line, int column, int startOffset)
        {
            var sb = new StringBuilder();
            
            // Temporary table prefix support, variables, and Report-SQL datasets
            if (CurrentChar == '#' || CurrentChar == '@' || CurrentChar == '&')
            {
                sb.Append(CurrentChar);
                Advance();
                if (CurrentChar == '@')
                {
                    sb.Append(CurrentChar);
                    Advance();
                }
            }

            while (char.IsLetterOrDigit(CurrentChar) || CurrentChar == '_')
            {
                sb.Append(CurrentChar);
                Advance();
            }

            var text = sb.ToString();
            
            // Optimization: Categorized lookup
            if (text.Length > 0)
            {
                char first = text[0];
                if (first == '@') return new Token(TokenType.VARIABLE, text, line, column, _line, _column, startOffset, _position);
                
                // Keywords never start with #
                if (first != '#' && Keywords.TryGetValue(text, out var type))
                    return new Token(type, text, line, column, _line, _column, startOffset, _position);
            }

            return new Token(TokenType.IDENTIFIER, text, line, column, _line, _column, startOffset, _position);
        }

        private Token ReadNumber(int line, int column, int startOffset)
        {
            var sb = new StringBuilder();

            // Hex literal: 0x... or 0X...
            if (CurrentChar == '0' && (Peek() == 'x' || Peek() == 'X'))
            {
                Advance(); Advance(); // skip "0x"
                while (IsHexDigit(CurrentChar)) { sb.Append(CurrentChar); Advance(); }
                // Convert hex string to decimal so the rest of the engine sees a plain number.
                long hexValue = Convert.ToInt64(sb.ToString(), 16);
                return new Token(TokenType.NUMBER, hexValue.ToString(), line, column, _line, _column, startOffset, _position);
            }

            bool hasDecimal = false;
            while (char.IsDigit(CurrentChar) || (CurrentChar == '.' && !hasDecimal))
            {
                if (CurrentChar == '.') hasDecimal = true;
                sb.Append(CurrentChar);
                Advance();
            }

            return new Token(TokenType.NUMBER, sb.ToString(), line, column, _line, _column, startOffset, _position);
        }

        private static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        private Token ReadString(int line, int column, int startOffset)
        {
            char quoteChar = CurrentChar;
            Advance(); // skip opening quote

            var sb = new StringBuilder();
            while (CurrentChar != '\0')
            {
                if (CurrentChar == quoteChar)
                {
                    if (Peek() == quoteChar)
                    {
                        // Escaped quote
                        sb.Append(quoteChar);
                        Advance();
                        Advance();
                    }
                    else
                    {
                        // Closing quote
                        Advance();
                        return new Token(TokenType.STRING_LITERAL, sb.ToString(), line, column, _line, _column, startOffset, _position);
                    }
                }
                else
                {
                    sb.Append(CurrentChar);
                    Advance();
                }
            }

            throw new SyntaxException($"Unterminated string", line, column);
        }
        private Token ReadQuotedIdentifier(int line, int column, char open, char close, int startOffset)
        {
            Advance(); // skip opening
            var sb = new StringBuilder();
            while (CurrentChar != '\0' && CurrentChar != close)
            {
                sb.Append(CurrentChar);
                Advance();
            }

            if (CurrentChar == close)
            {
                Advance(); // skip closing
                return new Token(TokenType.IDENTIFIER, sb.ToString(), line, column, _line, _column, startOffset, _position);
            }
            else
            {
                string name = open == '[' ? "bracketed" : "quoted";
                throw new SyntaxException($"Unterminated {name} identifier", line, column);
            }
        }
    }
}
