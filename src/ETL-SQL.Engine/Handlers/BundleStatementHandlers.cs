using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using ETL_SQL.Analysis.Lineage;
using ETL_SQL.Common;
using ETL_SQL.Core;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Core.Data;
using ETL_SQL.Core.Parser;
using ETL_SQL.Data;
using ETL_SQL.Engine.Services;
using ETL_SQL.Services;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Engine.Handlers;
public sealed record BundlePreflightResult(
    string EntryPath,
    IReadOnlyList<BundlePublishFile> Files,
    IReadOnlyList<BundleDependencyInfo> Dependencies,
    IReadOnlyList<LineageEntry> LineageEntries,
    string ContentHash,
    int RemovedPasswordStatements);

public static partial class BundlePublishSupport
{
    [GeneratedRegex(@"(?im)^\s*USE\s+PASSWORD\s+(?:=\s*'([^']|'')*'|PROMPT)\s*;\s*\r?\n?")]
    private static partial Regex UsePasswordRegex();

    [GeneratedRegex(@"ENC:[A-Za-z0-9+/=]+")]
    private static partial Regex EncRegex();

    public static async Task<BundlePreflightResult> PreflightAsync(
        string bundleName,
        string sourcePath,
        string entryPath,
        string? publishPassword,
        string encryptionPassword,
        bool rewriteSecrets = true)
    {
        var resolvedSource = Path.GetFullPath(sourcePath);
        var root = Directory.Exists(resolvedSource)
            ? resolvedSource
            : Path.GetDirectoryName(resolvedSource) ?? Directory.GetCurrentDirectory();
        root = Path.GetFullPath(root);

        var normalizedEntry = BundleUri.NormalizePath(entryPath);
        var entryFullPath = Directory.Exists(resolvedSource)
            ? Path.GetFullPath(Path.Combine(root, normalizedEntry))
            : resolvedSource;

        if (!IsWithinRoot(root, entryFullPath))
            throw new ExecutionException($"Bundle publish failed: entry script escapes bundle root: {entryPath}");

        if (!File.Exists(entryFullPath))
            throw new ExecutionException($"Bundle publish failed: entry script not found: {entryFullPath}");

        var isDirectoryPublish = Directory.Exists(resolvedSource);
        var visited = new Dictionary<string, BundlePublishFile>(StringComparer.OrdinalIgnoreCase);
        var dependencies = new List<BundleDependencyInfo>();
        var lineageEntries = new List<LineageEntry>();
        var removedPasswords = 0;
        await VisitAsync(bundleName, 0, root, entryFullPath, publishPassword, encryptionPassword, rewriteSecrets, visited, dependencies, lineageEntries, () => removedPasswords++);
        if (isDirectoryPublish)
        {
            foreach (var scriptPath in EnumerateScriptFiles(root))
                await VisitAsync(bundleName, 0, root, scriptPath, publishPassword, encryptionPassword, rewriteSecrets, visited, dependencies, lineageEntries, () => removedPasswords++);
        }

        var ordered = visited.Values.OrderBy(f => f.VirtualPath, StringComparer.OrdinalIgnoreCase).ToList();
        var contentHash = HashText(string.Join("\n", ordered.Select(f => $"{f.VirtualPath}:{f.ContentHash}")));
        return new BundlePreflightResult(GetVirtualPath(root, entryFullPath), ordered, dependencies, lineageEntries, contentHash, removedPasswords);
    }

    public static BundlePublishRequest ReEncryptRequest(
        BundlePublishRequest request,
        string? publishPassword,
        string encryptionPassword)
    {
        var removedPasswords = 0;
        var files = request.Files.Select(file =>
        {
            var filePassword = publishPassword ?? ExtractLiteralUsePassword(file.Content) ?? SecurityService.GetMachineKey();
            var content = ReEncryptAndStripPasswords(file.Content, filePassword, encryptionPassword, () => removedPasswords++);
            return new BundlePublishFile(
                file.VirtualPath,
                content,
                HashText(content),
                Encoding.UTF8.GetByteCount(content),
                file.ContentType);
        }).OrderBy(f => f.VirtualPath, StringComparer.OrdinalIgnoreCase).ToList();

        var contentHash = HashText(string.Join("\n", files.Select(f => $"{f.VirtualPath}:{f.ContentHash}")));
        return request with { Files = files, ContentHash = contentHash, EncryptionMode = request.EncryptionMode.ToUpperInvariant() };
    }

