using System;
using System.Linq;

namespace ETL_SQL.Core.Linting
{
    /// <summary>
    /// Creates pre-configured <see cref="Linter"/> instances.
    /// Centralises the reflection-based rule discovery that was previously
    /// duplicated in LintStatementHandler and ExecutionSession.
    /// </summary>
    public static class LinterFactory
    {
        /// <summary>
        /// Returns a <see cref="Linter"/> loaded with every <see cref="ILintRule"/>
        /// implementation found in the ETL-SQL.Core assembly.
        /// </summary>
        public static Linter CreateWithAllRules()
        {
            var linter = new Linter();
            foreach (var type in typeof(ILintRule).Assembly.GetTypes()
                .Where(t => typeof(ILintRule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract))
            {
                if (Activator.CreateInstance(type) is ILintRule rule)
                    linter.AddRule(rule);
            }
            return linter;
        }
    }
}
