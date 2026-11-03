using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Analysis.Linting;
/// <summary>
/// Creates pre-configured <see cref="Linter"/> instances.
/// Centralises the reflection-based rule discovery that was previously
/// duplicated in LintStatementHandler and ExecutionSession.
/// </summary>
public static class LinterFactory
{
    public const string AvoidSelectStarEnabledKey = "Linting:AvoidSelectStar:Enabled";

    private static readonly Type[] RuleTypes = typeof(ILintRule).Assembly.GetTypes()
        .Where(t => typeof(ILintRule).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract)
        .ToArray();

    /// <summary>
    /// Returns a <see cref="Linter"/> loaded with every <see cref="ILintRule"/>
    /// implementation found in the ETL-SQL.Analysis assembly.
    /// If a service provider is provided, it will check for registered rules first.
    /// </summary>
    /// <param name="readDependencyFiles">
    /// False on a server that lints scripts its users submit (the Portal designer, the
    /// Orchestrator): the dependency preflight reads the files a literal <c>RUN SCRIPT</c> names
    /// from the server's disk and echoes their contents back in diagnostics.
    /// </param>
    public static Linter CreateWithAllRules(IServiceProvider? serviceProvider = null, bool readDependencyFiles = true)
    {
        var linter = new Linter();
        var includeAvoidSelectStar = IsAvoidSelectStarEnabled(serviceProvider);

        if (serviceProvider != null)
        {
            var registeredRules = serviceProvider.GetServices<ILintRule>();
            foreach (var rule in registeredRules)
            {
                if (!includeAvoidSelectStar && rule is Rules.AvoidSelectStarRule) continue;
                if (!readDependencyFiles && rule is Rules.RunScriptDependencyPreflightRule) continue;
                linter.AddRule(rule);
            }
        }

        // Also load any rules NOT registered in DI but present in the assembly
        foreach (var type in RuleTypes)
        {
            if (!includeAvoidSelectStar && type == typeof(Rules.AvoidSelectStarRule)) continue;
            if (!readDependencyFiles && type == typeof(Rules.RunScriptDependencyPreflightRule)) continue;

            // Skip if already added via DI
            if (linter.HasRuleOfType(type)) continue;

            if (Activator.CreateInstance(type) is ILintRule rule)
                linter.AddRule(rule);
        }
        return linter;
    }

    private static bool IsAvoidSelectStarEnabled(IServiceProvider? serviceProvider)
    {
        if (serviceProvider?.GetService<IConfiguration>() is not { } configuration)
            return false;

        return bool.TryParse(configuration[AvoidSelectStarEnabledKey], out var enabled) && enabled;
    }
}