    private static async Task VisitAsync(
        string bundleName,
        int version,
        string root,
        string fullPath,
        string? publishPassword,
        string encryptionPassword,
        bool rewriteSecrets,
        Dictionary<string, BundlePublishFile> visited,
        List<BundleDependencyInfo> dependencies,
        List<LineageEntry> lineageEntries,
        Action passwordRemoved)
    {
        var virtualPath = GetVirtualPath(root, fullPath);
        if (visited.ContainsKey(virtualPath)) return;

        var source = await File.ReadAllTextAsync(fullPath);
        var tokens = new Lexer(source).Tokenize();
        var script = new Parser(tokens, source).Parse();
        lineageEntries.AddRange(AnalyzeLineage(script, bundleName, virtualPath));

        var filePassword = publishPassword ?? ExtractLiteralUsePassword(script) ?? SecurityService.GetMachineKey();
        var sanitized = rewriteSecrets
            ? ReEncryptAndStripPasswords(source, filePassword, encryptionPassword, passwordRemoved)
            : source;
        visited[virtualPath] = new BundlePublishFile(
            virtualPath,
            sanitized,
            HashText(sanitized),
            Encoding.UTF8.GetByteCount(sanitized),
            GuessContentType(fullPath));

        foreach (var run in FindRunScripts(script))
        {
            if (run.PathExpression is not LiteralExpression lit || lit.Value is not string childRaw)
            {
                throw new ExecutionException(
                    "Bundle publish failed: dynamic RUN SCRIPT paths cannot be packaged.\n\n" +
                    $"Found:\n  {virtualPath}:{run.Line}  {run.ToSql()}\n\n" +
                    "Published bundles require literal script paths so dependencies can be versioned and stored safely.\n" +
                    "Use live mode instead:\n  CREATE JOB JobName ON SCHEDULE EVERY 1 DAY AS RUN SCRIPT 'C:\\ETL\\main.etlsql';");
            }

            if (BundleUri.TryParse(childRaw, out _))
                continue;

            var childFullPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fullPath) ?? root, childRaw));
            if (!IsWithinRoot(root, childFullPath))
                throw new ExecutionException($"Bundle publish failed: RUN SCRIPT dependency escapes bundle root: {childRaw}");
            if (!File.Exists(childFullPath))
                throw new ExecutionException($"Bundle publish failed: RUN SCRIPT dependency not found: {childRaw}");

            var childVirtual = GetVirtualPath(root, childFullPath);
            dependencies.Add(new BundleDependencyInfo(bundleName, version, virtualPath, childVirtual));
            await VisitAsync(bundleName, version, root, childFullPath, publishPassword, encryptionPassword, rewriteSecrets, visited, dependencies, lineageEntries, passwordRemoved);
        }
    }

    private static IReadOnlyList<LineageEntry> AnalyzeLineage(Script script, string bundleName, string virtualPath)
    {
        var tracker = new LineageTracker(NullLogger.Instance);
        tracker.GlobalMetadata["bundle"] = bundleName;
        tracker.GlobalMetadata["bundle_path"] = virtualPath;
        new LineageAnalyzer(tracker).Analyze(script);

        return tracker.GetFullLineage()
            .Select(entry =>
            {
                entry.SourceFile = virtualPath;
                entry.Metadata["bundle"] = bundleName;
                entry.Metadata["bundle_path"] = virtualPath;
                return entry;
            })
            .ToList();
    }

    private static string ReEncryptAndStripPasswords(string source, string decryptPassword, string encryptPassword, Action passwordRemoved)
    {
        var rewritten = EncRegex().Replace(source, m =>
        {
            try
            {
                var decrypted = CryptoUtils.Decrypt(m.Value, decryptPassword);
                return CryptoUtils.Encrypt(decrypted, encryptPassword);
            }
            catch
            {
                return m.Value;
            }
        });

        return UsePasswordRegex().Replace(rewritten, _ =>
        {
            passwordRemoved();
            return "";
        });
    }

    private static string? ExtractLiteralUsePassword(Script script)
        => Flatten(script.Statements).OfType<UsePasswordStatement>().FirstOrDefault(s => !s.Prompt)?.Password;

    private static string? ExtractLiteralUsePassword(string source)
    {
        var script = new Parser(new Lexer(source).Tokenize(), source).Parse();
        return ExtractLiteralUsePassword(script);
    }

    private static IEnumerable<RunScriptStatement> FindRunScripts(Script script)
        => Flatten(script.Statements).OfType<RunScriptStatement>();

    private static IEnumerable<Statement> Flatten(IEnumerable<Statement> statements)
    {
        foreach (var stmt in statements)
        {
            yield return stmt;
            switch (stmt)
            {
                case BlockStatement b:
                    foreach (var s in Flatten(b.Statements)) yield return s;
                    break;
                case IfStatement i:
                    foreach (var s in Flatten(new[] { i.IfBody })) yield return s;
                    if (i.ElseBody != null)
                        foreach (var s in Flatten(new[] { i.ElseBody })) yield return s;
                    foreach (var e in i.ElseIfClauses ?? Enumerable.Empty<ElseIfClause>())
                        foreach (var s in Flatten(new[] { e.Body })) yield return s;
                    break;
                case TryCatchStatement tc:
                    foreach (var s in Flatten(new[] { tc.TryBody })) yield return s;
                    foreach (var s in Flatten(new[] { tc.CatchBody })) yield return s;
                    break;
                case WhileStatement w:
                    foreach (var s in Flatten(new[] { w.Body })) yield return s;
                    break;
                case ForStatement f:
                    foreach (var s in Flatten(new[] { f.Body })) yield return s;
                    break;
                case ForeachStatement fe:
                    foreach (var s in Flatten(new[] { fe.Body })) yield return s;
                    break;
                case ParallelStatement p:
                    foreach (var s in Flatten(p.Body.Statements)) yield return s;
                    break;
            }
        }
    }

    private static string GetVirtualPath(string root, string fullPath)
    {
        var rel = Path.GetRelativePath(root, fullPath);
        return BundleUri.NormalizePath(rel);
    }

    private static bool IsWithinRoot(string root, string fullPath)
        => SafePath.IsWithinRoot(root, fullPath);

    private static string GuessContentType(string path)
        => Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".etlsql" => "application/etlsql",
            ".rptsql" => "application/rptsql",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".csv" => "text/csv",
            ".yaml" or ".yml" => "application/yaml",
            _ => "text/plain"
        };

    private static IEnumerable<string> EnumerateScriptFiles(string root)
        => Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(path =>
            {
                var ext = Path.GetExtension(path);
                return ext.Equals(".etlsql", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".rptsql", StringComparison.OrdinalIgnoreCase);
            })
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);

    private static string HashText(string text)
        => "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}

