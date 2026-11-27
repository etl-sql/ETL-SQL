using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Governance;

/// <summary>A catalog-approved native launch contract and the files it can start.</summary>
public sealed record NativeToolArtifact(string ExecutablePath, string Sha256, string ApprovalId)
{
    internal const string DigestOption = "ARTIFACT_SHA256";
    internal const string ScriptDigestOption = "SCRIPT_SHA256";
    private const string ScriptPathOption = "SCRIPT_PATH";

    public string? ScriptPath { get; private init; }
    public string? ScriptSha256 { get; private init; }
    private string? WindowsServiceIdentity { get; init; }
    private string? WindowsCatalogEntry { get; init; }
    private string? WindowsArtifactRoot { get; init; }
    public string GenerationSha256 => ScriptSha256 is null ? Sha256 : ComputeScriptGeneration(Sha256, ScriptSha256);

    internal static NativeToolArtifact Resolve(string? artifactRoot, string name, string type,
        IReadOnlyDictionary<string, string> options, string? serviceIdentity = null, string? catalogEntry = null)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Native artifact approval requires a restricted Linux or Windows worker.");
        if (!string.Equals(type, "EXECUTABLE", StringComparison.OrdinalIgnoreCase)
            || options.ContainsKey("ALLOW_CALLER_CODE_ARGS"))
            throw new InvalidOperationException("Artifact approval requires an executable with a fixed entry point.");

        var digest = ReadDigest(options, DigestOption);
        if (string.IsNullOrWhiteSpace(artifactRoot) || !Path.IsPathFullyQualified(artifactRoot))
            throw new InvalidOperationException("Governance:Tools:ArtifactRoot must be a fully qualified path.");
        if (!options.TryGetValue("COMMAND", out var command) || !Path.IsPathFullyQualified(command))
            throw new ArgumentException("An approved executable requires an absolute COMMAND.");

        ToolAliasValidator.Validate(name);
        var path = Path.GetFullPath(command);
        string? scriptPath = null;
        string? scriptDigest = null;
        if (options.ContainsKey(ScriptDigestOption))
        {
            if (!OperatingSystem.IsLinux())
                throw new PlatformNotSupportedException("Fixed dash script approval requires Linux.");
            scriptDigest = ReadDigest(options, ScriptDigestOption);
            if (Path.GetFileName(path) != "dash"
                || !options.TryGetValue(ScriptPathOption, out var configuredScript)
                || !Path.IsPathFullyQualified(configuredScript))
                throw new InvalidOperationException("Fixed script approval requires a dash executable and an absolute SCRIPT_PATH.");

            scriptPath = Path.GetFullPath(configuredScript);
            if (Path.GetExtension(scriptPath) != ".sh" || Path.GetDirectoryName(scriptPath) != Path.GetDirectoryName(path))
                throw new InvalidOperationException("The approved .sh script must be in the same version directory as dash.");
        }
        else if (options.ContainsKey(ScriptPathOption) || ToolPathGuard.IsInterpreterCommand(path))
        {
            throw new InvalidOperationException(
                "Artifact approval supports direct executables or a fixed dash script with SCRIPT_SHA256.");
        }

