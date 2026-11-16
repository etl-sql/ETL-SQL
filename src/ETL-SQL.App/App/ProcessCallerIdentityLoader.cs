using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.App;

/// <summary>
/// Child-side bootstrap for signed caller identity envelopes written by <c>ProcessJobExecutor</c>.
/// Reads once, deletes the temp file before execution, and rejects missing, expired, altered,
/// mismatched, or replayed envelopes.
/// </summary>
internal static class ProcessCallerIdentityLoader
{
    public static async Task<ExecutionIdentity?> ReadAndConsumeAsync(
        string scriptPath,
        string? envelopeFileOverride,
        string? expectedJobId,
        string? signingSecret,
        CancellationToken cancellationToken = default)
    {
        var required = Environment.GetEnvironmentVariable(ProcessCallerIdentityEnvelope.EnvRequired);
        var envFile = Environment.GetEnvironmentVariable(ProcessCallerIdentityEnvelope.EnvFile);
        var targetFile = !string.IsNullOrWhiteSpace(envelopeFileOverride)
            ? envelopeFileOverride.Trim()
            : envFile;

        if (string.IsNullOrWhiteSpace(required) && string.IsNullOrWhiteSpace(targetFile))
            return null;

        if (required is not null && required != "1")
            throw new UnauthorizedAccessException("Invalid caller identity protocol requirement.");

        if (string.IsNullOrWhiteSpace(targetFile))
            throw new UnauthorizedAccessException("Required caller identity envelope path is missing.");

        var file = new FileInfo(targetFile);
        if (!file.Exists
            || file.Length == 0
            || file.Length > ProcessCallerIdentityEnvelope.MaximumBytes
            || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new UnauthorizedAccessException("Required caller identity envelope is missing or invalid.");
        }

        string signedEnvelope;
        try
        {
            signedEnvelope = (await File.ReadAllTextAsync(file.FullName, cancellationToken).ConfigureAwait(false)).Trim();
        }
        finally
        {
            try
            {
                if ((file.Attributes & FileAttributes.ReparsePoint) == 0 && File.Exists(file.FullName))
                    File.Delete(file.FullName);
            }
            catch
            {
                // Best effort; nonces are also tracked in memory for the lifetime of the child process.
            }
        }

        var scriptHash = await ProcessCallerIdentityEnvelope.ComputeScriptHashAsync(scriptPath, cancellationToken)
            .ConfigureAwait(false);
        return ProcessCallerIdentityEnvelope.ValidateAndConsume(
            signedEnvelope,
            signingSecret ?? string.Empty,
            scriptHash,
            expectedJobId);
    }
}
