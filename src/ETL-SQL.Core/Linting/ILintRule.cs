using System.Collections.Generic;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Linting
{
    /// <summary>
    /// Defines the contract for a linting rule that analyzes an ETL-SQL script.
    /// </summary>
    public interface ILintRule
    {
        /// <summary>The unique name of the linting rule.</summary>
        string Name { get; }
        /// <summary>A brief description of what the rule checks for.</summary>
        string Description { get; }
        /// <summary>Analyzes the script and returns a collection of linting results.</summary>
        Task<IEnumerable<LintResult>> AnalyzeAsync(Script script, ILintContext context);
    }
}
