using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ETL_SQL.Core.Common;

namespace ETL_SQL.Orchestrator.Execution;

public sealed record SandboxCommandResult(int ExitCode, string StandardOutput, string StandardError);

public interface ISandboxCommandRunner
{
    Task<SandboxCommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default);
}

public sealed class ProcessSandboxCommandRunner : ISandboxCommandRunner
{
    private const int MaxCapturedCharacters = 2_000_000;

    public async Task<SandboxCommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);
        var start = new ProcessStartInfo
        {
            FileName = executable,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = start };
        if (!process.Start()) throw new InvalidOperationException("The sandbox runtime command did not start.");
        var stdoutTask = ReadBoundedAsync(process.StandardOutput, cancellationToken);
        var stderrTask = ReadBoundedAsync(process.StandardError, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return new SandboxCommandResult(process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[8192];
        var head = new StringBuilder();
        var tail = new StringBuilder();
        const int halfMax = MaxCapturedCharacters / 2;
        long totalRead = 0;

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0) break;
            totalRead += read;

            if (head.Length < halfMax)
            {
                var takeHead = Math.Min(read, halfMax - head.Length);
                head.Append(buffer, 0, takeHead);
                if (takeHead < read)
                {
                    var rem = read - takeHead;
                    tail.Append(buffer, takeHead, rem);
                    if (tail.Length > halfMax)
                    {
                        tail.Remove(0, tail.Length - halfMax);
                    }
                }
            }
            else
            {
                tail.Append(buffer, 0, read);
                if (tail.Length > halfMax)
                {
                    tail.Remove(0, tail.Length - halfMax);
                }
            }
        }

        if (totalRead <= MaxCapturedCharacters)
        {
            return head.ToString() + tail.ToString();
        }

        return head.ToString() + "\n... [truncated] ...\n" + tail.ToString();
    }
}

public enum DockerSandboxMode
{
    Hardened,
    Standard
}

public sealed record DockerSandboxExecutionOptions
{
    public DockerSandboxMode Mode { get; init; } = DockerSandboxMode.Hardened;
    public string DockerExecutable { get; init; } = "docker";
    public required string Image { get; init; }
    public string? ImageDigest { get; init; }
    public string? LocalImageId { get; init; }
    public required string Runtime { get; init; }
    public required string HostPolicyVersion { get; init; }
    public required string SessionRoot { get; init; }
    public required string MachineKeyRoot { get; init; }
    public string Entrypoint { get; init; } = "etl-sql";
    public string User { get; init; } = "65532:65532";
    public string? DedicatedTenantId { get; init; }
    public string? DedicatedPoolId { get; init; }
    /// <summary>
    /// Host block device that carries sandbox I/O (for example <c>/dev/sda</c>). Block-I/O throttling
    /// is per-device, so a host that does not declare one cannot honour a profile's IOPS ceiling and
    /// must refuse that work instead of running it unthrottled.
    /// </summary>
    public string? IopsThrottleDevice { get; init; }
}

