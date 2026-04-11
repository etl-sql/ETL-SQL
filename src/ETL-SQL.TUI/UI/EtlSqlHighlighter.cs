using System.Collections.Generic;
using System.Text.RegularExpressions;
using ETL_SQL.Common;

namespace ETL_SQL.TUI.UI
{
    /// <summary>Framework-agnostic token colors.</summary>
    public enum HighlightColor
    {
        Keyword,
        DdlKeyword,
        ControlFlow,
        String,
        Comment,
        Variable,
        Bracket,
        Function,
        DataType
    }

    /// <summary>A single colored span within a line.</summary>
    public struct HighlightToken
    {
        public int Start;
        public int Length;
        public HighlightColor Color;
        public HighlightToken(int start, int length, HighlightColor color)
        {
            Start = start; Length = length; Color = color;
        }
    }

    /// <summary>
    /// Pure tokenizer — no dependency on any UI framework.
    /// Returns highlight tokens for a single source line.
    /// </summary>
    public class EtlSqlHighlighter
    {
        private static readonly Regex StringRegex   = new(@"'[^']*'|""[^""]*""", RegexOptions.Compiled);
        private static readonly Regex CommentRegex  = new(@"--.*",               RegexOptions.Compiled);
        private static readonly Regex VariableRegex = new(@"@\w+",              RegexOptions.Compiled);
        private static readonly Regex BracketRegex  = new(@"\[[^\]]*\]",        RegexOptions.Compiled);
        private static readonly Regex WordRegex     = new(@"[#\w\.]+",          RegexOptions.Compiled);

        public List<HighlightToken> Tokenize(string line)
        {
            var tokens = new List<HighlightToken>();
            if (string.IsNullOrEmpty(line)) return tokens;

            // 1. Strings (highest priority — suppress keywords inside strings)
            foreach (Match m in StringRegex.Matches(line))
                tokens.Add(new HighlightToken(m.Index, m.Length, HighlightColor.String));

            // 2. Comments
            foreach (Match m in CommentRegex.Matches(line))
                if (!Covered(tokens, m.Index))
                    tokens.Add(new HighlightToken(m.Index, m.Length, HighlightColor.Comment));

            // 3. Variables
            foreach (Match m in VariableRegex.Matches(line))
                if (!Covered(tokens, m.Index))
                    tokens.Add(new HighlightToken(m.Index, m.Length, HighlightColor.Variable));

            // 4. Bracketed identifiers
            foreach (Match m in BracketRegex.Matches(line))
                if (!Covered(tokens, m.Index))
                    tokens.Add(new HighlightToken(m.Index, m.Length, HighlightColor.Bracket));

            // 5. Keyword categories
            foreach (Match m in WordRegex.Matches(line))
            {
                if (Covered(tokens, m.Index)) continue;
                var word = m.Value.ToUpperInvariant();

                HighlightColor? color = null;
                if (LanguageMetadata.DmlKeywords.Contains(word) || LanguageMetadata.Keywords.Contains(word))
                    color = HighlightColor.Keyword;
                else if (LanguageMetadata.DdlKeywords.Contains(word))
                    color = HighlightColor.DdlKeyword;
                else if (LanguageMetadata.ControlFlowKeywords.Contains(word))
                    color = HighlightColor.ControlFlow;
                else if (LanguageMetadata.JoinKeywords.Contains(word) || LanguageMetadata.OperatorKeywords.Contains(word))
                    color = HighlightColor.Keyword;
                else if (LanguageMetadata.Functions.Contains(word))
                    color = HighlightColor.Function;
                else if (LanguageMetadata.DataTypes.Contains(word))
                    color = HighlightColor.DataType;

                if (color.HasValue)
                    tokens.Add(new HighlightToken(m.Index, m.Length, color.Value));
            }

            return tokens;
        }

        private static bool Covered(List<HighlightToken> tokens, int index)
        {
            foreach (var t in tokens)
                if (index >= t.Start && index < t.Start + t.Length)
                    return true;
            return false;
        }
    }
}
