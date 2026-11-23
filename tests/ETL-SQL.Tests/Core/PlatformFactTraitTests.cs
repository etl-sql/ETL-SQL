using System;
using System.Linq;
using System.Reflection;
using Xunit;

namespace ETL_SQL.Tests.Core
{
    /// <summary>
    /// A platform-only test skips on the other OS, and Enterprise Certification fails on any
    /// skipped result. It selects each platform's tests with <c>Platform!=Unix</c> or
    /// <c>Platform!=Windows</c>, so every <c>[WindowsFact]</c>, <c>[WindowsTheory]</c> and
    /// <c>[UnixFact]</c> must carry the matching <c>Platform</c> trait.
    /// </summary>
    public class PlatformFactTraitTests
    {
        [Fact]
        public void EveryPlatformOnlyTest_CarriesItsPlatformTrait()
        {
            var missing = typeof(PlatformFactTraitTests).Assembly.GetTypes()
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
                .Select(method => (Method: method, Platform: PlatformOf(method)))
                .Where(entry => entry.Platform != null && !HasTrait(entry.Method, entry.Platform))
                .Select(entry => $"{entry.Method.DeclaringType!.FullName}.{entry.Method.Name} needs [Trait(\"Platform\", \"{entry.Platform}\")]")
                .OrderBy(line => line, StringComparer.Ordinal)
                .ToList();

            Assert.True(missing.Count == 0, string.Join(Environment.NewLine, missing));
        }

        private static string? PlatformOf(MethodInfo method)
        {
            if (method.IsDefined(typeof(WindowsFactAttribute)) || method.IsDefined(typeof(WindowsTheoryAttribute))) return "Windows";
            if (method.IsDefined(typeof(UnixFactAttribute))) return "Unix";
            return null;
        }

        // A trait's name and value are read from its constructor arguments, as the test runner does.
        private static bool HasTrait(MethodInfo method, string platform) =>
            method.GetCustomAttributesData()
                .Concat(method.DeclaringType!.GetCustomAttributesData())
                .Where(data => data.AttributeType == typeof(TraitAttribute) && data.ConstructorArguments.Count == 2)
                .Any(data => (string?)data.ConstructorArguments[0].Value == "Platform"
                    && (string?)data.ConstructorArguments[1].Value == platform);
    }
}
