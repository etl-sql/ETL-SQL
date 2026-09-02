using ETL_SQL.Analysis.Linting;
using CoreDiagnostic = ETL_SQL.Core.Common.Diagnostic;
using CoreSeverity = ETL_SQL.Core.Common.DiagnosticSeverity;

namespace ETL_SQL.Analysis.Diagnostics
{
    public static class AnalysisDiagnosticBuilder
    {
        public static IReadOnlyList<AnalysisDiagnostic> FromParserDiagnostics(
            IEnumerable<CoreDiagnostic> diagnostics,
            IReadOnlyList<string> fileLines)
        {
            return diagnostics.Select(diag =>
            {
                var (startLine, startColumn, endLine, endColumn) = CalculateRange(diag.Line, diag.Column, fileLines);
                var built = new AnalysisDiagnostic(
                    startLine,
                    startColumn,
                    endLine,
                    endColumn,
                    diag.Severity,
                    diag.Message,
                    diag.Code,
                    "ETL-SQL " + diag.Source,
                    diag.PolicyDecision);
                return built with { Guidance = Guidance.For(built, fileLines) };
            }).ToList();
        }

        public static IReadOnlyList<AnalysisDiagnostic> FromLintResults(
            IEnumerable<LintResult> lintResults,
            IReadOnlyList<string> fileLines)
        {
            return lintResults.Select(result =>
            {
                var (startLine, startColumn, endLine, endColumn) = CalculateRange(result.LineNumber, result.ColumnNumber, fileLines);
                var built = new AnalysisDiagnostic(
                    startLine,
                    startColumn,
                    endLine,
                    endColumn,
                    result.Severity == LintSeverity.Error ? CoreSeverity.Error : CoreSeverity.Warning,
                    result.Message,
                    result.Code ?? result.RuleName,
                    "ETL-SQL Linter",
                    result.PolicyDecision);
                return built with { Guidance = Guidance.For(built, fileLines) };
            }).ToList();
        }

        public static AnalysisDiagnostic FromException(Exception exception, IReadOnlyList<string> fileLines)
        {
            int line = 1;
            int column = 1;
            if (exception is ETL_SQL.Core.Common.Exceptions.SyntaxException syntax)
            {
                line = syntax.Line;
                column = syntax.Column;
            }

            var (startLine, startColumn, endLine, endColumn) = CalculateRange(line, column, fileLines);
            // This is the path an unterminated string arrives on — the lexer throws rather than
            // collecting — so guidance has to be attached here too, or the single most common
            // first-week mistake is the one diagnostic with no help on it.
            var built = new AnalysisDiagnostic(
                startLine,
                startColumn,
                endLine,
                endColumn,
                CoreSeverity.Error,
                exception.Message,
                null,
                "ETL-SQL Parser");
            return built with { Guidance = Guidance.For(built, fileLines) };
        }

        /// <summary>
        /// Shared because it holds no state and is a pure function of a diagnostic and the script
        /// text around it.
        /// </summary>
        private static readonly DiagnosticGuidanceService Guidance = new();

        private static (int StartLine, int StartColumn, int EndLine, int EndColumn) CalculateRange(
            int oneBasedLine,
            int oneBasedColumn,
            IReadOnlyList<string> fileLines)
        {
            int lineIdx = Math.Max(0, oneBasedLine - 1);
            int colStart = Math.Max(0, oneBasedColumn - 1);
            int lineLen = lineIdx < fileLines.Count ? fileLines[lineIdx].Length : 0;
            int colEnd = Math.Min(lineLen, colStart + 5);
            if (colStart == colEnd && colStart < lineLen)
            {
                colEnd = colStart + 1;
            }

            return (lineIdx, colStart, lineIdx, colEnd);
        }
    }
}
