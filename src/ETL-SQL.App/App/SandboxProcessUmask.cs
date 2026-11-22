using System;
using System.Runtime.InteropServices;

namespace ETL_SQL.App;

/// <summary>
/// Applies the file-creation mask the orchestrator hands a sandbox worker.
/// </summary>
/// <remarks>
/// A sandbox runs as an unprivileged uid unrelated to the orchestrator's. The directories it creates
/// under the tenant session mount (one per job, plus spill and checkpoint state) would otherwise be
/// 0755 and owned by that uid, so the orchestrator's account could never remove them. The session
/// root on the host is owner-only (0700), so entries the sandbox opens beneath it reach no other host
/// account. The mask is only applied when the orchestrator sets <see cref="Variable"/>; a CLI or
/// service run keeps the inherited mask.
/// </remarks>
internal static class SandboxProcessUmask
{
    public const string Variable = "ETLSQL_SANDBOX_UMASK";

    /// <summary>Applies the mask in <see cref="Variable"/> to this process on Unix.</summary>
    public static void ApplyToProcess()
    {
        if (OperatingSystem.IsWindows()) return;
        Apply(Environment.GetEnvironmentVariable, mask => umask(mask));
    }

    /// <summary>
    /// Parses <see cref="Variable"/> as an octal mode and passes it to <paramref name="setUmask"/>.
    /// Returns the mask applied, or null when the variable is unset. An invalid value fails the
    /// worker rather than running with a mask the orchestrator did not ask for.
    /// </summary>
    public static uint? Apply(Func<string, string?> getEnvironment, Func<uint, uint> setUmask)
    {
        var raw = getEnvironment(Variable);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        var text = raw.Trim();
        if (text.Length is < 1 or > 4 || !IsOctal(text))
            throw new InvalidOperationException(
                $"{Variable} must be an octal mode from 0000 to 0777; got '{raw}'.");

        var mask = Convert.ToUInt32(text, 8);
        if (mask > 0x1FF)
            throw new InvalidOperationException(
                $"{Variable} must be an octal mode from 0000 to 0777; got '{raw}'.");

        setUmask(mask);
        return mask;
    }

    private static bool IsOctal(string text)
    {
        foreach (var c in text)
            if (c is < '0' or > '7') return false;
        return true;
    }

    [DllImport("libc", EntryPoint = "umask")]
    private static extern uint umask(uint mask);
}