public class PublishBundleStatementHandler(IBundleStore store) : IStatementHandler
{
    public Type SupportedStatementType => typeof(PublishBundleStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (PublishBundleStatement)statement;
        var source = (await context.EvaluateValue(stmt.SourcePath, new Row()))?.ToString()
            ?? throw new ExecutionException("PUBLISH BUNDLE source path evaluated to null.");

        var publishPassword = stmt.PasswordMode == BundleSecretMode.Prompt
            ? PasswordPromptResolver.Get(context).ReadPassword($"Publish password for bundle '{stmt.BundleName}': ")
            : stmt.Password;

        var encryptionPassword = SecurityService.GetMachineKey();
        if (stmt.EncryptionMode.Equals("KEYFILE", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(stmt.KeyFile))
                throw new ExecutionException("PUBLISH BUNDLE ENCRYPT = KEYFILE requires KEYFILE = 'path'.");
            var keyPath = context.ResolvePath(stmt.KeyFile);
            encryptionPassword = File.Exists(keyPath)
                ? await File.ReadAllTextAsync(keyPath)
                : throw new ExecutionException($"PUBLISH BUNDLE keyfile not found: {keyPath}");
        }
        else if (!stmt.EncryptionMode.Equals("MACHINE", StringComparison.OrdinalIgnoreCase))
        {
            throw new ExecutionException($"Unsupported PUBLISH BUNDLE encryption mode: {stmt.EncryptionMode}");
        }

        var preflight = await BundlePublishSupport.PreflightAsync(stmt.BundleName, context.ResolvePath(source),
            stmt.EntryPath, publishPassword, encryptionPassword);
        var version = await store.PublishBundleAsync(new BundlePublishRequest(
            stmt.BundleName,
            preflight.EntryPath,
            preflight.Files,
            preflight.Dependencies,
            preflight.ContentHash,
            stmt.EncryptionMode.ToUpperInvariant(),
            stmt.EncryptionMode.Equals("KEYFILE", StringComparison.OrdinalIgnoreCase) ? stmt.KeyFile : null,
            Environment.UserName,
            stmt.Description));

        context.Log($"Published bundle '{stmt.BundleName}' version {version.Version} with {preflight.Files.Count} file(s).", ConsoleColor.Green);
        if (preflight.RemovedPasswordStatements > 0)
            context.Log($"Removed {preflight.RemovedPasswordStatements} USE PASSWORD statement(s) from the published copy. Source files were not modified.", ConsoleColor.Yellow);
    }
}

