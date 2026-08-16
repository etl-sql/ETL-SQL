using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using ETL_SQL.Common;
using ETL_SQL.Core;

namespace ETL_SQL.App;

/// <summary>
/// Builds the versioned, read-only inventory used before a deployment-profile promotion. The
/// inventory deliberately records protected material by kind and relative path only; it never
/// serializes, hashes, or logs secret-bearing content.
/// </summary>
internal static partial class DeploymentPromotionPreflightService
{
    internal const string SchemaVersion = "etl-sql.deployment-preflight/v1";
    private const int MaxFiles = 20_000;
    private const int MaxDepth = 20;

    private static readonly HashSet<string> Profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        "Solo", "Team", "Enterprise", "SaaS"
    };

    private static readonly HashSet<string> PortableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".etlsql", ".rptsql"
    };

    private static readonly HashSet<string> ProtectedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pem", ".key", ".pfx", ".p12", ".jks", ".kdbx"
    };

    private static readonly HashSet<string> EphemeralDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".vs", "bin", "obj", "node_modules", ".cache", ".etlsnap", "TestResults"
    };

    internal sealed record InventoryEntry(string Path, string Kind, long? SizeBytes = null, string? Sha256 = null);
    internal sealed record BindingRequirement(string Kind, string Name, IReadOnlyList<string> ReferencedBy);
    internal sealed record InventoryFinding(string Code, string Severity, string Path, string Message);
    internal sealed record PreflightInventory(
        string SchemaVersion,
        DateTimeOffset GeneratedUtc,
        string SourceProfile,
        string TargetProfile,
        string SourceRoot,
        bool MutationPerformed,
        IReadOnlyList<InventoryEntry> PortableArtifacts,
        IReadOnlyList<InventoryEntry> ExportableCatalogState,
        IReadOnlyList<BindingRequirement> TargetBindings,
        IReadOnlyList<InventoryEntry> ProtectedMaterial,
        IReadOnlyList<InventoryEntry> OperationalEvidence,
        IReadOnlyList<InventoryEntry> EphemeralState,
        IReadOnlyList<InventoryFinding> Findings)
    {
        public bool Ready => Findings.All(f => !string.Equals(f.Severity, "Error", StringComparison.OrdinalIgnoreCase));
    }

    internal static async Task<int> RunAsync(CliContext ctx, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            var root = Path.GetFullPath(ctx.PromotionSource ?? Directory.GetCurrentDirectory());
            var output = Path.GetFullPath(ctx.PromotionOutput ?? Path.Combine(Directory.GetCurrentDirectory(), "deployment-preflight.json"));
            var inventory = await BuildAsync(
                root, ctx.PromotionFromProfile, ctx.PromotionToProfile, ct, await ReadUnownedObjectsAsync(ct));
            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            await using var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await JsonSerializer.SerializeAsync(stream, inventory, JsonOptions, ct);
            await stream.FlushAsync(ct);
            logger.WriteLine($"Promotion preflight inventory: {output}", ConsoleColor.Cyan);
            logger.WriteLine(
                inventory.Ready
                    ? $"READY — {inventory.PortableArtifacts.Count} portable artifact(s), {inventory.TargetBindings.Count} target binding(s), no mutation performed."
                    : $"NOT READY — {inventory.Findings.Count(f => f.Severity == "Error")} blocking finding(s), no mutation performed.",
                inventory.Ready ? ConsoleColor.Green : ConsoleColor.Red);
            return inventory.Ready ? 0 : 1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            logger.WriteLine($"Promotion preflight failed: {ex.Message}", ConsoleColor.Red);
            return 1;
        }
    }

    /// <summary>
    /// One orchestrator object nobody is accountable for, as the preflight sees it. Supplied by the
    /// caller rather than read here, so the inventory stays a pure function of what it is given.
    /// </summary>
    internal sealed record UnownedObject(string Kind, string Name);

    /// <summary>
    /// Reads the local orchestrator catalog's unowned objects, or nothing when there is no catalog.
    ///
    /// <para>The preflight is a read-only inventory of a machine that may not be running anything, so
    /// an unreachable or absent store is an ordinary state rather than a failure — it means there is
    /// no orchestrator work here to be accountable for. A promotion is not blocked by the absence of
    /// the thing being checked.</para>
    /// </summary>
    private static async Task<IReadOnlyList<UnownedObject>> ReadUnownedObjectsAsync(CancellationToken ct)
    {
        try
        {
            if (Program.ServiceProvider?.GetService(typeof(ETL_SQL.Core.Data.IOrchestratorAuthorizationStore))
                is not ETL_SQL.Core.Data.IOrchestratorAuthorizationStore store) return [];
            // The local catalog is unbound: a machine being promoted has no signed tenant yet, which
            // is the same scope its own scheduler and CLI write under.
            var unowned = await store.GetUnownedObjectsAsync(null, ct);
            return [.. unowned.Select(entry => new UnownedObject(
                entry.ObjectKind.ToString().ToUpperInvariant(), entry.Name))];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return [];
        }
    }

    internal static async Task<PreflightInventory> BuildAsync(
        string sourceRoot,
        string? sourceProfile,
        string? targetProfile,
        CancellationToken ct = default,
        IReadOnlyList<UnownedObject>? unownedObjects = null)
    {
        var root = Path.GetFullPath(sourceRoot);
        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException($"Promotion source '{root}' does not exist.");
        if (Path.GetPathRoot(root)?.TrimEnd(Path.DirectorySeparatorChar) == root.TrimEnd(Path.DirectorySeparatorChar))
            throw new ArgumentException("A filesystem root cannot be used as a promotion source.", nameof(sourceRoot));

        var from = NormalizeProfile(sourceProfile, "source");
        var to = NormalizeProfile(targetProfile, "target");
        var findings = new List<InventoryFinding>();
        if (ProfileRank(to) < ProfileRank(from))
            findings.Add(new("DP001", "Error", ".", $"Promotion cannot move backward from {from} to {to}; use an explicit export/restore workflow."));

        var portable = new List<InventoryEntry>();
        var catalogs = new List<InventoryEntry>();
        var protectedMaterial = new List<InventoryEntry>();
        var evidence = new List<InventoryEntry>();
        var ephemeral = new List<InventoryEntry>();
        var bindings = new Dictionary<(string Kind, string Name), HashSet<string>>();
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        var visitedFiles = 0;

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (directory, depth) = pending.Pop();
            IEnumerable<string> childDirectories;
            IEnumerable<string> files;
            try
            {
                childDirectories = Directory.EnumerateDirectories(directory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
                files = Directory.EnumerateFiles(directory).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            catch (UnauthorizedAccessException)
            {
                findings.Add(new("DP002", "Error", Relative(root, directory), "Directory could not be inventoried."));
                continue;
            }

            foreach (var child in childDirectories)
            {
                var info = new DirectoryInfo(child);
                var relative = Relative(root, child);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    findings.Add(new("DP003", "Error", relative, "Reparse points are not followed during promotion inventory."));
                    continue;
                }
                if (EphemeralDirectories.Contains(info.Name))
                {
                    ephemeral.Add(new(relative, "Directory"));
                    continue;
                }
                if (depth + 1 > MaxDepth)
                {
                    findings.Add(new("DP004", "Error", relative, $"Inventory depth exceeds the supported limit of {MaxDepth}."));
                    continue;
                }
                pending.Push((child, depth + 1));
            }

            foreach (var file in files)
            {
                ct.ThrowIfCancellationRequested();
                if (++visitedFiles > MaxFiles)
                {
                    findings.Add(new("DP005", "Error", ".", $"Inventory exceeds the supported limit of {MaxFiles} files."));
                    pending.Clear();
                    break;
                }

                var info = new FileInfo(file);
                var relative = Relative(root, file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    findings.Add(new("DP003", "Error", relative, "Reparse-point files are not inventoried."));
                    continue;
                }
                if (IsProtected(info))
                {
                    protectedMaterial.Add(new(relative, ProtectedKind(info)));
                    continue;
                }
                if (IsOperationalEvidence(relative, info))
                {
                    evidence.Add(new(relative, "Evidence", info.Length, await HashAsync(file, ct)));
                    continue;
                }
                if (PortableExtensions.Contains(info.Extension))
                {
                    portable.Add(new(relative, info.Extension.Equals(".rptsql", StringComparison.OrdinalIgnoreCase) ? "Report" : "Pipeline", info.Length, await HashAsync(file, ct)));
                    await DiscoverBindingsAsync(file, relative, bindings, findings, ct);
                    continue;
                }
                if (IsPortablePolicy(info))
                {
                    portable.Add(new(relative, "Policy", info.Length, await HashAsync(file, ct)));
                    continue;
                }
                if (info.Extension.Equals(".db", StringComparison.OrdinalIgnoreCase)
                    || info.Extension.Equals(".sqlite", StringComparison.OrdinalIgnoreCase))
                {
                    catalogs.Add(new(relative, "CatalogDatabase", info.Length));
                }
            }
        }

        var requiredBindings = bindings
            .OrderBy(pair => pair.Key.Kind, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Name, StringComparer.OrdinalIgnoreCase)
            .Select(pair => new BindingRequirement(pair.Key.Kind, pair.Key.Name, pair.Value.OrderBy(v => v, StringComparer.OrdinalIgnoreCase).ToArray()))
            .ToArray();

        if (portable.Count == 0)
            findings.Add(new("DP006", "Warning", ".", "No portable .etlsql, .rptsql, or policy artifacts were found."));
        if (protectedMaterial.Count > 0)
            findings.Add(new("DP007", "Warning", ".", $"{protectedMaterial.Count} protected item(s) require out-of-band target provisioning; their contents and hashes were not collected."));
        AddUnownedObjectFinding(findings, to, unownedObjects);

        return new(
            SchemaVersion, DateTimeOffset.UtcNow, from, to, root, false,
            portable.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            catalogs.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            requiredBindings,
            protectedMaterial.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            evidence.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            ephemeral.OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).ToArray(),
            findings.OrderBy(f => f.Code, StringComparer.Ordinal).ThenBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    /// <summary>
    /// Reports orchestrator objects that nobody owns, before a promotion makes that matter.
    ///
    /// <para>On Solo there are no principals and ownership decides nothing, so this is silent. From
    /// Team upward an owner is what lets anyone but an administrator reach an object — so a promotion
    /// that carried unowned jobs across would hand the new deployment work that only an administrator
    /// can run, and the symptom arrives later as "the schedule fires but nobody can touch it".</para>
    ///
    /// <para>One finding rather than one per object: the remedy is a single bulk adoption, so three
    /// hundred blocking findings would describe one decision three hundred times. Enough names are
    /// listed to recognise the estate, and the count is exact.</para>
    /// </summary>
    private static void AddUnownedObjectFinding(
        List<InventoryFinding> findings, string targetProfile, IReadOnlyList<UnownedObject>? unownedObjects)
    {
        if (unownedObjects is not { Count: > 0 }) return;
        if (string.Equals(targetProfile, "Solo", StringComparison.OrdinalIgnoreCase)) return;

        const int NamedLimit = 10;
        var named = unownedObjects
            .Take(NamedLimit)
            .Select(entry => $"{entry.Kind} {entry.Name}")
            .ToArray();
        var remainder = unownedObjects.Count - named.Length;
        findings.Add(new("DP009", "Error", ".",
            $"{unownedObjects.Count} orchestrator object(s) have no recorded owner and would be reachable " +
            $"only by an administrator on {targetProfile}: {string.Join(", ", named)}" +
            (remainder > 0 ? $", and {remainder} more" : "") +
            ". Assign owners with 'etl-sql admin orchestrator adopt' before promoting."));
    }

    private static async Task DiscoverBindingsAsync(
        string file,
        string relative,
        Dictionary<(string Kind, string Name), HashSet<string>> bindings,
        List<InventoryFinding> findings,
        CancellationToken ct)
    {
        var text = await File.ReadAllTextAsync(file, ct);
        foreach (Match match in SecretReferenceRegex().Matches(text))
            AddBinding(bindings, "Secret", match.Groups[1].Value, relative);
        foreach (Match match in SharedConnectionRegex().Matches(text))
            AddBinding(bindings, "Connection", match.Groups[1].Value, relative);
        if (RawCredentialRegex().IsMatch(text))
            findings.Add(new("DP008", "Error", relative, "A credential-like option contains a raw value; replace it with SECRET:name before export."));
    }

    private static void AddBinding(Dictionary<(string Kind, string Name), HashSet<string>> bindings, string kind, string name, string path)
    {
        var key = (kind, name);
        if (!bindings.TryGetValue(key, out var paths))
            bindings[key] = paths = new(StringComparer.OrdinalIgnoreCase);
        paths.Add(path);
    }

    private static string NormalizeProfile(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || !Profiles.Contains(value))
            throw new ArgumentException($"The {label} profile must be one of: Solo, Team, Enterprise, SaaS.");
        return Profiles.Single(p => p.Equals(value, StringComparison.OrdinalIgnoreCase));
    }

    private static int ProfileRank(string profile) => profile switch
    {
        "Solo" => 0,
        "Team" => 1,
        "Enterprise" => 2,
        "SaaS" => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(profile))
    };

    private static bool IsProtected(FileInfo info) =>
        ProtectedExtensions.Contains(info.Extension)
        || info.Name.Equals(".env", StringComparison.OrdinalIgnoreCase)
        || info.Name.StartsWith(".env.", StringComparison.OrdinalIgnoreCase)
        || info.Name.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || info.Name.Equals("appsettings.Production.json", StringComparison.OrdinalIgnoreCase);

    private static string ProtectedKind(FileInfo info) => ProtectedExtensions.Contains(info.Extension) ? "KeyOrCertificate" : "SecretConfiguration";
    private static bool IsPortablePolicy(FileInfo info) =>
        info.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
        && (info.Name.Equals("etlsql-policy.json", StringComparison.OrdinalIgnoreCase)
            || info.Name.EndsWith(".policy.json", StringComparison.OrdinalIgnoreCase));
    private static bool IsOperationalEvidence(string relative, FileInfo info) =>
        relative.Replace('\\', '/').Contains("artifacts/release-evidence/", StringComparison.OrdinalIgnoreCase)
        || info.Extension.Equals(".trx", StringComparison.OrdinalIgnoreCase);
    private static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

    private static async Task<string> HashAsync(string path, CancellationToken ct)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hash);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    [GeneratedRegex(@"\bSECRET:([A-Za-z0-9._-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretReferenceRegex();
    [GeneratedRegex(@"\bSHARED:([A-Za-z0-9._-]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SharedConnectionRegex();
    [GeneratedRegex(@"\b(?:PASSWORD|API_KEY|TOKEN|CLIENT_SECRET|PRIVATE_KEY)\s*=\s*'(?!SECRET:|ENC:)[^']+'", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RawCredentialRegex();
}
