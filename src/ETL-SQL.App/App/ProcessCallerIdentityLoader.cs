using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Governance;

namespace ETL_SQL.App;

/// <summary>
/// Child-side bootstrap for signed caller identity envelopes written by <c>ProcessJobExecutor</c>.
/// Reads once, deletes the temp file before execution, and rejects missing, expired, altered,
/// mismatched, or replayed envelopes. The envelope is verified with the per-run key the parent
/// minted for this run only; the child never holds the Orchestrator's identity-signing secret.
/// </summary>
internal static class ProcessCallerIdentityLoader
{
    /// <summary>
    /// Reads the one-shot per-run envelope key from the environment and removes it from this
    /// process, so script text that runs afterwards cannot read it through <c>ENV()</c>.
    /// </summary>
    public static string? TakeKeyFromEnvironment()
    {
        var key = Environment.GetEnvironmentVariable(ProcessCallerIdentityEnvelope.EnvKey);
        if (key is not null)
            Environment.SetEnvironmentVariable(ProcessCallerIdentityEnvelope.EnvKey, null);
        return key;
    }

    public static async Task<ExecutionIdentity?> ReadAndConsumeAsync(
        string scriptPath,
        string? envelopeFileOverride,
        string? expectedJobId,
        string? identityKey,
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
            identityKey ?? string.Empty,
            scriptHash,
            expectedJobId);
    }
}
