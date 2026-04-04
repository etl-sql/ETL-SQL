using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting
{
    /// <summary>
    /// Orchestrates the linting process by executing multiple <see cref="ILintRule"/> instances.
    /// </summary>
    public class Linter
    {
        private readonly List<ILintRule> _rules = new();

        /// <summary>Adds a new linting rule to the linter.</summary>
        public void AddRule(ILintRule rule)
        {
            _rules.Add(rule);
        }

        /// <summary>Analyzes the script against all registered rules.</summary>
        public async Task<List<LintResult>> AnalyzeAsync(Script script, ILintContext context)
        {
            var results = new List<LintResult>();
            foreach (var rule in _rules)
            {
                var ruleResults = await rule.AnalyzeAsync(script, context);
                results.AddRange(ruleResults);
            }
            return results;
        }
    }
}