public class ValidateBundleStatementHandler : IStatementHandler
{
    public Type SupportedStatementType => typeof(ValidateBundleStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ValidateBundleStatement)statement;
        var publishPassword = stmt.PasswordMode == BundleSecretMode.Prompt
            ? PasswordPromptResolver.Get(context).ReadPassword($"Publish password for bundle '{stmt.BundleName}': ")
            : stmt.Password;
        var source = (await context.EvaluateValue(stmt.SourcePath, new Row()))?.ToString()
            ?? throw new ExecutionException("VALIDATE BUNDLE source path evaluated to null.");
        var result = await BundlePublishSupport.PreflightAsync(stmt.BundleName, context.ResolvePath(source),
            stmt.EntryPath, publishPassword, SecurityService.GetMachineKey());
        context.Log($"Bundle '{stmt.BundleName}' validated: {result.Files.Count} file(s), {result.Dependencies.Count} dependency edge(s).", ConsoleColor.Green);
    }

}

internal static class PasswordPromptResolver
{
    public static IPasswordPromptProvider Get(IExecutionContext context)
        => context.ServiceProvider.GetService<IPasswordPromptProvider>() ?? ConsolePasswordPromptProvider.Instance;
}

public class ExportScriptStatementHandler(IBundleStore store) : IStatementHandler
{
    public Type SupportedStatementType => typeof(ExportScriptStatement);

    public async Task Execute(Statement statement, IExecutionContext context)
    {
        var stmt = (ExportScriptStatement)statement;
        var source = (await context.EvaluateValue(stmt.SourcePath, new Row()))?.ToString()
            ?? throw new ExecutionException("EXPORT SCRIPT source evaluated to null.");
        var target = (await context.EvaluateValue(stmt.TargetPath, new Row()))?.ToString()
            ?? throw new ExecutionException("EXPORT SCRIPT target evaluated to null.");

        if (!BundleUri.TryParse(source, out var uri) || uri == null)
            throw new ExecutionException("EXPORT SCRIPT source must be an orch://bundle@version/path.etlsql path.");
        var version = uri.Version ?? (await store.GetLatestVersionAsync(uri.BundleName))?.Version
            ?? throw new ExecutionException($"Bundle '{uri.BundleName}' was not found.");

        var files = (await store.GetFilesAsync(uri.BundleName, version)).ToList();
        if (files.Count == 0)
            throw new ExecutionException($"Bundle '{uri.BundleName}' version {version} has no files.");

        var targetDir = context.ResolvePath(target);
        Directory.CreateDirectory(targetDir);
        foreach (var file in files)
        {
            var outPath = Path.GetFullPath(Path.Combine(targetDir, file.VirtualPath.Replace('/', Path.DirectorySeparatorChar)));
            if (!SafePath.IsWithinRoot(targetDir, outPath))
                throw new ExecutionException($"EXPORT SCRIPT refused path outside target directory: {file.VirtualPath}");
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            await File.WriteAllTextAsync(outPath, file.Content);
        }

        context.Log($"Exported bundle '{uri.BundleName}' version {version} to {targetDir}. Re-enter any secrets before running recovered scripts.", ConsoleColor.Green);
    }
}
