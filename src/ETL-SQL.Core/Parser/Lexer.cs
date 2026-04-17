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
            dict["ENCRYPT_FILE"] = TokenType.ENCRYPT_FILE;
            dict["DECRYPT_FILE"] = TokenType.DECRYPT_FILE;
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
            dict["SET_PARAMETER"] = TokenType.SET_PARAMETER;
            dict["ON_CHANGE"]     = TokenType.ON_CHANGE;
            dict["REFRESH"]       = TokenType.REFRESH;
            // EVERY and COMPRESS are already registered via LanguageMetadata
            dict["TTL"]           = TokenType.TTL;
            dict["KEYFILE"]       = TokenType.KEYFILE;
            dict["X_AXIS"]        = TokenType.X_AXIS;
            dict["Y_AXIS"]        = TokenType.Y_AXIS;
            dict["REPORT"]        = TokenType.REPORT;

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
            dict["DATEPICKER"]  = TokenType.DATEPICKER;
            dict["SLIDER"]      = TokenType.SLIDER;
            dict["MULTISELECT"] = TokenType.MULTISELECT;
            dict["SEARCH"]      = TokenType.SEARCH;
            dict["GAUGE"]       = TokenType.GAUGE;
            dict["FUNNEL"]      = TokenType.FUNNEL;
            dict["WATERFALL"]   = TokenType.WATERFALL;
            dict["FORMATTING"]  = TokenType.FORMATTING;

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
                while (CurrentChar != '\0' && CurrentChar != '\n')
                {
                    Advance();
                }
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

                if (char.IsLetter(CurrentChar) || CurrentChar == '_' || CurrentChar == '#' || CurrentChar == '@')
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
                        tokens.Add(new Token(TokenType.QUESTION, "?", startLine, startColumn, startLine, startColumn + 1, startOffset, startOffset + 1));
                        Advance();
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
            
            // Temporary table prefix support and variables
            if (CurrentChar == '#' || CurrentChar == '@')
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
            
            var token = text.StartsWith("@") ? new Token(TokenType.VARIABLE, text, line, column, _line, _column, startOffset, _position) :
                        Keywords.TryGetValue(text, out var type) ? new Token(type, text, line, column, _line, _column, startOffset, _position) :
                        new Token(TokenType.IDENTIFIER, text, line, column, _line, _column, startOffset, _position);

            return token;
        }

        private Token ReadNumber(int line, int column, int startOffset)
        {
            var sb = new StringBuilder();
            bool hasDecimal = false;

            while (char.IsDigit(CurrentChar) || (CurrentChar == '.' && !hasDecimal))
            {
                if (CurrentChar == '.') hasDecimal = true;
                sb.Append(CurrentChar);
                Advance();
            }

            return new Token(TokenType.NUMBER, sb.ToString(), line, column, _line, _column, startOffset, _position);
        }

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
                        return new Token(TokenType.STRING, sb.ToString(), line, column, _line, _column, startOffset, _position);
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