/// <summary>
/// Docker OCI binding for hostile tenant work. Hardened and Dedicated evidence is emitted only for
/// allowlisted non-default runtimes (gVisor/Kata); an ordinary shared-kernel runc container can never
/// satisfy those tiers.
/// </summary>
public sealed class DockerSandboxExecutionProvider : ISandboxExecutionProvider
{
    internal const string AdmissionLabel = "com.etlsql.sandbox.admission";
    internal const string TenantLabel = "com.etlsql.sandbox.tenant";
    internal const string AssignmentLabel = "com.etlsql.sandbox.assignment";
    private static readonly HashSet<string> HardenedRuntimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "runsc", "io.containerd.runsc.v1", "io.containerd.runsc.v2",
        "kata", "kata-qemu", "io.containerd.kata.v2"
    };
    private readonly DockerSandboxExecutionOptions _options;
    private readonly ISandboxCommandRunner _commands;
    private readonly IImmutableSandboxArtifactStore _artifacts;
    private readonly string _sessionRoot;
    private readonly string _machineKeyRoot;

    public DockerSandboxExecutionProvider(
        DockerSandboxExecutionOptions options,
        ISandboxCommandRunner commands,
        IImmutableSandboxArtifactStore artifacts)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _artifacts = artifacts ?? throw new ArgumentNullException(nameof(artifacts));
        ValidateOptions(options);
        _sessionRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.SessionRoot));
        _machineKeyRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.MachineKeyRoot));
        Directory.CreateDirectory(_sessionRoot);
        Directory.CreateDirectory(_machineKeyRoot);
        // Persistent tenant session state and key material stay unreachable to every other account on
        // the host. Only the per-tenant leaf a sandbox actually mounts is opened to its uid.
        SandboxFilePermissions.RestrictToOwner(_sessionRoot);
        SandboxFilePermissions.RestrictToOwner(_machineKeyRoot);
    }

    public async Task<ISandboxAttempt> PrepareAsync(
        SandboxWorkloadRequest request,
        SandboxWorkspaceAssignment workspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(workspace);
        if (string.IsNullOrWhiteSpace(request.AdmissionId))
            throw new InvalidOperationException("A durably activated admission id is required before runtime preparation.");
        if (request.RequiredIsolationTier < SandboxIsolationTier.Hardened && _options.Mode == DockerSandboxMode.Hardened)
            throw new InvalidOperationException("The hardened Docker provider accepts only Hardened or Dedicated work.");
        if (request.RequiredIsolationTier >= SandboxIsolationTier.Hardened && _options.Mode == DockerSandboxMode.Standard)
            throw new InvalidOperationException("The Standard Docker provider accepts only Standard work.");
        if (request.Limits.MaxIops is not null && string.IsNullOrWhiteSpace(_options.IopsThrottleDevice))
            throw new InvalidOperationException(
                "This sandbox host declares no block device for I/O throttling and cannot enforce the " +
                "profile's IOPS limit; running the workload unthrottled would make the ceiling fictional.");
        if (request.CapabilityHandles.Count != 0)
            throw new NotSupportedException(
                "Docker sandbox capability injection is not configured; raw capability handles will not be exposed.");
        if (request.VariableOverrides.Count != 0)
            throw new NotSupportedException(
                "Docker sandbox variable override injection is not configured; values will not be exposed in process arguments.");

        var tenantId = request.Assignment.Tenant.Tenant.Value;
        if (!string.IsNullOrWhiteSpace(_options.DedicatedTenantId) &&
            (!string.Equals(_options.DedicatedTenantId, tenantId, StringComparison.Ordinal) ||
             !string.Equals(_options.DedicatedPoolId, request.AdmissionPolicy.PoolId, StringComparison.Ordinal)))
        {
            throw new UnauthorizedAccessException(
                "This dedicated worker refuses work outside its fixed tenant and capacity pool.");
        }
        if (request.RequiredIsolationTier == SandboxIsolationTier.Dedicated)
        {
            if (!string.Equals(_options.DedicatedTenantId, tenantId, StringComparison.Ordinal) ||
                !string.Equals(_options.DedicatedPoolId, request.AdmissionPolicy.PoolId, StringComparison.Ordinal))
                throw new UnauthorizedAccessException(
                    "Dedicated sandbox work does not match this worker's fixed tenant and capacity pool.");
        }

        var scriptPath = Path.Combine(workspace.InputPath, "job.etlsql");
        await _artifacts.StageAsync(request.ArtifactId, request.ArtifactHash, scriptPath, cancellationToken);
        var sessionPath = ResolveTenantSessionPath(tenantId);
        var machineKeyPath = ResolveTenantMachineKeyPath(tenantId);
        // The session mount is read-write and the sandbox runs as an unprivileged uid, so this leaf
        // must admit that uid or the workload cannot persist session or checkpoint state at all.
        SandboxFilePermissions.AllowUnprivilegedSandboxWrites(
            Directory.CreateDirectory(sessionPath).FullName);
        RequireDockerMountSource(workspace.InputPath);
        RequireDockerMountSource(workspace.OutputPath);
        RequireDockerMountSource(sessionPath);
        RequireDockerMountSource(machineKeyPath);

        var image = await InspectImageAsync(cancellationToken);
        var version = await RequireSuccessAsync(
            ["version", "--format", "{{.Client.Version}}"], "inspect Docker version", cancellationToken);
        await VerifyRuntimeRegisteredAsync(cancellationToken);
        var containerName = $"etlsql-{workspace.AssignmentId}";
        var createInvoked = false;
        try
        {
            createInvoked = true;
            var create = await _commands.RunAsync(
                _options.DockerExecutable,
                BuildCreateArguments(containerName, request, workspace, sessionPath, machineKeyPath),
                cancellationToken);
            RequireSuccess(create, "create hardened sandbox");

            var state = await RequireSuccessAsync(
                ["inspect", "--format", "{{.State.Status}}", containerName],
                "inspect prepared sandbox", cancellationToken);
            if (!state.Trim().Equals("created", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Sandbox preparation did not leave tenant code stopped.");

            var tier = _options.Mode == DockerSandboxMode.Standard
                ? SandboxIsolationTier.Standard
                : (request.RequiredIsolationTier == SandboxIsolationTier.Dedicated
                    ? SandboxIsolationTier.Dedicated
                    : SandboxIsolationTier.Hardened);
            var evidence = new SandboxProviderEvidence(
                "docker-oci", version.Trim(), _options.Runtime, tier, image, _options.HostPolicyVersion);
            return new DockerSandboxAttempt(
                _options, _commands, containerName, request.Limits.MaxDuration, evidence);
        }
        catch (Exception preparationFailure)
        {
            if (createInvoked)
            {
                try
                {
                    var remove = await _commands.RunAsync(
                        _options.DockerExecutable, ["rm", "--force", "--volumes", containerName],
                        CancellationToken.None);
                    var verify = await _commands.RunAsync(
                        _options.DockerExecutable, ["inspect", containerName], CancellationToken.None);
                    if (!IsContainerAbsent(verify))
                        throw new InvalidOperationException("Prepared sandbox removal was not proven.");
                }
                catch (Exception teardownFailure)
                {
                    throw new SandboxPrepareTeardownException(
                        preparationFailure, teardownFailure, request.AdmissionId!);
                }
            }
            throw;
        }
    }

    private IReadOnlyList<string> BuildCreateArguments(
        string containerName,
        SandboxWorkloadRequest request,
        SandboxWorkspaceAssignment workspace,
        string sessionPath,
        string machineKeyPath)
    {
        var args = new List<string>
        {
            "create", "--name", containerName,
            "--runtime", _options.Runtime,
            "--network", "none",
            "--read-only",
            "--user", _options.User,
            "--cap-drop", "ALL",
            "--security-opt", "no-new-privileges",
            "--pids-limit", request.Limits.MaxProcesses.ToString(CultureInfo.InvariantCulture),
            "--cpus", request.Limits.MaxCpuCores.ToString("0.###", CultureInfo.InvariantCulture),
            "--memory", request.Limits.MaxMemoryBytes.ToString(CultureInfo.InvariantCulture),
            "--memory-swap", request.Limits.MaxMemoryBytes.ToString(CultureInfo.InvariantCulture),
            "--tmpfs", $"/workspace/scratch:rw,noexec,nosuid,nodev,size={request.Limits.MaxScratchBytes}",
            "--mount", $"type=bind,source={workspace.InputPath},target=/workspace/input,readonly",
            "--mount", $"type=bind,source={workspace.OutputPath},target=/workspace/output",
            "--mount", $"type=bind,source={sessionPath},target=/var/lib/etl-sql/sessions",
            "--mount", $"type=bind,source={machineKeyPath},target=/run/secrets/etlsql-machine-key,readonly",
            "--env", "Session__Root=/var/lib/etl-sql/sessions",
            "--env", "ETLSQL_MACHINE_KEY_FILE=/run/secrets/etlsql-machine-key",
            "--env", "TMPDIR=/workspace/scratch",
            // Server-owned per-tenant ceiling, not the worker image's default.
            "--env", "Engine__MaxConnectionsPerScript=" +
                     request.Limits.MaxConnectorConcurrency.ToString(CultureInfo.InvariantCulture),
            // Every writable location the workload can reach is this assignment's single-use tmpfs.
            // The root is read-only and the tenant uid is unmapped, so a home, XDG, machine-key
            // cache, log, or outbox path left at its default would either abort the run or write
            // where a later assignment could observe it. These are server-owned, not caller-supplied.
            "--env", "HOME=/workspace/scratch",
            "--env", "XDG_DATA_HOME=/workspace/scratch",
            "--env", "XDG_CONFIG_HOME=/workspace/scratch",
            "--env", "XDG_CACHE_HOME=/workspace/scratch",
            "--env", "ETLSQL_SECURITY_EVENT_OUTBOX_PATH=/workspace/scratch/security-events.db",
            "--env", "Logging__AppLog__Directory=/workspace/scratch/logs/app",
            "--workdir", "/workspace",
            "--label", $"{AdmissionLabel}={request.AdmissionId}",
            "--label", $"{TenantLabel}={request.Assignment.Tenant.Tenant.Value}",
            "--label", $"{AssignmentLabel}={workspace.AssignmentId}",
            "--entrypoint", _options.Entrypoint,
            _options.Image,
            // --log also selects the script log directory, which otherwise defaults to a path under
            // the read-only working directory.
            "run", "/workspace/input/job.etlsql", "--json", "--log", "/workspace/scratch/logs/scripts"
        };
        if (request.Limits.MaxIops is { } iops)
        {
            var rate = iops.ToString(CultureInfo.InvariantCulture);
            args.InsertRange(args.IndexOf("--workdir"),
            [
                "--device-read-iops", $"{_options.IopsThrottleDevice}:{rate}",
                "--device-write-iops", $"{_options.IopsThrottleDevice}:{rate}"
            ]);
        }
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            args.Add("--session");
            args.Add(request.SessionId);
        }
        if (request.ResumeFromCheckpoint) args.Add("--resume");
        return args;
    }

    private string ResolveTenantSessionPath(string tenantId)
    {
        var path = Path.GetFullPath(Path.Combine(_sessionRoot, tenantId));
        if (!path.StartsWith(_sessionRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The tenant session path escaped its server-owned root.");
        return path;
    }

    private string ResolveTenantMachineKeyPath(string tenantId)
    {
        var path = Path.GetFullPath(Path.Combine(_machineKeyRoot, $"{tenantId}.key"));
        if (!path.StartsWith(_machineKeyRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new UnauthorizedAccessException("The tenant machine-key path escaped its server-owned root.");
        if (!File.Exists(path))
            throw new FileNotFoundException("The tenant sandbox machine-key file is not provisioned.");
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("A tenant sandbox machine-key file cannot be a reparse point.");
        if (new FileInfo(path).Length < 32)
            throw new InvalidDataException("The tenant sandbox machine-key file is too short.");
        return path;
    }

    private async Task<string> InspectImageAsync(CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(_options.LocalImageId))
        {
            var raw = await RequireSuccessAsync(
                ["image", "inspect", "--format", "{{.Id}}", _options.Image],
                "inspect sandbox local image", cancellationToken);
            if (!string.Equals(raw.Trim(), _options.LocalImageId, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException("The local sandbox image does not match the pinned local image ID.");
            return _options.LocalImageId;
        }
        else
        {
            var raw = await RequireSuccessAsync(
                ["image", "inspect", "--format", "{{json .RepoDigests}}", _options.Image],
                "inspect sandbox image", cancellationToken);
            string[] repoDigests;
            try
            {
                repoDigests = JsonSerializer.Deserialize<string[]>(raw.Trim()) ?? [];
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Docker returned malformed sandbox image digest evidence.", ex);
            }
            if (!repoDigests.Contains(_options.Image, StringComparer.Ordinal))
                throw new UnauthorizedAccessException("The local sandbox image does not match the pinned digest.");
            return _options.ImageDigest!;
        }
    }

    private static void RequireDockerMountSource(string path)
    {
        if (!Path.IsPathFullyQualified(path) || path.Contains(',') || path.Any(char.IsControl))
            throw new InvalidOperationException(
                "A sandbox mount source is not representable as one Docker --mount value.");
    }

    private async Task VerifyRuntimeRegisteredAsync(CancellationToken cancellationToken)
    {
        var raw = await RequireSuccessAsync(
            ["info", "--format", "{{json .Runtimes}}"],
            "inspect registered OCI runtimes", cancellationToken);
        try
        {
            using var document = JsonDocument.Parse(raw);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.EnumerateObject().Any(property =>
                    property.Name.Equals(_options.Runtime, StringComparison.OrdinalIgnoreCase)))
                throw new UnauthorizedAccessException("The configured hardened OCI runtime is not registered.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Docker returned malformed OCI runtime evidence.", ex);
        }
    }

    private async Task<string> RequireSuccessAsync(
        IReadOnlyList<string> arguments,
        string operation,
        CancellationToken cancellationToken)
    {
        var result = await _commands.RunAsync(_options.DockerExecutable, arguments, cancellationToken);
        RequireSuccess(result, operation);
        return result.StandardOutput;
    }

    private static void RequireSuccess(SandboxCommandResult result, string operation)
    {
        if (result.ExitCode == 0) return;
        throw new InvalidOperationException(
            $"Failed to {operation}: {LogSanitizer.Clean(result.StandardError.Trim())}");
    }

    internal static bool IsContainerAbsent(SandboxCommandResult result)
    {
        if (result.ExitCode == 0) return false;
        var diagnostic = result.StandardError + "\n" + result.StandardOutput;
        return diagnostic.Contains("No such object", StringComparison.OrdinalIgnoreCase) ||
               diagnostic.Contains("No such container", StringComparison.OrdinalIgnoreCase);
    }

    internal static void ValidateOptions(DockerSandboxExecutionOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DockerExecutable);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Image);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.HostPolicyVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.SessionRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.MachineKeyRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Entrypoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.User);

        if (options.IopsThrottleDevice is { } device)
        {
            // The device is joined to the rate with ':', so a value carrying its own colon could
            // rewrite the throttle argument instead of naming a device.
            if (string.IsNullOrWhiteSpace(device) || !device.StartsWith('/') || device.Contains(':'))
                throw new ArgumentException(
                    "The sandbox IOPS throttle device must be an absolute host device path without a colon.",
                    nameof(options));
        }

        if (options.Mode == DockerSandboxMode.Hardened)
        {
            if (string.IsNullOrWhiteSpace(options.ImageDigest))
                throw new ArgumentException("Hardened mode requires ImageDigest.", nameof(options));
            if (!string.IsNullOrWhiteSpace(options.LocalImageId))
                throw new ArgumentException("Hardened mode cannot use LocalImageId.", nameof(options));

            var digest = options.ImageDigest.ToLowerInvariant();
            if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 ||
                digest[7..].Any(character => !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
                throw new ArgumentException("The sandbox image must have a canonical sha256 digest.", nameof(options));
            if (!options.Image.EndsWith("@" + digest, StringComparison.Ordinal))
                throw new ArgumentException("The sandbox image reference must be pinned to ImageDigest.", nameof(options));
            if (!HardenedRuntimes.Contains(options.Runtime))
                throw new ArgumentException("Hardened sandbox runtime must be an allowlisted gVisor or Kata runtime.", nameof(options));
        }
        else if (options.Mode == DockerSandboxMode.Standard)
        {
            if (string.IsNullOrWhiteSpace(options.ImageDigest) && string.IsNullOrWhiteSpace(options.LocalImageId))
                throw new ArgumentException("Standard mode requires ImageDigest or LocalImageId.", nameof(options));
            if (!string.IsNullOrWhiteSpace(options.ImageDigest) && !string.IsNullOrWhiteSpace(options.LocalImageId))
                throw new ArgumentException("Standard mode cannot specify both ImageDigest and LocalImageId.", nameof(options));

            if (!string.IsNullOrWhiteSpace(options.LocalImageId))
            {
                var id = options.LocalImageId.ToLowerInvariant();
                if (!id.StartsWith("sha256:", StringComparison.Ordinal) || id.Length != 71 ||
                    id[7..].Any(character => !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
                    throw new ArgumentException("The local image id must have a canonical sha256 digest.", nameof(options));
            }
            else if (!string.IsNullOrWhiteSpace(options.ImageDigest))
            {
                var digest = options.ImageDigest.ToLowerInvariant();
                if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || digest.Length != 71 ||
                    digest[7..].Any(character => !(character is >= '0' and <= '9' or >= 'a' and <= 'f')))
                    throw new ArgumentException("The sandbox image must have a canonical sha256 digest.", nameof(options));
                if (!options.Image.EndsWith("@" + digest, StringComparison.Ordinal))
                    throw new ArgumentException("The sandbox image reference must be pinned to ImageDigest.", nameof(options));
            }

            if (HardenedRuntimes.Contains(options.Runtime))
                throw new ArgumentException("Standard sandbox mode cannot use a Hardened runtime.", nameof(options));
        }
        if (!Path.IsPathFullyQualified(options.SessionRoot))
            throw new ArgumentException("The sandbox session root must be absolute.", nameof(options));
        if (!Path.IsPathFullyQualified(options.MachineKeyRoot))
            throw new ArgumentException("The sandbox machine-key root must be absolute.", nameof(options));
        if (string.IsNullOrWhiteSpace(options.DedicatedTenantId) != string.IsNullOrWhiteSpace(options.DedicatedPoolId))
            throw new ArgumentException("Dedicated tenant and pool bindings must be configured together.", nameof(options));
        if (!string.IsNullOrWhiteSpace(options.DedicatedTenantId))
            ETL_SQL.Core.Multitenancy.TenantId.FromTrustedSource(options.DedicatedTenantId);
    }

    private sealed class DockerSandboxAttempt(
        DockerSandboxExecutionOptions options,
        ISandboxCommandRunner commands,
        string containerName,
        TimeSpan maxDuration,
        SandboxProviderEvidence evidence) : ISandboxAttempt
    {
        public SandboxProviderEvidence Evidence { get; } = evidence;

        public async Task<SandboxExecutionOutcome> RunAsync(CancellationToken cancellationToken)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(maxDuration);
            try
            {
                var result = await commands.RunAsync(
                    options.DockerExecutable, ["start", "--attach", containerName], timeout.Token);
                var parsed = ProcessJobExecutor.ParseResult(
                    result.ExitCode, result.StandardOutput, result.StandardError, 0, 0);
                return new SandboxExecutionOutcome(
                    parsed.Success ? SandboxTerminalStatus.Succeeded : SandboxTerminalStatus.Failed,
                    result.ExitCode,
                    parsed.ErrorMessage,
                    parsed);
            }
            catch (OperationCanceledException)
            {
                return new SandboxExecutionOutcome(
                    cancellationToken.IsCancellationRequested
                        ? SandboxTerminalStatus.Cancelled
                        : SandboxTerminalStatus.Failed,
                    null,
                    cancellationToken.IsCancellationRequested
                        ? "Sandbox execution was cancelled."
                        : "Sandbox execution exceeded its server-owned duration limit.",
                    new ScriptExecutionResult(false, 0,
                        cancellationToken.IsCancellationRequested
                            ? "Sandbox execution was cancelled."
                            : "Sandbox execution exceeded its server-owned duration limit."));
            }
        }

        public async Task DestroyAsync(CancellationToken cancellationToken)
        {
            var remove = await commands.RunAsync(
                options.DockerExecutable, ["rm", "--force", "--volumes", containerName], cancellationToken);
            if (remove.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Sandbox removal failed: {LogSanitizer.Clean(remove.StandardError.Trim())}");
            var inspect = await commands.RunAsync(
                options.DockerExecutable, ["inspect", containerName], cancellationToken);
            if (!IsContainerAbsent(inspect))
                throw new InvalidOperationException("Sandbox removal did not prove runtime detachment.");
        }
    }
}

public sealed class DockerSandboxRuntimeReconciler(
    DockerSandboxExecutionOptions options,
    ISandboxCommandRunner commands) : ISandboxRuntimeReconciler
{
    public async Task<SandboxRuntimeReconciliationState> ProbeAsync(
        Storage.SandboxAdmissionLedgerEntry admission,
        CancellationToken cancellationToken)
    {
        var filter = $"label={DockerSandboxExecutionProvider.AdmissionLabel}={admission.AdmissionId}";
        var list = await commands.RunAsync(
            options.DockerExecutable,
            ["ps", "--all", "--filter", filter, "--format", "{{.ID}}|{{.State}}"],
            cancellationToken);
        if (list.ExitCode != 0) return SandboxRuntimeReconciliationState.Unknown;
        var rows = list.StandardOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        if (rows.Length == 0) return SandboxRuntimeReconciliationState.Detached;
        if (rows.Length != 1) return SandboxRuntimeReconciliationState.Unknown;
        var parts = rows[0].Split('|', 2);
        if (parts.Length != 2 || string.IsNullOrWhiteSpace(parts[0]))
            return SandboxRuntimeReconciliationState.Unknown;
        if (parts[1].Trim().Equals("running", StringComparison.OrdinalIgnoreCase))
            return SandboxRuntimeReconciliationState.Running;

        var remove = await commands.RunAsync(
            options.DockerExecutable, ["rm", "--force", "--volumes", parts[0].Trim()], cancellationToken);
        if (remove.ExitCode != 0) return SandboxRuntimeReconciliationState.Unknown;
        var verify = await commands.RunAsync(
            options.DockerExecutable, ["inspect", parts[0].Trim()], cancellationToken);
        return DockerSandboxExecutionProvider.IsContainerAbsent(verify)
            ? SandboxRuntimeReconciliationState.Detached
            : SandboxRuntimeReconciliationState.Unknown;
    }
}
