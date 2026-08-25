using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Linting.Rules;

/// <summary>Validates renderer-neutral native advanced chart declarations.</summary>
/// <remarks>
/// The rules themselves live in <see cref="AdvancedChartSemanticValidator"/> so that this lint pass and
/// the reporting lowerer run the same checks against the same nodes. This rule only projects the shared
/// diagnostics onto <see cref="LintResult"/>; adding a check here instead of in the validator would
/// re-open the drift between editor diagnostics and report preview.
/// </remarks>
public sealed class AdvancedChartAuthoringRule : ILintRule
{
    public string Name => "AdvancedChartAuthoring";
    public string Description => "Validates CUSTOM CHART layers, scales, coordinates, conditions, and facets.";

    public Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context)
    {
        var results = script.Statements
            .OfType<CreateVisualStatement>()
            .SelectMany(AdvancedChartSemanticValidator.Validate)
            .Select(diagnostic => new LintResult
            {
                RuleName = Name,
                Code = diagnostic.Code,
                Severity = LintSeverity.Error,
                Message = diagnostic.Message,
                LineNumber = diagnostic.Line,
                ColumnNumber = diagnostic.Column
            })
            .ToList();
        return Task.FromResult<IEnumerable<LintResult>>(results);
    }
}
