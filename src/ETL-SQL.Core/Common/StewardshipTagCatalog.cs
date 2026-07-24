using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ETL_SQL.Common;

public enum StewardshipTagValueKind
{
    String,
    Boolean,
    Enum,
    Duration
}

public sealed record StewardshipTagDefinition(
    string Name,
    StewardshipTagValueKind ValueKind,
    IReadOnlyList<string> Scopes,
    IReadOnlyList<string> AllowedValues,
    IReadOnlyList<string> Aliases,
    string? DeprecatedBy = null)
{
    public bool IsDeprecated => !string.IsNullOrWhiteSpace(DeprecatedBy);
}

public sealed record StewardshipTagValidationResult(
    string TagName,
    string CanonicalName,
    bool IsValid,
    string? Message,
    bool IsDeprecated = false);

public static class StewardshipTagCatalog
{
    private static readonly Regex DurationPattern = new(@"^\d+[smhd]$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly string[] TableColumnScriptScopes = ["script", "table", "column"];
    private static readonly string[] TableColumnScopes = ["table", "column"];

    private static readonly StewardshipTagDefinition[] Definitions =
    [
        new("owner", StewardshipTagValueKind.String, TableColumnScriptScopes, [], []),
        new("steward", StewardshipTagValueKind.String, TableColumnScriptScopes, [], []),
        new("contact", StewardshipTagValueKind.String, TableColumnScriptScopes, [], []),
        new("domain", StewardshipTagValueKind.String, TableColumnScriptScopes, [], []),
        new("classification", StewardshipTagValueKind.Enum, TableColumnScopes, ["public", "internal", "confidential", "restricted"], ["sensitivity"]),
        new("quality", StewardshipTagValueKind.Enum, TableColumnScopes, ["gold", "silver", "bronze"], []),
        new("pii", StewardshipTagValueKind.Boolean, TableColumnScopes, [], []),
        new("phi", StewardshipTagValueKind.Boolean, TableColumnScopes, [], []),
        new("pci", StewardshipTagValueKind.Boolean, TableColumnScopes, [], []),
        new("sensitive", StewardshipTagValueKind.Boolean, TableColumnScopes, [], []),
        new("encrypted_at_rest", StewardshipTagValueKind.Boolean, TableColumnScopes, [], []),
        new("nullable", StewardshipTagValueKind.Boolean, ["column"], [], []),
        new("freshness", StewardshipTagValueKind.Duration, ["table"], [], []),
        new("load_pattern", StewardshipTagValueKind.Enum, ["table"], ["full", "incremental", "cdc"], []),
        new("tags", StewardshipTagValueKind.String, TableColumnScriptScopes, [], []),
        new("category", StewardshipTagValueKind.String, TableColumnScriptScopes, [], []),
        new("certification", StewardshipTagValueKind.String, TableColumnScriptScopes, [], []),
        new("trusted", StewardshipTagValueKind.Boolean, TableColumnScriptScopes, [], []),
        new("sla", StewardshipTagValueKind.String, ["table"], [], []),
        new("d", StewardshipTagValueKind.String, TableColumnScopes, [], []),
        new("example", StewardshipTagValueKind.String, ["column"], [], []),
        new("unit", StewardshipTagValueKind.String, ["column"], [], []),
        new("format", StewardshipTagValueKind.String, ["column"], [], []),
        new("source_system", StewardshipTagValueKind.String, ["table"], [], []),
        new("source_table", StewardshipTagValueKind.String, ["table"], [], []),
        new("source_column", StewardshipTagValueKind.String, ["column"], [], []),
        // Data-quality rule pair (numbered variants @expect_1/@fail_1 resolve to these base
        // definitions). The @expect rule grammar is too rich for the value-kind validator —
        // ColumnRuleValidationRule in Analysis runs the real ColumnRuleParser.
        new("expect", StewardshipTagValueKind.String, ["column"], [], []),
        new("fail", StewardshipTagValueKind.Enum, ["column"], ["THROW", "WARN", "QUARANTINE"], [])
    ];

    // @expect_1/@fail_1 … pair numbered rules with numbered actions on one column; they share
    // the base tag's definition so catalog lookups, lint, and the stewardship read side treat
    // them as first-class.
    private static readonly Regex NumberedRuleTagPattern =
        new(@"^(expect|fail)_\d+$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Dictionary<string, StewardshipTagDefinition> ByName =
        Definitions.ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);

    private static readonly Dictionary<string, StewardshipTagDefinition> ByAlias =
        Definitions
            .SelectMany(d => d.Aliases.Select(alias => (alias, definition: d)))
            .ToDictionary(x => x.alias, x => x.definition, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<StewardshipTagDefinition> StandardTags => Definitions;

    public static IReadOnlyCollection<string> StandardTagNames =>
        Definitions.Select(d => d.Name).Concat(ByAlias.Keys).ToArray();

    public static IReadOnlyCollection<string> RequiredStewardshipTags =>
        ["owner", "steward", "contact", "classification", "quality"];

    public static bool IsKnown(string tagName) => Resolve(tagName) is not null;

    public static bool IsCustomOrganizationTag(string tagName) =>
        tagName.StartsWith("org_", StringComparison.OrdinalIgnoreCase)
        || tagName.StartsWith("x_", StringComparison.OrdinalIgnoreCase)
        || tagName.StartsWith("custom_", StringComparison.OrdinalIgnoreCase);

    public static bool IsKnownOrCustom(string tagName) => IsKnown(tagName) || IsCustomOrganizationTag(tagName);

    public static StewardshipTagDefinition? Resolve(string tagName)
    {
        if (ByName.TryGetValue(tagName, out var definition)) return definition;
        if (ByAlias.TryGetValue(tagName, out definition)) return definition;
        var numbered = NumberedRuleTagPattern.Match(tagName);
        return numbered.Success ? ByName[numbered.Groups[1].Value] : null;
    }

    public static string Canonicalize(string tagName) => Resolve(tagName)?.Name ?? tagName;

    public static StewardshipTagValidationResult Validate(string tagName, string? value)
    {
        var definition = Resolve(tagName);
        if (definition is null)
            return new StewardshipTagValidationResult(tagName, tagName, true, null);

        var canonical = definition.Name;
        var isAlias = !tagName.Equals(canonical, StringComparison.OrdinalIgnoreCase)
                      && !NumberedRuleTagPattern.IsMatch(tagName);
        if (isAlias)
        {
            return new StewardshipTagValidationResult(
                tagName,
                canonical,
                false,
                $"Tag '@{tagName}' is a deprecated alias for '@{canonical}'. Use '@{canonical}' instead.",
                IsDeprecated: true);
        }

        // The comment-tag layer preserves outer quotes on values (@fail: 'THROW'); strip one
        // matching pair before kind validation so quoted and bare values validate identically.
        value = ETL_SQL.Core.Quality.ColumnRuleParser.Unquote(value ?? string.Empty);
        return definition.ValueKind switch
        {
            StewardshipTagValueKind.Boolean when !IsBoolean(value) =>
                Invalid(tagName, canonical, $"Tag '@{tagName}' expects a boolean (true/false), got '{value}'."),
            StewardshipTagValueKind.Enum when !definition.AllowedValues.Any(v => value.Equals(v, StringComparison.OrdinalIgnoreCase)) =>
                Invalid(tagName, canonical, $"Tag '@{tagName}' value '{value}' is not one of: {string.Join(", ", definition.AllowedValues)}."),
            StewardshipTagValueKind.Duration when !DurationPattern.IsMatch(value) =>
                Invalid(tagName, canonical, $"Tag '@{tagName}' value '{value}' is not a duration. Use a number followed by s, m, h, or d (e.g. '1h', '24h', '7d')."),
            _ => new StewardshipTagValidationResult(tagName, canonical, true, null)
        };
    }

    private static bool IsBoolean(string value) =>
        value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("false", StringComparison.OrdinalIgnoreCase);

    private static StewardshipTagValidationResult Invalid(string tagName, string canonical, string message) =>
        new(tagName, canonical, false, message);
}
