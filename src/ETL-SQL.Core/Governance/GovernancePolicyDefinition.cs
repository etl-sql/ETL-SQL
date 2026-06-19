using System.Collections.ObjectModel;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// Typed metadata for one centrally governed policy key.
/// </summary>
public sealed record GovernancePolicyDefinition
{
    public GovernancePolicyDefinition(
        string key,
        GovernancePolicyScope scope,
        GovernancePolicyClassification classification,
        GovernancePolicyValueKind valueKind,
        string description,
        object? defaultValue = null,
        object? minimumValue = null,
        object? maximumValue = null,
        IEnumerable<string>? allowedValues = null)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Policy key is required.", nameof(key));
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Policy description is required.", nameof(description));

        Key = NormalizeKey(key);
        Scope = scope;
        Classification = classification;
        ValueKind = valueKind;
        Description = description.Trim();
        DefaultValue = defaultValue;
        MinimumValue = minimumValue;
        MaximumValue = maximumValue;
        AllowedValues = new ReadOnlyCollection<string>(
            (allowedValues ?? Array.Empty<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray());
    }

    public string Key { get; }
    public GovernancePolicyScope Scope { get; }
    public GovernancePolicyClassification Classification { get; }
    public GovernancePolicyValueKind ValueKind { get; }
    public string Description { get; }
    public object? DefaultValue { get; }
    public object? MinimumValue { get; }
    public object? MaximumValue { get; }
    public IReadOnlyList<string> AllowedValues { get; }

    public static string NormalizeKey(string key) => key.Trim().Replace("__", ":", StringComparison.Ordinal);
}

