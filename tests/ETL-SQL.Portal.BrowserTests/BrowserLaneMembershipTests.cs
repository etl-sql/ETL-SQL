using System.Reflection;

namespace ETL_SQL.Portal.BrowserTests;

/// <summary>
/// Every lane that runs this project selects by <c>Category</c>, so a test class without one runs
/// nowhere and reports nothing. Two classes sat that way until v0.22.0.
/// </summary>
[Trait("Category", "Browser")]
public sealed class BrowserLaneMembershipTests
{
    [Fact]
    public void EveryTestClassCarriesACategoryALaneSelects()
    {
        var untagged = typeof(BrowserLaneMembershipTests).Assembly.GetTypes()
            .Where(type => type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .Any(method => method.GetCustomAttributes<FactAttribute>(inherit: true).Any()))
            .Where(type => !Categories(type).Any())
            .Select(type => type.FullName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(untagged.Length == 0,
            "No lane runs these classes; add [Trait(\"Category\", \"Browser\")]:\n" + string.Join("\n", untagged));
    }

    private static IEnumerable<string> Categories(Type type)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            foreach (var attribute in current.GetCustomAttributesData())
            {
                if (attribute.AttributeType != typeof(TraitAttribute)) continue;
                if (attribute.ConstructorArguments is [{ Value: "Category" }, { Value: string value }]) yield return value;
            }
        }
    }
}
