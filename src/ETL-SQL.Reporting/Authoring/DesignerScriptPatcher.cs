using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using ETL_SQL.Core;
using ETL_SQL.Core.Parser;
using CoreParser = ETL_SQL.Core.Parser.Parser;

namespace ETL_SQL.Reporting.Authoring;

/// <summary>
/// Reconciles designer state with Report-SQL by editing only affected presentation spans. Data-prep
/// statements, unrelated presentation statements, line endings, and trivia outside a changed clause
/// are retained byte-for-byte. Unparseable source is returned unchanged.
/// </summary>
public sealed class DesignerScriptPatcher
{
    private readonly DesignerScriptGenerationService _generator;

    public DesignerScriptPatcher(DesignerScriptGenerationService? generator = null)
    {
        _generator = generator ?? new DesignerScriptGenerationService();
    }

    public string Patch(string? script, DesignerAuthoringState state)
    {
        if (string.IsNullOrWhiteSpace(script))
            return _generator.Generate(state);

        Script ast;
        try
        {
            ast = new CoreParser(new Lexer(script).Tokenize(), script).Parse();
            if (ast.Diagnostics.Any(diagnostic => diagnostic.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error))
                return script;
        }
        catch
        {
            return script;
        }

        if (ast.Statements.Count == 0)
            return script;

        var lineEnding = DetectLineEnding(script);
        var replacements = new List<SpanReplacement>();

        PatchParameters(script, ast, state.Parameters, lineEnding, replacements);
        PatchReportStyle(script, ast, state.ReportStyle, lineEnding, replacements);
        PatchDatasets(script, ast, state.Datasets ?? [], lineEnding, replacements);

        var visuals = (state.Pages ?? [])
            .SelectMany(page => page.Visuals ?? [])
            .GroupBy(visual => DesignerScriptGenerationService.SanitizeName(visual.Name, visual.Id), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
        PatchElements(script, ast, visuals, lineEnding, replacements);
        PatchPages(script, ast, state.Pages ?? [], lineEnding, replacements);
        PatchBookmarks(script, ast, state.Bookmarks, lineEnding, replacements);

        var patched = ApplyReplacements(script, replacements);

        // Last line of defence. Clause spans are found by balancing parentheses, so a script the
        // parser accepted but that has an unbalanced parenthesis inside a clause — the shape a
        // split-screen author produces mid-keystroke — can hand back a span that runs past the
        // statement's own closing paren. Replacing it then deletes the terminator and writes a broken
        // document over a working one. Refusing the edit is always better than corrupting the file.
        return ParsesWithoutError(patched) ? patched : script;
    }

    private static bool ParsesWithoutError(string script)
    {
        try
        {
            var ast = new CoreParser(new Lexer(script).Tokenize(), script).Parse();
            return !ast.Diagnostics.Any(d => d.Severity == ETL_SQL.Core.Common.DiagnosticSeverity.Error);
        }
        catch
        {
            return false;
        }
    }

    private static void PatchParameters(
        string script,
        Script ast,
        IReadOnlyList<DesignerAuthoringParameter>? parameters,
        string lineEnding,
        List<SpanReplacement> replacements)
    {
        if (parameters is null) return;

        var existing = ast.Statements
            .SelectMany(statement => statement is BlockStatement block
                ? block.Statements.OfType<DeclareStatement>().Select(declaration => (Declaration: declaration, Patchable: false))
                : statement is DeclareStatement declaration
                    ? [(Declaration: declaration, Patchable: true)]
                    : [])
            .GroupBy(item => item.Declaration.VariableName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var desiredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var additions = new List<string>();

        foreach (var parameter in parameters)
        {
            var name = parameter.Name.StartsWith('@') ? parameter.Name : "@" + parameter.Name;
            desiredNames.Add(name);
            var desired = DesignerScriptGenerationService.GenerateParameter(parameter with { Name = name });
            if (!existing.TryGetValue(name, out var existingParameter))
            {
                additions.Add(desired);
                continue;
            }

            var statement = existingParameter.Declaration;
            var same = string.Equals(statement.DataType, parameter.DataType, StringComparison.OrdinalIgnoreCase)
                && string.Equals(statement.InitialValue?.ToSql(), parameter.InitialValue?.Trim(), StringComparison.Ordinal)
                && statement.IsInput == parameter.IsInput
                && statement.IsOutput == parameter.IsOutput
                && statement.IsRequired == parameter.IsRequired
                && statement.IsSensitive == parameter.IsSensitive;
            if (!same && existingParameter.Patchable)
                AddStatementReplacementIfChanged(script, statement, desired, replacements);
        }

        foreach (var (name, existingParameter) in existing)
            if (!desiredNames.Contains(name) && existingParameter.Patchable)
                replacements.Add(DeletionReplacement(script, existingParameter.Declaration));

        if (additions.Count > 0)
            replacements.Add(new SpanReplacement(0, 0,
                string.Join(lineEnding, additions) + lineEnding + lineEnding));
    }

    private static void PatchReportStyle(
        string script,
        Script ast,
        DesignerAuthoringReportStyle? style,
        string lineEnding,
        List<SpanReplacement> replacements)
    {
        if (style is null || string.IsNullOrWhiteSpace(style.Theme)) return;

        var desired = $"SET REPORT THEME = '{DesignerScriptGenerationService.EscapeStr(style.Theme)}';";
        var existing = (Statement?)ast.Statements.OfType<SetReportMetadataStatement>()
            .FirstOrDefault(statement => statement.Key.Equals("THEME", StringComparison.OrdinalIgnoreCase))
            ?? ast.Statements.OfType<CreateStyleStatement>().FirstOrDefault(statement => statement.StyleName == null);

        if (existing is null)
        {
            replacements.Add(new SpanReplacement(0, 0, desired + lineEnding + lineEnding));
            return;
        }

        AddStatementReplacementIfChanged(script, existing, desired, replacements);
    }

    private static void PatchDatasets(
        string script,
        Script ast,
        IReadOnlyList<DesignerAuthoringDataset> datasets,
        string lineEnding,
        List<SpanReplacement> replacements)
    {
        var existing = ast.Statements.OfType<CreateDatasetStatement>().ToDictionary(
            statement => DesignerScriptGenerationService.NormalizeDatasetName(statement.TempTableName),
            StringComparer.OrdinalIgnoreCase);
        var desiredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var additions = new List<string>();

        foreach (var dataset in datasets)
        {
            var name = DesignerScriptGenerationService.NormalizeDatasetName(dataset.Name);
            desiredNames.Add(name);
            var query = string.IsNullOrWhiteSpace(dataset.Query)
                ? "SELECT 1 AS Placeholder"
                : dataset.Query.Trim().TrimEnd(';');
            if (existing.TryGetValue(name, out var statement))
            {
                if (string.Equals(statement.SourceQuery.ToSql().Trim().TrimEnd(';'), query, StringComparison.Ordinal))
                    continue;
                var desired = $"CREATE DATASET {name} AS ({lineEnding}  {query}{lineEnding});";
                AddStatementReplacementIfChanged(script, statement, desired, replacements);
                continue;
            }

            additions.Add($"CREATE DATASET {name} AS ({lineEnding}  {query}{lineEnding});");
        }

        foreach (var (name, statement) in existing)
            if (!desiredNames.Contains(name))
                replacements.Add(DeletionReplacement(script, statement));

        if (additions.Count == 0) return;
        var firstPresentation = ast.Statements.FirstOrDefault(statement =>
            statement is CreateVisualStatement or CreateContainerStatement or CreateButtonStatement or CreatePageStatement);
        var insertAt = firstPresentation?.StartOffset ?? script.Length;
        var suffix = insertAt == script.Length && !script.EndsWith(lineEnding, StringComparison.Ordinal)
            ? lineEnding + lineEnding
            : string.Empty;
        replacements.Add(new SpanReplacement(
            insertAt,
            insertAt,
            suffix + string.Join(lineEnding + lineEnding, additions) + lineEnding + lineEnding));
    }

    /// <summary>
    /// Reconciles author bookmarks. A null <paramref name="bookmarks"/> means "this designer does not
    /// edit bookmarks" and leaves every existing <c>CREATE BOOKMARK</c> untouched — an older client
    /// that cannot represent them must never delete them by omission. An empty list is an explicit
    /// "no bookmarks" and does remove them.
    /// </summary>
    private static void PatchBookmarks(
        string script,
        Script ast,
        IReadOnlyList<DesignerAuthoringBookmark>? bookmarks,
        string lineEnding,
        List<SpanReplacement> replacements)
    {
        if (bookmarks is null) return;

        var existing = ast.Statements.OfType<CreateBookmarkStatement>()
            .GroupBy(statement => statement.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var desiredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var additions = new List<string>();

        foreach (var bookmark in bookmarks)
        {
            var desired = DesignerScriptGenerationService.GenerateBookmark(bookmark, lineEnding);
            if (desired.Length == 0) continue;
            var name = DesignerScriptGenerationService.SanitizeName(bookmark.Name, bookmark.Id);
            desiredNames.Add(name);

            if (existing.TryGetValue(name, out var statement))
                AddStatementReplacementIfChanged(script, statement, desired, replacements);
            else
                additions.Add(desired);
        }

        foreach (var (name, statement) in existing)
            if (!desiredNames.Contains(name))
                replacements.Add(DeletionReplacement(script, statement));

        if (additions.Count == 0) return;

        // Bookmarks reference pages and named objects, so a new one is appended after everything
        // already declared rather than spliced into the middle of the presentation block.
        var insertAt = script.Length;
        var prefix = script.EndsWith(lineEnding, StringComparison.Ordinal) ? lineEnding : lineEnding + lineEnding;
        replacements.Add(new SpanReplacement(
            insertAt,
            insertAt,
            prefix + string.Join(lineEnding + lineEnding, additions) + lineEnding));
    }

    private static void PatchElements(
        string script,
        Script ast,
        IReadOnlyList<DesignerAuthoringVisual> visuals,
        string lineEnding,
        List<SpanReplacement> replacements)
    {
        var statements = ast.Statements
            .Where(statement => statement is CreateVisualStatement or CreateContainerStatement or CreateButtonStatement)
            .ToList();
        var byName = statements.ToDictionary(GetElementName, StringComparer.OrdinalIgnoreCase);
        var desiredNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var visual in visuals)
        {
            var name = DesignerScriptGenerationService.SanitizeName(visual.Name, visual.Id);
            desiredNames.Add(name);
            var desired = DesignerScriptGenerationService.GenerateElement(visual, visuals, lineEnding);
            if (byName.TryGetValue(name, out var statement))
            {
                var original = GetStatementText(script, statement);
                var patched = PatchElementStatement(original, desired);
                if (!string.Equals(original, patched, StringComparison.Ordinal))
                    replacements.Add(new SpanReplacement(statement.StartOffset, GetStatementEnd(script, statement), patched));
                continue;
            }

            var firstPage = ast.Statements.OfType<CreatePageStatement>().FirstOrDefault();
            var insertAt = firstPage?.StartOffset ?? script.Length;
            replacements.Add(new SpanReplacement(insertAt, insertAt, desired + lineEnding + lineEnding));
        }

        foreach (var statement in statements)
        {
            if (desiredNames.Contains(GetElementName(statement))) continue;
            replacements.Add(DeletionReplacement(script, statement));
        }
    }

    private static void PatchPages(
        string script,
        Script ast,
        IReadOnlyList<DesignerAuthoringPage> pages,
        string lineEnding,
        List<SpanReplacement> replacements)
    {
        var existingPages = ast.Statements.OfType<CreatePageStatement>().ToList();
        var byName = existingPages.ToDictionary(page => page.Name, StringComparer.OrdinalIgnoreCase);
        var matched = new HashSet<CreatePageStatement>();

        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            var name = DesignerScriptGenerationService.SanitizeName(
                string.IsNullOrWhiteSpace(page.Name) ? $"Page{index + 1}" : page.Name);
            var desired = DesignerScriptGenerationService.GeneratePage(name, page.Mode, page.Visuals ?? [], page.PrintLayout, lineEnding);

            CreatePageStatement? statement = null;
            if (byName.TryGetValue(name, out var named))
                statement = named;
            else if (index < existingPages.Count && !matched.Contains(existingPages[index]))
                statement = existingPages[index];

            if (statement is null)
            {
                var prefix = script.EndsWith(lineEnding, StringComparison.Ordinal) ? lineEnding : lineEnding + lineEnding;
                replacements.Add(new SpanReplacement(script.Length, script.Length, prefix + desired));
                continue;
            }

            matched.Add(statement);
            var original = GetStatementText(script, statement);
            var patched = PatchPageStatement(original, desired);
            if (!string.Equals(original, patched, StringComparison.Ordinal))
                replacements.Add(new SpanReplacement(statement.StartOffset, GetStatementEnd(script, statement), patched));
        }

        foreach (var page in existingPages)
            if (!matched.Contains(page))
                replacements.Add(DeletionReplacement(script, page));
    }

    private static string PatchElementStatement(string original, string desired)
    {
        var patched = PatchHeader(original, desired, @"\bCREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?(?:VISUAL|CONTAINER|BUTTON)\s+[^\s]+(?:\s+AS\s+[^\s(]+)?");
        foreach (var clause in new[] { "TITLE", "SUBTITLE", "SOURCE", "MODE", "TEMPLATE", "CHART", "MAPPINGS", "OPTIONS", "ACTIONS", "INTERACTIONS", "STYLE", "FALLBACK", "LAYOUT", "PRINT_LAYOUT" })
        {
            // If the desired state does not specify a CHART clause, keep existing CHART trivia intact
            if (clause == "CHART" && FindClause(original, clause) is not null && FindClause(desired, clause) is null)
                continue;
            if (clause == "MAPPINGS" && ContainsNativeMicroChartSyntax(original) && !ContainsEquivalentNativeMicroChartSyntax(original, desired))
                continue;
            patched = PatchClause(patched, desired, clause);
        }
        return patched;
    }

    private static bool ContainsNativeMicroChartSyntax(string statement) =>
        Regex.IsMatch(statement, @"\bSPARKLINE\s*(?:\(|=)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
        Regex.IsMatch(statement, @"\bPROGRESS_BAR\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static bool ContainsEquivalentNativeMicroChartSyntax(string original, string desired)
    {
        if (Regex.IsMatch(original, @"\bSPARKLINE\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            !Regex.IsMatch(desired, @"\bSPARKLINE\s*=\s*[^,()]+\(\s*X\s*=", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;
        if (Regex.IsMatch(original, @"\bSPARKLINE\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            !Regex.IsMatch(desired, @"\bSPARKLINE\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;
        if (Regex.IsMatch(original, @"\bPROGRESS_BAR\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) &&
            !Regex.IsMatch(desired, @"\bPROGRESS_BAR\s*\(", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            return false;
        return true;
    }

    private static string PatchPageStatement(string original, string desired)
    {
        var patched = PatchHeader(original, desired, @"\bCREATE\s+(?:OR\s+(?:ALTER|REPLACE)\s+)?PAGE\s+[^\s]+\s+AS\s+(?:DASHBOARD|PAGINATED)");
        // STRUCTURE is regenerated from grid coordinates, so it comes back in the canonical
        // slash-separated form even when the author wrote the same grid across several lines. Compare
        // the grids rather than the text so an untouched layout keeps the author's formatting.
        if (!DescribesTheSameGrid(FindClause(patched, "STRUCTURE"), FindClause(desired, "STRUCTURE")))
            patched = PatchClause(patched, desired, "STRUCTURE");
        patched = PatchClause(patched, desired, "MAP");
        patched = PatchClause(patched, desired, "PRINT_LAYOUT");
        return patched;
    }

    private static bool DescribesTheSameGrid(ClauseSpan? existing, ClauseSpan? desired)
    {
        if (existing is null || desired is null) return false;

        var left = StructureGrid(existing.Value.Text);
        var right = StructureGrid(desired.Value.Text);
        if (left is null || right is null || left.Count != right.Count) return false;

        return left.Zip(right).All(pair => pair.First.SequenceEqual(pair.Second, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Rows of slots, split the way the runtime page compiler splits them.</summary>
    private static List<List<string>>? StructureGrid(string clause)
    {
        var literal = Regex.Match(clause, @"'((?:[^']|'')*)'", RegexOptions.CultureInvariant);
        if (!literal.Success) return null;

        return literal.Groups[1].Value
            .Split(['/', '\n', '\r'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(row => row
                .Split([' ', '\t'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .ToList())
            .ToList();
    }

    private static string PatchHeader(string original, string desired, string pattern)
    {
        var existingMatch = Regex.Match(original, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var desiredMatch = Regex.Match(desired, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!existingMatch.Success || !desiredMatch.Success
            || SemanticNormalize(existingMatch.Value) == SemanticNormalize(desiredMatch.Value))
            return original;
        return original[..existingMatch.Index] + desiredMatch.Value + original[(existingMatch.Index + existingMatch.Length)..];
    }

    private static string PatchClause(string original, string desiredStatement, string keyword)
    {
        var existing = FindClause(original, keyword);
        var desired = FindClause(desiredStatement, keyword);
        if (existing is null && desired is null) return original;
        if (existing is not null && desired is not null
            && SemanticNormalize(existing.Value.Text) == SemanticNormalize(desired.Value.Text))
            return original;

        if (existing is not null && desired is null)
        {
            var end = existing.Value.End;
            while (end < original.Length && char.IsWhiteSpace(original[end])) end++;
            if (end < original.Length && original[end] == ',') end++;
            return original.Remove(existing.Value.Start, end - existing.Value.Start);
        }

        if (existing is null && desired is not null)
        {
            var close = FindOuterClosingParenthesis(original);
            if (close < 0) return original;
            var lineEnding = DetectLineEnding(original);
            var indent = DetectBodyIndent(original);
            var prefix = close > 0 && original[close - 1] is not '\n' and not '\r' ? "," + lineEnding : string.Empty;
            return original.Insert(close, prefix + indent + desired.Value.Text + lineEnding);
        }

        var replacement = PreserveComments(existing!.Value.Text, desired!.Value.Text, DetectLineEnding(original));
        return original[..existing.Value.Start] + replacement + original[existing.Value.End..];
    }

    private static ClauseSpan? FindClause(string text, string keyword)
    {
        var depth = 0;
        var inString = false;
        var inLineComment = false;
        var inBlockComment = false;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (inLineComment)
            {
                if (current is '\r' or '\n') inLineComment = false;
                continue;
            }
            if (inBlockComment)
            {
                if (current == '*' && next == '/') { inBlockComment = false; index++; }
                continue;
            }
            if (!inString && current == '-' && next == '-') { inLineComment = true; index++; continue; }
            if (!inString && current == '/' && next == '*') { inBlockComment = true; index++; continue; }
            if (current == '\'')
            {
                if (inString && next == '\'') { index++; continue; }
                inString = !inString;
                continue;
            }
            if (inString) continue;
            if (current == '(') { depth++; continue; }
            if (current == ')') { depth--; continue; }
            if (depth < 1 || !IsWordAt(text, index, keyword)) continue;

            var start = index;
            var cursor = index + keyword.Length;
            while (cursor < text.Length && char.IsWhiteSpace(text[cursor])) cursor++;
            if (cursor < text.Length && text[cursor] == '=')
            {
                cursor++;
                var end = FindTopLevelDelimiter(text, cursor, depth);
                return new ClauseSpan(start, end, text[start..end]);
            }
            if (cursor < text.Length && text[cursor] == '(')
            {
                var end = FindMatchingParenthesis(text, cursor);
                if (end >= 0) return new ClauseSpan(start, end + 1, text[start..(end + 1)]);
            }
        }
        return null;
    }

    private static int FindTopLevelDelimiter(string text, int start, int targetDepth)
    {
        var depth = targetDepth;
        var inString = false;
        for (var index = start; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (current == '\'')
            {
                if (inString && next == '\'') { index++; continue; }
                inString = !inString;
            }
            if (inString) continue;
            if (current == '(') depth++;
            else if (current == ')')
            {
                if (depth == targetDepth) return index;
                depth--;
            }
            else if (current == ',' && depth == targetDepth) return index;
        }
        return text.Length;
    }

    private static int FindMatchingParenthesis(string text, int open)
    {
        var depth = 0;
        var inString = false;
        var inLineComment = false;
        var inBlockComment = false;
        for (var index = open; index < text.Length; index++)
        {
            var current = text[index];
            var next = index + 1 < text.Length ? text[index + 1] : '\0';
            if (inLineComment) { if (current is '\r' or '\n') inLineComment = false; continue; }
            if (inBlockComment) { if (current == '*' && next == '/') { inBlockComment = false; index++; } continue; }
            if (!inString && current == '-' && next == '-') { inLineComment = true; index++; continue; }
            if (!inString && current == '/' && next == '*') { inBlockComment = true; index++; continue; }
            if (current == '\'')
            {
                if (inString && next == '\'') { index++; continue; }
                inString = !inString;
                continue;
            }
            if (inString) continue;
            if (current == '(') depth++;
            else if (current == ')' && --depth == 0) return index;
        }
        return -1;
    }

    private static int FindOuterClosingParenthesis(string text)
    {
        var open = text.IndexOf('(');
        return open < 0 ? -1 : FindMatchingParenthesis(text, open);
    }

    private static bool IsWordAt(string text, int index, string word)
    {
        if (index + word.Length > text.Length
            || !text.AsSpan(index, word.Length).Equals(word, StringComparison.OrdinalIgnoreCase))
            return false;
        return (index == 0 || !IsWordCharacter(text[index - 1]))
            && (index + word.Length == text.Length || !IsWordCharacter(text[index + word.Length]));
    }

    private static bool IsWordCharacter(char value) => char.IsLetterOrDigit(value) || value == '_';

    private static string PreserveComments(string original, string replacement, string lineEnding)
    {
        var comments = Regex.Matches(original, @"--[^\r\n]*|/\*[\s\S]*?\*/", RegexOptions.CultureInvariant)
            .Select(match => match.Value)
            .Where(comment => !comment.StartsWith("/* ETL-SQL-STUDIO-FILTER ", StringComparison.Ordinal))
            .Where(comment => !replacement.Contains(comment, StringComparison.Ordinal))
            .ToList();
        if (comments.Count == 0) return replacement;
        var close = replacement.LastIndexOf(')');
        if (close < 0) return replacement + " " + string.Join(" ", comments);
        var indent = DetectBodyIndent(original) + "    ";
        var trivia = lineEnding + string.Join(lineEnding, comments.Select(comment => indent + comment)) + lineEnding + DetectBodyIndent(original);
        return replacement.Insert(close, trivia);
    }

    private static string SemanticNormalize(string text)
    {
        var withoutComments = Regex.Replace(text, @"--[^\r\n]*|/\*[\s\S]*?\*/", string.Empty);
        return Regex.Replace(withoutComments, @"\s+", string.Empty).TrimEnd(',').ToUpperInvariant();
    }

    private static string DetectLineEnding(string text) => text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    private static string DetectBodyIndent(string statement)
    {
        var match = Regex.Match(statement, @"(?:\r\n|\n)([ \t]+)\S", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : "    ";
    }

    private static string GetElementName(Statement statement) => statement switch
    {
        CreateVisualStatement visual => visual.Name,
        CreateContainerStatement container => container.Name,
        CreateButtonStatement button => button.Name,
        _ => throw new ArgumentOutOfRangeException(nameof(statement))
    };

    private static string GetStatementText(string script, Statement statement) =>
        script[statement.StartOffset..GetStatementEnd(script, statement)];

    private static int GetStatementEnd(string script, Statement statement)
    {
        var end = Math.Clamp(statement.EndOffset, statement.StartOffset, script.Length);
        if (end < script.Length && script[end] == ';') end++;
        return end;
    }

    private static void AddStatementReplacementIfChanged(
        string script,
        Statement statement,
        string desired,
        List<SpanReplacement> replacements)
    {
        var end = GetStatementEnd(script, statement);
        var original = script[statement.StartOffset..end];
        if (SemanticNormalize(original) != SemanticNormalize(desired))
            replacements.Add(new SpanReplacement(statement.StartOffset, end, PreserveComments(original, desired, DetectLineEnding(script))));
    }

    private static SpanReplacement DeletionReplacement(string script, Statement statement)
    {
        var end = GetStatementEnd(script, statement);
        while (end < script.Length && script[end] is '\r' or '\n') end++;
        return new SpanReplacement(statement.StartOffset, end, string.Empty);
    }

    private static string ApplyReplacements(string script, IEnumerable<SpanReplacement> replacements)
    {
        var ordered = replacements.OrderByDescending(replacement => replacement.Start).ThenByDescending(replacement => replacement.End).ToList();
        if (ordered.Count == 0) return script;
        var builder = new StringBuilder(script);
        var nextStart = int.MaxValue;
        foreach (var replacement in ordered)
        {
            var start = Math.Clamp(replacement.Start, 0, builder.Length);
            var end = Math.Clamp(replacement.End, start, builder.Length);
            if (end > nextStart) throw new InvalidOperationException("Designer script replacements overlap.");
            builder.Remove(start, end - start).Insert(start, replacement.Text);
            nextStart = start;
        }
        return builder.ToString();
    }

    private readonly record struct ClauseSpan(int Start, int End, string Text);
    private readonly record struct SpanReplacement(int Start, int End, string Text);
}
