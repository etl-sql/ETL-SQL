using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace ETL_SQL.Core.Data;

public sealed record ProtectedLineageHistoryEntry(
    long Id,
    DateTime RunAt,
    string? JobName,
    string? ScriptPath,
    string TargetTable,
    string? TargetColumn,
    IReadOnlyList<string> SourceTables,
    string Operation,
    IReadOnlyDictionary<string, string> Tags,
    IReadOnlyList<string> ProtectionTags,
    string ProtectionReason,
    string? Owner,
    string? Steward,
    string? Contact,
    string? Domain,
    string? Classification,
    string? Quality,
    string? SourceFile,
    int Line);

public sealed record ProtectedDataSuggestionEntry(
    long Id,
    DateTime RunAt,
    string? JobName,
    string? ScriptPath,
    string TargetTable,
    string? TargetColumn,
    IReadOnlyList<string> SourceTables,
    IReadOnlyList<string> SourceColumns,
    string SuggestedTag,
    string SuggestedValue,
    decimal Confidence,
    string EvidenceKind,
    string Evidence,
    string Reason,
    IReadOnlyDictionary<string, string> ExistingTags,
    string? SourceFile,
    int Line);

public static class LineageProtectedData
{
    private static readonly string[] TruthyProtectedTags = ["pii", "phi", "pci", "sensitive"];
    private static readonly string[] ProtectedClassifications = ["confidential", "restricted"];
    private static readonly ProtectedDataRule[] Rules =
    [
        new("pii", "true", 0.95m, ["email", "e_mail", "email_address"], "email identifier"),
        new("pii", "true", 0.96m, ["ssn", "social_security", "socialsecurity", "tax_id", "tin", "national_id"], "government identifier"),
        new("classification", "restricted", 0.90m, ["ssn", "social_security", "socialsecurity", "tax_id", "tin", "national_id"], "government identifier"),
        new("pii", "true", 0.88m, ["dob", "birth_date", "birthdate", "date_of_birth"], "birth date"),
        new("pii", "true", 0.82m, ["phone", "mobile", "cell_phone", "telephone"], "phone number"),
        new("pii", "true", 0.80m, ["address", "street", "postal", "zip_code", "zipcode"], "address"),
        new("pii", "true", 0.74m, ["first_name", "firstname", "last_name", "lastname", "full_name", "fullname", "customer_name", "patient_name"], "personal name"),
        new("pci", "true", 0.96m, ["credit_card", "creditcard", "card_number", "cardnumber", "pan", "cvv"], "payment card data"),
        new("classification", "restricted", 0.90m, ["credit_card", "creditcard", "card_number", "cardnumber", "pan", "cvv"], "payment card data"),
        new("phi", "true", 0.94m, ["mrn", "medical_record", "diagnosis", "icd", "patient_id", "patientid"], "health information"),
        new("classification", "restricted", 0.88m, ["mrn", "medical_record", "diagnosis", "icd", "patient_id", "patientid"], "health information"),
        new("sensitive", "true", 0.94m, ["password", "passwd", "secret", "token", "api_key", "apikey", "private_key"], "secret material"),
        new("classification", "restricted", 0.88m, ["password", "passwd", "secret", "token", "api_key", "apikey", "private_key"], "secret material")
    ];

    private static readonly Regex EmailValuePattern = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SsnValuePattern = new(@"^\d{3}-?\d{2}-?\d{4}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CardValuePattern = new(@"^\d{4}[- ]?\d{4}[- ]?\d{4}[- ]?\d{4}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static IEnumerable<ProtectedLineageHistoryEntry> FromHistory(IEnumerable<LineageHistoryEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (!TryBuildProtection(entry.Tags, out var protectionTags, out var reason))
                continue;

            yield return new ProtectedLineageHistoryEntry(
                entry.Id,
                entry.RunAt,
                entry.JobName,
                entry.ScriptPath,
                entry.TargetTable,
                entry.TargetColumn,
                entry.SourceTables,
                entry.Operation,
                entry.Tags,
                protectionTags,
                reason,
                GetTag(entry.Tags, "owner"),
                GetTag(entry.Tags, "steward"),
                GetTag(entry.Tags, "contact"),
                GetTag(entry.Tags, "domain"),
                GetTag(entry.Tags, "classification"),
                GetTag(entry.Tags, "quality"),
                entry.SourceFile,
                entry.Line);
        }
    }

    public static bool IsProtected(IReadOnlyDictionary<string, string> tags) =>
        TryBuildProtection(tags, out _, out _);

