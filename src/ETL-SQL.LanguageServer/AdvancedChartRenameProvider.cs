using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core;
using OmniSharp.Extensions.LanguageServer.Protocol;
using OmniSharp.Extensions.LanguageServer.Protocol.Client.Capabilities;
using OmniSharp.Extensions.LanguageServer.Protocol.Document;
using OmniSharp.Extensions.LanguageServer.Protocol.Models;
using LSPRange = OmniSharp.Extensions.LanguageServer.Protocol.Models.Range;
using TextDocumentSelector = OmniSharp.Extensions.LanguageServer.Protocol.Models.TextDocumentSelector;

namespace ETL_SQL.LSP;

/// <summary>Scoped rename for layer, scale, and field symbols inside one native CHART declaration.</summary>
public sealed class AdvancedChartRenameProvider(DocumentStateStore store) : IRenameHandler, IPrepareRenameHandler
{
    public Task<WorkspaceEdit?> Handle(RenameParams request, CancellationToken cancellationToken)
    {
        if (!TryResolve(request.TextDocument.Uri, request.Position, out var state, out var symbol) ||
            !Regex.IsMatch(request.NewName ?? string.Empty, @"^[A-Za-z_][A-Za-z0-9_]*$", RegexOptions.CultureInvariant))
            return Task.FromResult<WorkspaceEdit?>(null);
        var edits = symbol.Offsets.Select(offset => new TextEdit
        {
            Range = Range(state.Text, offset, symbol.Name.Length),
            NewText = request.NewName!
        }).ToArray();
        WorkspaceEdit edit = new()
        {
            Changes = new Dictionary<DocumentUri, IEnumerable<TextEdit>> { [request.TextDocument.Uri] = edits }
        };
        return Task.FromResult<WorkspaceEdit?>(edit);
    }

    public Task<RangeOrPlaceholderRange?> Handle(PrepareRenameParams request, CancellationToken cancellationToken) =>
        Task.FromResult(TryResolve(request.TextDocument.Uri, request.Position, out var state, out var symbol)
            ? (RangeOrPlaceholderRange?)Range(state.Text, symbol.CursorOffset, symbol.Name.Length)
            : null);

    public RenameRegistrationOptions GetRegistrationOptions(RenameCapability capability, ClientCapabilities clientCapabilities) => new()
    {
        DocumentSelector = TextDocumentSelector.ForLanguage("etlsql", "rptsql"),
        PrepareProvider = true
    };

