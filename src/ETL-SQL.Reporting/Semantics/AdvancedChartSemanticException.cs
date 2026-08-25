using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using ETL_SQL.Core;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Reporting.Semantics.Runtime;

/// <summary>
/// A CUSTOM chart authoring failure carrying the positioned diagnostics that describe it.
/// </summary>
/// <remarks>
/// Lowering never fails with a bare message. Every semantic failure reaches <c>VisualBuilder</c> as this
/// exception so preview can publish the same positioned diagnostics the editor's lint pass shows, instead
/// of painting an unpositioned error string inside the rendered visual.
/// </remarks>
public sealed class AdvancedChartSemanticException(IEnumerable<Diagnostic> diagnostics)
    : InvalidOperationException(Summarize(diagnostics))
{
    /// <summary>The positioned diagnostics, in author order.</summary>
    public ImmutableArray<Diagnostic> Diagnostics { get; } = [.. diagnostics];

    /// <summary>Creates an exception for a single failure anchored to the offending AST node.</summary>
    public static AdvancedChartSemanticException At(AstNode node, string message) => new([
        new Diagnostic
        {
            Message = message,
            Line = node.Line,
            Column = node.Column,
            Severity = DiagnosticSeverity.Error,
            Code = AdvancedChartSemanticValidator.DiagnosticCode,
            Source = AdvancedChartSemanticValidator.DiagnosticSource
        }
    ]);

    private static string Summarize(IEnumerable<Diagnostic> diagnostics)
    {
        var messages = diagnostics.Select(diagnostic => diagnostic.Message).ToList();
        return messages.Count == 0
            ? "The CUSTOM chart definition is not valid."
            : string.Join(" ", messages);
    }
}