    public static IEnumerable<ProtectedDataSuggestionEntry> SuggestFromHistory(
        IEnumerable<LineageHistoryEntry> entries,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? sampledValuesByColumn = null)
    {
        foreach (var entry in entries)
        {
            var suggestions = new Dictionary<string, ProtectedDataSuggestionEntry>(StringComparer.OrdinalIgnoreCase);

            foreach (var suggestion in SuggestFromName(entry, entry.TargetColumn, "TargetColumn"))
                AddBest(suggestions, suggestion);

            foreach (var sourceColumn in entry.SourceColumns ?? [])
            {
                foreach (var suggestion in SuggestFromName(entry, sourceColumn, "SourceColumn"))
                    AddBest(suggestions, suggestion);
            }

            foreach (var suggestion in SuggestFromMetadata(entry))
                AddBest(suggestions, suggestion);

            foreach (var suggestion in SuggestFromSamples(entry, sampledValuesByColumn))
                AddBest(suggestions, suggestion);

            foreach (var suggestion in suggestions.Values
                .OrderByDescending(s => s.Confidence)
                .ThenBy(s => s.TargetTable, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.TargetColumn, StringComparer.OrdinalIgnoreCase)
                .ThenBy(s => s.SuggestedTag, StringComparer.OrdinalIgnoreCase))
            {
                yield return suggestion;
            }
        }
    }

    public static bool TryBuildProtection(
        IReadOnlyDictionary<string, string> tags,
        out IReadOnlyList<string> protectionTags,
        out string reason)
    {
        var matches = new List<string>();

        foreach (var tag in TruthyProtectedTags)
        {
            if (HasTruthyTag(tags, tag))
                matches.Add($"@{tag}=true");
        }

        var classification = GetTag(tags, "classification");
        if (!string.IsNullOrWhiteSpace(classification)
            && ProtectedClassifications.Contains(classification, StringComparer.OrdinalIgnoreCase))
        {
            matches.Add($"@classification={classification}");
        }

        protectionTags = matches;
        reason = string.Join(", ", matches);
        return matches.Count > 0;
    }

    private static IEnumerable<ProtectedDataSuggestionEntry> SuggestFromName(
        LineageHistoryEntry entry,
        string? columnName,
        string evidenceKind)
    {
        if (string.IsNullOrWhiteSpace(columnName))
            yield break;

        var normalized = Normalize(columnName);
        foreach (var rule in Rules)
        {
            if (HasTag(entry.Tags, rule.Tag))
                continue;
            if (!rule.Patterns.Any(pattern => ContainsNormalizedTerm(normalized, pattern)))
                continue;

            yield return BuildSuggestion(
                entry,
                rule,
                evidenceKind,
                columnName,
                $"Column name suggests {rule.Reason}.");
        }
    }

    private static IEnumerable<ProtectedDataSuggestionEntry> SuggestFromMetadata(LineageHistoryEntry entry)
    {
        foreach (var tag in entry.Tags)
        {
            var key = tag.Key ?? "";
            var value = tag.Value ?? "";
            if (!IsMetadataHintKey(key))
                continue;

            var normalized = Normalize($"{key} {value}");
            foreach (var rule in Rules)
            {
                if (HasTag(entry.Tags, rule.Tag))
                    continue;
                if (!rule.Patterns.Any(pattern => ContainsNormalizedTerm(normalized, pattern)))
                    continue;

                yield return BuildSuggestion(
                    entry,
                    rule with { Confidence = Math.Min(rule.Confidence, 0.86m) },
                    "CatalogMetadata",
                    $"@{key}={value}",
                    $"Catalog metadata suggests {rule.Reason}.");
            }
        }
    }

    private static IEnumerable<ProtectedDataSuggestionEntry> SuggestFromSamples(
        LineageHistoryEntry entry,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? sampledValuesByColumn)
    {
        if (sampledValuesByColumn is null || string.IsNullOrWhiteSpace(entry.TargetColumn))
            yield break;

        var sampleValues = GetSampleValues(sampledValuesByColumn, entry.TargetTable, entry.TargetColumn).Take(20).ToList();
        if (sampleValues.Count == 0)
            yield break;

        if (!HasTag(entry.Tags, "pii") && sampleValues.Any(v => EmailValuePattern.IsMatch(v)))
            yield return BuildSuggestion(entry, new ProtectedDataRule("pii", "true", 0.92m, [], "email value"), "SampleValue", entry.TargetColumn, "Sampled values match an email pattern.");

        if (!HasTag(entry.Tags, "pii") && sampleValues.Any(v => SsnValuePattern.IsMatch(v)))
            yield return BuildSuggestion(entry, new ProtectedDataRule("pii", "true", 0.94m, [], "government identifier value"), "SampleValue", entry.TargetColumn, "Sampled values match an SSN-like pattern.");

        if (!HasTag(entry.Tags, "classification") && sampleValues.Any(v => SsnValuePattern.IsMatch(v)))
            yield return BuildSuggestion(entry, new ProtectedDataRule("classification", "restricted", 0.90m, [], "government identifier value"), "SampleValue", entry.TargetColumn, "Sampled values match an SSN-like pattern.");

        if (!HasTag(entry.Tags, "pci") && sampleValues.Any(v => CardValuePattern.IsMatch(v)))
            yield return BuildSuggestion(entry, new ProtectedDataRule("pci", "true", 0.92m, [], "payment card value"), "SampleValue", entry.TargetColumn, "Sampled values match a payment-card pattern.");

        if (!HasTag(entry.Tags, "classification") && sampleValues.Any(v => CardValuePattern.IsMatch(v)))
            yield return BuildSuggestion(entry, new ProtectedDataRule("classification", "restricted", 0.90m, [], "payment card value"), "SampleValue", entry.TargetColumn, "Sampled values match a payment-card pattern.");
    }

