using System;
using System.IO;
using System.Linq;

namespace ETL_SQL.Core.Governance;

/// <summary>
/// First-class reference to a sandbox capability bind-mounted at the capability root.
/// </summary>
public static class CapabilityReference
{
    public const string Prefix = "CAPABILITY:";
    public const string EnvironmentVariable = "ETLSQL_CAPABILITY_ROOT";
    public const string DefaultCapabilityRoot = "/run/secrets/capabilities";

    /// <summary>
    /// Gets the configured capability root directory, checking the environment variable
    /// <see cref="EnvironmentVariable"/> and falling back to <see cref="DefaultCapabilityRoot"/>.
    /// </summary>
    public static string GetCapabilityRoot(string? capabilityRootOverride = null) =>
        capabilityRootOverride
        ?? Environment.GetEnvironmentVariable(EnvironmentVariable)
        ?? DefaultCapabilityRoot;

    /// <summary>
    /// Checks if a string is a <c>CAPABILITY:name</c> reference.
    /// </summary>
    public static bool IsCapabilityReference(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        value.TrimStart().StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts and validates the capability handle name from a reference string.
    /// </summary>
    public static string GetCapabilityName(string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        var trimmed = reference.Trim();
        if (!trimmed.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"'{reference}' is not a CAPABILITY: reference.", nameof(reference));

        var name = trimmed[Prefix.Length..].Trim().Trim('\'', '"');
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("CAPABILITY: reference has no name.", nameof(reference));

        ValidateHandle(name);
        return name;
    }

    /// <summary>
    /// Validates that a capability handle is a single plain identifier.
    /// </summary>
    public static void ValidateHandle(string capabilityHandle)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(capabilityHandle);
        if (capabilityHandle.Length > 128 ||
            capabilityHandle is "." or ".." ||
            capabilityHandle.AsSpan().IndexOfAny('/', '\\', ':') >= 0 ||
            capabilityHandle.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))
        {
            throw new ArgumentException(
                $"Capability handle '{capabilityHandle}' is not a single plain name.",
                nameof(capabilityHandle));
        }
    }

    /// <summary>
    /// Resolves a <c>CAPABILITY:name</c> reference to the path of the mounted capability file.
    /// Throws a descriptive <see cref="FileNotFoundException"/> if the capability is not mounted or the file does not exist.
    /// </summary>
    public static string ResolvePath(string reference, string? capabilityRootOverride = null)
    {
        var name = GetCapabilityName(reference);
        var root = GetCapabilityRoot(capabilityRootOverride);

        var fullPath = Path.GetFullPath(Path.Combine(root, name));
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                $"Capability '{name}' is not mounted or available in this execution environment (expected at '{fullPath}').",
                fullPath);
        }

        return fullPath;
    }

    /// <summary>
    /// Resolves a <c>CAPABILITY:name</c> reference to its string content.
    /// </summary>
    public static string ResolveContent(string reference, string? capabilityRootOverride = null)
    {
        var path = ResolvePath(reference, capabilityRootOverride);
        return File.ReadAllText(path).Trim();
    }
}
