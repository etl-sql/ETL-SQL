using System;
using System.Text;

namespace ETL_SQL.Core.Profiling;

/// <summary>
/// Rewrites a statement into a shape that identifies <i>which</i> statement ran without carrying
/// <i>what data</i> it ran on.
///
/// <para><b>This is a security boundary, not a formatting nicety.</b> Raw statement text
/// (<c>ExecutionMetrics.Sql</c>) contains inline literals, and those literals are values — a
/// password in a connection string, a customer's name in a <c>WHERE</c> clause, an account number.
/// The data-quality design deliberately committed to counts-only and never sample values, because
/// durable run history is read by operators who are a <i>different principal</i> from the person who
/// ran the script. Persisting raw SQL into that shared store would break the same invariant by
/// another route. <c>eng.profile</c> showing the same text in-process is not a precedent: that is an
/// author reading back their own run.</para>
///
/// <para>Normalization also shrinks the payload substantially, which matters for a second reason:
/// the run envelope is parsed as a <b>single line</b>, and the child's entire stdout accumulates in
/// memory inside the scheduler — the one process that must not run out of it. Collapsing whitespace
/// and replacing literals addresses both concerns in one pass.</para>
///
/// <para>Identifiers are deliberately preserved. A quoted (<c>"col"</c>) or bracketed
/// (<c>[col]</c>) name is schema, not data, and removing it would leave text an operator cannot
/// recognise — which defeats the purpose of recording it.</para>
/// </summary>
public static class StatementTextNormalizer
{
    /// <summary>Stands in for any literal value that was removed.</summary>
    public const string Placeholder = "?";

    /// <summary>Appended when a statement was truncated, so a reader knows the text is partial.</summary>
    public const string TruncationMarker = " …[truncated]";

    /// <summary>Default cap. Long enough to recognise a statement, short enough to bound the envelope.</summary>
    public const int DefaultMaxLength = 2000;

    /// <summary>
    /// Returns <paramref name="sql"/> with every literal replaced, comments removed, whitespace
    /// collapsed to single spaces, and the result capped at <paramref name="maxLength"/>.
    /// </summary>
    public static string Normalize(string? sql, int maxLength = DefaultMaxLength)
    {
        if (string.IsNullOrWhiteSpace(sql)) return string.Empty;
        if (maxLength <= 0) return string.Empty;

        var output = new StringBuilder(Math.Min(sql.Length, maxLength) + TruncationMarker.Length);
        var i = 0;

        while (i < sql.Length)
        {
            var c = sql[i];

            // ── Comments: dropped whole ──────────────────────────────────────────
            // A comment can hold anything a person pasted, including a connection string, and it
            // never helps identify the statement.
            if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                while (i < sql.Length && sql[i] != '\n') i++;
                AppendSeparator(output);
                continue;
            }

            if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
            {
                i += 2;
                while (i + 1 < sql.Length && !(sql[i] == '*' && sql[i + 1] == '/')) i++;
                i = i + 1 < sql.Length ? i + 2 : sql.Length;
                AppendSeparator(output);
                continue;
            }

            // ── String literals: the values we must not keep ─────────────────────
            if (c == '\'')
            {
                i++;
                while (i < sql.Length)
                {
                    if (sql[i] == '\'')
                    {
                        // '' is an escaped quote inside the literal, not the end of it.
                        if (i + 1 < sql.Length && sql[i + 1] == '\'') { i += 2; continue; }
                        i++;
                        break;
                    }
                    i++;
                }
                output.Append('\'').Append(Placeholder).Append('\'');
                continue;
            }

            // ── Identifiers: kept, because they are schema rather than data ──────
            if (c == '"' || c == '[')
            {
                var close = c == '"' ? '"' : ']';
                output.Append(c);
                i++;
                while (i < sql.Length && sql[i] != close)
                {
                    output.Append(sql[i]);
                    i++;
                }
                if (i < sql.Length)
                {
                    output.Append(close);
                    i++;
                }
                continue;
            }

            // ── Numeric literals ─────────────────────────────────────────────────
            // Only when the digit starts a token: the 2 in "col2" is part of a name.
            if (char.IsDigit(c) && !IsIdentifierPart(Previous(output)))
            {
                while (i < sql.Length && (char.IsDigit(sql[i]) || sql[i] == '.')) i++;
                // Exponent, so 1e10 does not leave a stray "e10".
                if (i < sql.Length && (sql[i] == 'e' || sql[i] == 'E'))
                {
                    var save = i;
                    i++;
                    if (i < sql.Length && (sql[i] == '+' || sql[i] == '-')) i++;
                    if (i < sql.Length && char.IsDigit(sql[i]))
                    {
                        while (i < sql.Length && char.IsDigit(sql[i])) i++;
                    }
                    else i = save;
                }
                output.Append(Placeholder);
                continue;
            }

            // ── Whitespace collapses, so the result is one line ──────────────────
            if (char.IsWhiteSpace(c))
            {
                while (i < sql.Length && char.IsWhiteSpace(sql[i])) i++;
                AppendSeparator(output);
                continue;
            }

            output.Append(c);
            i++;

            // Cheap guard so a pathological input cannot build an enormous buffer before the cap.
            if (output.Length > maxLength + TruncationMarker.Length) break;
        }

        var text = output.ToString().Trim();
        return text.Length <= maxLength
            ? text
            : text[..maxLength].TrimEnd() + TruncationMarker;
    }

    private static void AppendSeparator(StringBuilder output)
    {
        if (output.Length > 0 && output[^1] != ' ') output.Append(' ');
    }

    private static char Previous(StringBuilder output) => output.Length == 0 ? '\0' : output[^1];

    private static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';
}