    private static ProtectedDataSuggestionEntry BuildSuggestion(
        LineageHistoryEntry entry,
        ProtectedDataRule rule,
        string evidenceKind,
        string evidence,
        string reason) =>
        new(
            entry.Id,
            entry.RunAt,
            entry.JobName,
            entry.ScriptPath,
            entry.TargetTable,
            entry.TargetColumn,
            entry.SourceTables,
            entry.SourceColumns ?? [],
            "@" + rule.Tag,
            rule.Value,
            rule.Confidence,
            evidenceKind,
            evidence,
            reason,
            entry.Tags,
            entry.SourceFile,
            entry.Line);

    private static void AddBest(
        Dictionary<string, ProtectedDataSuggestionEntry> suggestions,
        ProtectedDataSuggestionEntry suggestion)
    {
        var key = $"{suggestion.TargetTable}\u001f{suggestion.TargetColumn}\u001f{suggestion.SuggestedTag}\u001f{suggestion.SuggestedValue}";
        if (!suggestions.TryGetValue(key, out var existing) || suggestion.Confidence > existing.Confidence)
            suggestions[key] = suggestion;
    }

    private static IEnumerable<string> GetSampleValues(
        IReadOnlyDictionary<string, IReadOnlyList<string>> sampledValuesByColumn,
        string targetTable,
        string targetColumn)
    {
        if (sampledValuesByColumn.TryGetValue($"{targetTable}.{targetColumn}", out var scoped))
            return scoped.Where(v => !string.IsNullOrWhiteSpace(v));
        if (sampledValuesByColumn.TryGetValue(targetColumn, out var unscoped))
            return unscoped.Where(v => !string.IsNullOrWhiteSpace(v));
        return [];
    }

    private static bool IsMetadataHintKey(string key) =>
        key.Equals("format", StringComparison.OrdinalIgnoreCase)
        || key.Equals("semantic_type", StringComparison.OrdinalIgnoreCase)
        || key.Equals("data_type", StringComparison.OrdinalIgnoreCase)
        || key.Equals("source_type", StringComparison.OrdinalIgnoreCase)
        || key.Equals("classification_hint", StringComparison.OrdinalIgnoreCase);

    private static bool HasTruthyTag(IReadOnlyDictionary<string, string> tags, string key)
    {
        var value = GetTag(tags, key);
        return value is not null
            && (value.Equals("true", StringComparison.OrdinalIgnoreCase)
                || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
                || value.Equals("1", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasTag(IReadOnlyDictionary<string, string> tags, string key) =>
        tags.Any(tag => tag.Key.Equals(key, StringComparison.OrdinalIgnoreCase));

    private static string? GetTag(IReadOnlyDictionary<string, string> tags, string key)
    {
        foreach (var tag in tags)
        {
            if (tag.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                return tag.Value;
        }

        return null;
    }

    private static string Normalize(string value) =>
        Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9]+", " ").Trim();

    private static bool ContainsNormalizedTerm(string normalizedValue, string pattern)
    {
        var normalizedPattern = Normalize(pattern);
        return normalizedValue.Equals(normalizedPattern, StringComparison.OrdinalIgnoreCase)
            || normalizedValue.StartsWith(normalizedPattern + " ", StringComparison.OrdinalIgnoreCase)
            || normalizedValue.EndsWith(" " + normalizedPattern, StringComparison.OrdinalIgnoreCase)
            || normalizedValue.Contains(" " + normalizedPattern + " ", StringComparison.OrdinalIgnoreCase)
            || normalizedValue.Replace(" ", "", StringComparison.OrdinalIgnoreCase)
                .Contains(normalizedPattern.Replace(" ", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase);
    }

    private sealed record ProtectedDataRule(
        string Tag,
        string Value,
        decimal Confidence,
        IReadOnlyList<string> Patterns,
        string Reason);
}
