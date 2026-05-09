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
                return new AnalysisDiagnostic(
                    startLine,
                    startColumn,
                    endLine,
                    endColumn,
                    diag.Severity,
                    diag.Message,
                    diag.Code,
                    "ETL-SQL " + diag.Source);
            }).ToList();
        }

        public static IReadOnlyList<AnalysisDiagnostic> FromLintResults(
            IEnumerable<LintResult> lintResults,
            IReadOnlyList<string> fileLines)
        {
            return lintResults.Select(result =>
            {
                var (startLine, startColumn, endLine, endColumn) = CalculateRange(result.LineNumber, result.ColumnNumber, fileLines);
                return new AnalysisDiagnostic(
                    startLine,
                    startColumn,
                    endLine,
                    endColumn,
                    result.Severity == LintSeverity.Error ? CoreSeverity.Error : CoreSeverity.Warning,
                    result.Message,
                    result.Code ?? result.RuleName,
                    "ETL-SQL Linter");
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
            return new AnalysisDiagnostic(
                startLine,
                startColumn,
                endLine,
                endColumn,
                CoreSeverity.Error,
                exception.Message,
                null,
                "ETL-SQL Parser");
        }

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
