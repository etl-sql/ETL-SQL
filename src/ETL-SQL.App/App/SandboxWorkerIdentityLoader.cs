using System;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.App;

/// <summary>Trusted CLI bootstrap for an assignment-local, read-only sandbox identity mount.</summary>
internal static class SandboxWorkerIdentityLoader
{
    public static async Task<ExecutionIdentity?> ReadAsync(string scriptPath, CancellationToken cancellationToken)
    {
        var required = Environment.GetEnvironmentVariable("ETLSQL_SANDBOX_IDENTITY_REQUIRED");
        if (required is null) return null;
        if (required != "1") throw new UnauthorizedAccessException("Invalid sandbox identity protocol requirement.");
        var file = new FileInfo(SandboxIdentityEnvelope.WorkerFile);
        if (!file.Exists || file.Length > SandboxIdentityEnvelope.MaximumBytes
            || (file.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Required sandbox execution identity is missing or invalid.");
        await using var input = File.OpenRead(file.FullName);
        var envelope = await JsonSerializer.DeserializeAsync<SandboxIdentityEnvelope>(input, cancellationToken: cancellationToken)
            ?? throw new UnauthorizedAccessException("Required sandbox execution identity is empty.");
        await using var script = File.OpenRead(scriptPath);
        var hash = "sha256:" + Convert.ToHexString(await SHA256.HashDataAsync(script, cancellationToken)).ToLowerInvariant();
        return envelope.Validate(
            Environment.GetEnvironmentVariable("ETLSQL_SANDBOX_TENANT") ?? "",
            Environment.GetEnvironmentVariable("ETLSQL_SANDBOX_RUN") ?? "",
            Environment.GetEnvironmentVariable("ETLSQL_SANDBOX_ATTEMPT") ?? "", hash);
    }
}