    private bool TryResolve(DocumentUri uri, Position position, out DocumentState state, out RenameSymbol symbol)
    {
        symbol = default!;
        if (!store.TryGetState(uri, out state!)) return false;
        var cursor = Offset(state.Text, (int)position.Line, (int)position.Character);
        var visual = state.Script.Statements.OfType<CreateVisualStatement>()
            .FirstOrDefault(item => item.AdvancedChart is not null && cursor >= item.StartOffset && cursor <= item.EndOffset);
        if (visual?.AdvancedChart is not { } chart) return false;
        var word = WordAt(state.Text, cursor);
        if (word is null) return false;
        var chartStart = Math.Max(0, chart.StartOffset);
        var chartEnd = chart.EndOffset > chartStart ? Math.Min(state.Text.Length, chart.EndOffset) : Math.Min(state.Text.Length, visual.EndOffset);
        var chartText = state.Text[chartStart..chartEnd];

        var scale = chart.Scales.FirstOrDefault(item => item.Name.Equals(word.Value.Name, StringComparison.OrdinalIgnoreCase));
        if (scale is not null)
        {
            var offsets = Captures(chartText, chartStart,
                $@"(?ix)(?:\bSCALE\s*=\s*|(?:^|[,\(])\s*)(?<name>{Regex.Escape(scale.Name)})\b(?=\s*(?:=|[,\)]))", "name");
            symbol = new RenameSymbol(scale.Name, word.Value.Start, offsets);
            return offsets.Contains(word.Value.Start);
        }
        var layer = chart.Layers.FirstOrDefault(item => item.Name.Equals(word.Value.Name, StringComparison.OrdinalIgnoreCase));
        if (layer is not null)
        {
            var offsets = Captures(chartText, chartStart,
                $@"(?ix)(?:^|[,\(])\s*(?<name>{Regex.Escape(layer.Name)})\b(?=\s*=\s*(?:RECT|LINE|AREA|POINT|RULE|ARC|TEXT)\b)", "name");
            symbol = new RenameSymbol(layer.Name, word.Value.Start, offsets);
            return offsets.Contains(word.Value.Start);
        }

        var fields = chart.Layers.SelectMany(item => item.Encodings.Select(encoding => encoding.Field))
            .Concat(chart.Layers.SelectMany(item => item.Conditions.SelectMany(condition => condition.Predicate.GetSourceColumns())))
            .Concat(new[] { chart.Facet?.RowField, chart.Facet?.ColumnField }.Where(item => item is not null).Cast<string>())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!fields.Contains(word.Value.Name)) return false;
        var fieldOffsets = IdentifierOffsets(chartText, chartStart, word.Value.Name);
        symbol = new RenameSymbol(word.Value.Name, word.Value.Start, fieldOffsets);
        return fieldOffsets.Contains(word.Value.Start);
    }

    private static List<int> Captures(string text, int baseOffset, string pattern, string group) =>
        Regex.Matches(text, pattern, RegexOptions.Multiline | RegexOptions.CultureInvariant)
            .Select(match => baseOffset + match.Groups[group].Index).Distinct().Order().ToList();

    private static List<int> IdentifierOffsets(string text, int baseOffset, string name)
    {
        var result = new List<int>();
        var inString = false;
        var inLineComment = false;
        var blockDepth = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (inLineComment) { if (text[index] is '\r' or '\n') inLineComment = false; else continue; }
            if (blockDepth > 0) { if (index + 1 < text.Length && text[index] == '*' && text[index + 1] == '/') { blockDepth--; index++; } continue; }
            if (!inString && index + 1 < text.Length && text[index] == '-' && text[index + 1] == '-') { inLineComment = true; index++; continue; }
            if (!inString && index + 1 < text.Length && text[index] == '/' && text[index + 1] == '*') { blockDepth++; index++; continue; }
            if (text[index] == '\'') { if (inString && index + 1 < text.Length && text[index + 1] == '\'') { index++; continue; } inString = !inString; continue; }
            if (inString || !(char.IsLetter(text[index]) || text[index] == '_')) continue;
            var start = index;
            while (index + 1 < text.Length && (char.IsLetterOrDigit(text[index + 1]) || text[index + 1] == '_')) index++;
            if (text.AsSpan(start, index - start + 1).Equals(name, StringComparison.OrdinalIgnoreCase)) result.Add(baseOffset + start);
        }
        return result;
    }

    private static (string Name, int Start)? WordAt(string text, int offset)
    {
        if (text.Length == 0) return null;
        offset = Math.Clamp(offset, 0, text.Length - 1);
        if (!(char.IsLetterOrDigit(text[offset]) || text[offset] == '_') && offset > 0) offset--;
        var start = offset;
        while (start > 0 && (char.IsLetterOrDigit(text[start - 1]) || text[start - 1] == '_')) start--;
        var end = offset;
        while (end < text.Length && (char.IsLetterOrDigit(text[end]) || text[end] == '_')) end++;
        return end <= start ? null : (text[start..end], start);
    }

    private static int Offset(string text, int line, int character)
    {
        var offset = 0;
        for (var currentLine = 0; currentLine < line && offset < text.Length; currentLine++)
        {
            var newline = text.IndexOf('\n', offset);
            offset = newline < 0 ? text.Length : newline + 1;
        }
        return Math.Min(text.Length, offset + character);
    }

    private static LSPRange Range(string text, int offset, int length)
    {
        static Position PositionAt(string source, int value)
        {
            var line = 0; var lineStart = 0;
            for (var index = 0; index < value && index < source.Length; index++)
                if (source[index] == '\n') { line++; lineStart = index + 1; }
            return new Position(line, value - lineStart);
        }
        return new LSPRange(PositionAt(text, offset), PositionAt(text, offset + length));
    }

    private sealed record RenameSymbol(string Name, int CursorOffset, List<int> Offsets);
}