        var generation = scriptDigest is null ? digest : ComputeScriptGeneration(digest, scriptDigest);
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(artifactRoot));
        var pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var version = Path.Combine(root, name.ToLowerInvariant(), generation);
        if (root.Equals(Path.GetPathRoot(root), pathComparison)
            || !string.Equals(Path.GetDirectoryName(path), version, pathComparison))
            throw new InvalidOperationException("The executable must be under ArtifactRoot/<alias>/<generation-sha256>/.");

        if (OperatingSystem.IsWindows())
        {
            WindowsNativeToolBoundary.RequireProfile(serviceIdentity);
            root = WindowsNativeToolBoundary.RequireLocalNtfsPath(root);
            path = WindowsNativeToolBoundary.RequireLocalNtfsPath(path);
            ToolPathGuard.ValidateNativeCommandPath(path, ToolCatalogOwnershipMode.MachineAdministrator);
            catalogEntry = WindowsNativeToolBoundary.RequireLocalNtfsPath(catalogEntry
                ?? throw new InvalidOperationException("Windows native tool approval requires a protected local catalog entry."));
            if (!Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Windows native artifact approval requires a direct .exe file.");
            if (options.TryGetValue("WORKING_DIR", out var workDir) && !string.IsNullOrWhiteSpace(workDir)
                && (!Path.IsPathFullyQualified(workDir)
                    || !Path.TrimEndingDirectorySeparator(Path.GetFullPath(workDir)).Equals(version, pathComparison)))
                throw new InvalidOperationException("Approved Windows tools must use their protected version directory as the working directory.");
        }

        // The grant list and catalog revision can change independently of the reviewed contract.
        var contract = options.Where(option => !option.Key.Equals(DigestOption, StringComparison.OrdinalIgnoreCase)
                && !option.Key.Equals("COMMAND", StringComparison.OrdinalIgnoreCase)
                && !option.Key.Equals(ScriptPathOption, StringComparison.OrdinalIgnoreCase)
                && !option.Key.Equals(ScriptDigestOption, StringComparison.OrdinalIgnoreCase))
            .Select(option => new KeyValuePair<string, string>(option.Key.ToUpperInvariant(), option.Value))
            .OrderBy(option => option.Key, StringComparer.Ordinal);
        var contractBytes = scriptPath is null
            ? JsonSerializer.SerializeToUtf8Bytes(new { path, digest, contract })
            : JsonSerializer.SerializeToUtf8Bytes(new { path, digest, scriptPath, scriptDigest, launch = "dash-fixed-script-v1", contract });
        return new NativeToolArtifact(path, digest, Convert.ToHexStringLower(SHA256.HashData(contractBytes)))
        {
            ScriptPath = scriptPath,
            ScriptSha256 = scriptDigest,
            WindowsServiceIdentity = OperatingSystem.IsWindows() ? serviceIdentity : null,
            WindowsCatalogEntry = OperatingSystem.IsWindows() ? catalogEntry : null,
            WindowsArtifactRoot = OperatingSystem.IsWindows() ? root : null
        };
    }

    private static string ReadDigest(IReadOnlyDictionary<string, string> options, string key)
    {
        if (!options.TryGetValue(key, out var digest) || digest is not { Length: 64 } || !digest.All(Uri.IsHexDigit))
            throw new ArgumentException($"{key} must contain 64 hexadecimal digits.");
        return digest.ToLowerInvariant();
    }

    private static string ComputeScriptGeneration(string interpreterDigest, string scriptDigest) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.ASCII.GetBytes(interpreterDigest + "\n" + scriptDigest)));

    /// <summary>Verifies an approved artifact and retains the Windows read handle through process start.</summary>
    public async Task<FileStream?> OpenForLaunchAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsNativeToolBoundary.RequireProfile(WindowsServiceIdentity);
            WindowsNativeToolBoundary.VerifyWorker(WindowsServiceIdentity!, ExecutablePath, WindowsArtifactRoot!, WindowsCatalogEntry!);
            return await OpenVerifiedWindowsExecutableAsync(cancellationToken).ConfigureAwait(false);
        }

        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Native artifact approval requires a restricted Linux or Windows worker.");

        var status = await File.ReadAllLinesAsync("/proc/self/status", cancellationToken).ConfigureAwait(false);
        var uid = StatusValue(status, "Uid").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (uid.Length != 4 || uid.Any(value => !uint.TryParse(value, out var id) || id == 0)
            || StatusValue(status, "NoNewPrivs") != "1"
            || !HasNoCapabilities(status, "CapEff") || !HasNoCapabilities(status, "CapPrm")
            || !HasNoCapabilities(status, "CapInh") || !HasNoCapabilities(status, "CapAmb"))
            throw new UnauthorizedAccessException(
                "Approved native tools require a non-root Linux worker with no capabilities and NoNewPrivileges enabled.");

        await VerifyFilesAsync(cancellationToken).ConfigureAwait(false);
        return null;
    }

    private static string StatusValue(string[] status, string key) =>
        status.FirstOrDefault(line => line.StartsWith(key + ":", StringComparison.Ordinal))?
            [(key.Length + 1)..].Trim() ?? string.Empty;

    private static bool HasNoCapabilities(string[] status, string key) =>
        ulong.TryParse(StatusValue(status, key), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value)
        && value == 0;

    internal async Task VerifyFilesAsync(CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsNativeToolBoundary.RequireProfile(WindowsServiceIdentity);
            WindowsNativeToolBoundary.VerifyPublisherPaths(ExecutablePath, WindowsArtifactRoot!, Path.GetDirectoryName(WindowsCatalogEntry)!);
            await using var verified = await OpenVerifiedWindowsExecutableAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Native artifact verification requires Linux or Windows.");

        ToolPathGuard.ValidateNativeCommandPath(ExecutablePath, ToolCatalogOwnershipMode.MachineAdministrator,
            requireExists: true, verifyWriteBoundary: true);
        var version = Path.GetDirectoryName(ExecutablePath)!;
        for (var directory = version; directory is not null; directory = Path.GetDirectoryName(directory))
            ToolPathGuard.EnsureNotReparsePoint(directory);
        // These directories must also pass the write check when an ancestor is sticky.
        var aliasDirectory = Path.GetDirectoryName(version)!;
        foreach (var directory in new[] { version, aliasDirectory, Path.GetDirectoryName(aliasDirectory)! })
            ToolPathGuard.VerifyDirectoryBinaryWriteBoundary(directory, ToolCatalogOwnershipMode.MachineAdministrator);

        if (Directory.EnumerateFileSystemEntries(version).Any(entry => entry != ExecutablePath && entry != ScriptPath))
            throw new InvalidOperationException(ScriptPath is null
                ? "A direct executable version must contain only its approved executable."
                : "A fixed dash version must contain only its approved interpreter and script.");

        if (ScriptPath is { } scriptPath)
        {
            ToolPathGuard.ValidateNativeScriptPath(scriptPath, null, ToolCatalogOwnershipMode.MachineAdministrator,
                requireExists: true, verifyWriteBoundary: true);
            await VerifyApprovedFileAsync(scriptPath, ScriptSha256!, executable: false, cancellationToken).ConfigureAwait(false);
        }

        await VerifyApprovedFileAsync(ExecutablePath, Sha256, executable: true, cancellationToken).ConfigureAwait(false);
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private async Task<FileStream> OpenVerifiedWindowsExecutableAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var version = Path.GetDirectoryName(ExecutablePath)!;
        if (Directory.EnumerateFileSystemEntries(version)
            .Any(entry => !entry.Equals(ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A Windows executable version must contain only its approved executable.");

        var handle = new FileStream(ExecutablePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);
        try
        {
            WindowsNativeToolBoundary.VerifyExecutable(handle, ExecutablePath);
            var digest = await SHA256.HashDataAsync(handle, cancellationToken).ConfigureAwait(false);
            if (!Convert.ToHexStringLower(digest).Equals(Sha256, StringComparison.Ordinal))
                throw new InvalidOperationException("The approved Windows executable failed SHA-256 verification.");
            return handle;
        }
        catch
        {
            await handle.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    private static async Task VerifyApprovedFileAsync(string path, string digest, bool executable,
        CancellationToken cancellationToken)
    {
        var fileRole = executable ? "native executable" : "fixed script";
        // Inspect the inode type before opening the file, including rejection of FIFOs and devices.
        using var stat = new Process
        {
            StartInfo = new ProcessStartInfo("/usr/bin/stat")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                ArgumentList = { "--format=%f %h", "--", path }
            }
        };
        stat.StartInfo.Environment.Clear();
        stat.Start();
        var outputTask = stat.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = stat.StandardError.ReadToEndAsync(cancellationToken);
        await stat.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        var output = await outputTask.ConfigureAwait(false);
        await errorTask.ConfigureAwait(false);
        var fields = output.Trim().Split(' ');
        if (stat.ExitCode != 0 || fields.Length != 2 || fields[1] != "1"
            || !int.TryParse(fields[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var mode)
            || (mode & 0xf000) != 0x8000 || (mode & 0xc00) != 0 || (executable && (mode & 0x49) == 0))
            throw new InvalidOperationException($"The approved {fileRole} must be a regular file without hard links or set-ID bits.");

        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);
        if (executable)
        {
            var magic = new byte[4];
            if (await stream.ReadAtLeastAsync(magic, magic.Length, throwOnEndOfStream: false, cancellationToken)
                    .ConfigureAwait(false) != magic.Length
                || !magic.AsSpan().SequenceEqual("\u007fELF"u8))
                throw new InvalidOperationException("The approved executable must be a direct ELF binary.");
            stream.Position = 0;
        }
        else if (stream.Length == 0)
        {
            throw new InvalidOperationException("The approved fixed script is empty.");
        }

        var actual = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        if (!Convert.ToHexStringLower(actual).Equals(digest, StringComparison.Ordinal))
            throw new InvalidOperationException($"The approved {fileRole} failed SHA-256 verification.");
    }
}
