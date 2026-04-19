using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

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
        /// If a service provider is provided, it will check for registered rules first.
        /// </summary>
        public static Linter CreateWithAllRules(IServiceProvider? serviceProvider = null)
        {
            var linter = new Linter();
            
            if (serviceProvider != null)
            {
                var registeredRules = serviceProvider.GetServices<ILintRule>();
                foreach (var rule in registeredRules)
                {
                    linter.AddRule(rule);
                }
            }

            // Also load any rules NOT registered in DI but present in the assembly
            var ruleTypes = typeof(ILintRule).Assembly.GetTypes()
                .Where(t => typeof(ILintRule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

            foreach (var type in ruleTypes)
            {
                // Skip if already added via DI
                if (linter.HasRuleOfType(type)) continue;

                if (Activator.CreateInstance(type) is ILintRule rule)
                    linter.AddRule(rule);
            }
            return linter;
        }
    }
}
