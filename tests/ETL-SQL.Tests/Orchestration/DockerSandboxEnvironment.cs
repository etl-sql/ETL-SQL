using System.Diagnostics;
using System.Text.Json;
using Xunit;

namespace ETL_SQL.Tests.Orchestration;

/// <summary>
/// Detects which sandbox isolation tiers this host can actually produce evidence for. Each gate
/// skips only when its environment is absent; once Docker, the runtime, and the image are all
/// present, a broken lifecycle must fail rather than skip.
/// </summary>
internal static class DockerSandboxEnvironment
{
    /// <summary>A local tag or image reference for the Standard lane.</summary>
    public const string ImageVariable = "ETLSQL_SANDBOX_WORKER_IMAGE";

    /// <summary>
    /// A digest-pinned reference (<c>repo@sha256:…</c>) for the Hardened lane. Hardened mode never
    /// accepts a local image ID, so this must be an image the daemon holds by repo digest.
    /// </summary>
    public const string HardenedImageVariable = "ETLSQL_SANDBOX_WORKER_DIGEST_IMAGE";

    private const string DefaultImage = "etlsql-sandbox-worker-test:local";
    private static readonly string[] StandardRuntimes = ["runc", "io.containerd.runc.v2"];
    private static readonly string[] HardenedRuntimes =
        ["runsc", "io.containerd.runsc.v1", "io.containerd.runsc.v2", "kata", "kata-qemu", "io.containerd.kata.v2"];

    private static readonly Lazy<Detection> StandardDetected =
        new(DetectStandard, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Lazy<Detection> HardenedDetected =
        new(DetectHardened, LazyThreadSafetyMode.ExecutionAndPublication);

    public static string? StandardSkipReason => StandardDetected.Value.SkipReason;
    public static string? HardenedSkipReason => HardenedDetected.Value.SkipReason;

    /// <summary>Standard lane image reference (a local tag is acceptable here).</summary>
    public static string Image => Require(StandardDetected).Image;

    /// <summary>
    /// The Standard image's content ID. A local image ID, not a registry <c>RepoDigest</c>, which is
    /// why only the Standard provider mode may consume it.
    /// </summary>
    public static string ImageId => Require(StandardDetected).ImageIdentity;

    /// <summary>The registered ordinary OCI runtime, never a Hardened one.</summary>
    public static string StandardRuntime => Require(StandardDetected).Runtime;

    /// <summary>Hardened lane image, pinned to <see cref="HardenedDigest"/>.</summary>
    public static string HardenedImage => Require(HardenedDetected).Image;

    /// <summary>The Hardened image's canonical registry digest.</summary>
    public static string HardenedDigest => Require(HardenedDetected).ImageIdentity;

    /// <summary>The registered gVisor or Kata runtime.</summary>
    public static string HardenedRuntime => Require(HardenedDetected).Runtime;

    private static Detection Require(Lazy<Detection> detection) => detection.Value.SkipReason is null
        ? detection.Value
        : throw new InvalidOperationException(detection.Value.SkipReason);

    private static Detection DetectStandard()
    {
        if (CheckDaemon() is { } unavailable) return unavailable;

        var runtime = FindRuntime(StandardRuntimes, out var registered);
        if (runtime is null)
        {
            return Detection.Skipped(
                "No ordinary runc runtime is registered, so no Standard evidence can be produced here. " +
                $"Registered runtimes: {registered}");
        }

        var image = Environment.GetEnvironmentVariable(ImageVariable);
        if (string.IsNullOrWhiteSpace(image)) image = DefaultImage;
        if (!TryRun(["image", "inspect", "--format", "{{.Id}}", image], out var imageId))
        {
            return Detection.Skipped(
                $"The sandbox worker image '{image}' is not present. Build it with " +
                "'pwsh -File scripts/Test-SandboxWorkerImage.ps1 -Tag etlsql-sandbox-worker-test:local', " +
                $"or point {ImageVariable} at an existing worker image.");
        }

        return new Detection(null, image, imageId.Trim(), runtime);
    }

    private static Detection DetectHardened()
    {
        if (CheckDaemon() is { } unavailable) return unavailable;

        var runtime = FindRuntime(HardenedRuntimes, out var registered);
        if (runtime is null)
        {
            return Detection.Skipped(
                "No gVisor or Kata runtime is registered, so no Hardened evidence can be produced here. " +
                $"An ordinary shared-kernel runtime cannot stand in. Registered runtimes: {registered}");
        }

        var image = Environment.GetEnvironmentVariable(HardenedImageVariable);
        if (string.IsNullOrWhiteSpace(image))
        {
            return Detection.Skipped(
                $"{HardenedImageVariable} is not set. Hardened mode requires a digest-pinned worker " +
                "image reference (repo@sha256:...) that this daemon holds by repo digest.");
        }

        var separator = image.LastIndexOf("@sha256:", StringComparison.Ordinal);
        if (separator < 0)
        {
            return Detection.Skipped(
                $"{HardenedImageVariable} ('{image}') is not digest-pinned; Hardened mode refuses a mutable tag.");
        }
        var digest = image[(separator + 1)..];

        if (!TryRun(["image", "inspect", "--format", "{{json .RepoDigests}}", image], out var repoDigests))
            return Detection.Skipped($"The digest-pinned worker image '{image}' is not present on this daemon.");
        try
        {
            var parsed = JsonSerializer.Deserialize<string[]>(repoDigests.Trim()) ?? [];
            if (!parsed.Contains(image, StringComparer.Ordinal))
            {
                return Detection.Skipped(
                    $"The daemon does not hold '{image}' by repo digest, so its identity cannot be pinned.");
            }
        }
        catch (JsonException)
        {
            return Detection.Skipped("Docker returned malformed sandbox image digest evidence.");
        }

        return new Detection(null, image, digest, runtime);
    }

    private static Detection? CheckDaemon()
    {
        if (!TryRun(["version", "--format", "{{.Server.Os}}"], out var serverOs))
        {
            return Detection.Skipped(
                "Docker is not available on this host, so the sandbox lifecycle cannot be exercised.");
        }
        if (!serverOs.Trim().Equals("linux", StringComparison.OrdinalIgnoreCase))
        {
            return Detection.Skipped(
                $"The sandbox worker image is linux/amd64 but this Docker server reports '{serverOs.Trim()}'.");
        }

        return null;
    }

    private static string? FindRuntime(IReadOnlyList<string> candidates, out string registered)
    {
        registered = "<unavailable>";
        if (!TryRun(["info", "--format", "{{json .Runtimes}}"], out var runtimes)) return null;
        registered = runtimes.Trim();
        try
        {
            using var document = JsonDocument.Parse(runtimes);
            return candidates.FirstOrDefault(candidate =>
                document.RootElement.EnumerateObject().Any(property =>
                    property.Name.Equals(candidate, StringComparison.OrdinalIgnoreCase)));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static bool TryRun(IReadOnlyList<string> arguments, out string standardOutput)
    {
        standardOutput = string.Empty;
        try
        {
            var start = new ProcessStartInfo
            {
                FileName = "docker",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var argument in arguments) start.ArgumentList.Add(argument);

            using var process = Process.Start(start);
            if (process is null) return false;
            standardOutput = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(milliseconds: 60_000)) return false;
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private sealed record Detection(string? SkipReason, string Image, string ImageIdentity, string Runtime)
    {
        public static Detection Skipped(string reason) => new(reason, "", "", "");
    }
}

/// <summary>
/// A fact that runs the lifecycle on an ordinary runtime. Evidence it produces is Docker /
/// <c>runc</c> / Standard and is never a hostile-tenant boundary result.
/// </summary>
public sealed class DockerStandardSandboxFactAttribute : FactAttribute
{
    public DockerStandardSandboxFactAttribute()
    {
        Skip = DockerSandboxEnvironment.StandardSkipReason;
    }
}

/// <summary>
/// A fact that runs the lifecycle on a registered gVisor or Kata runtime with a digest-pinned image.
/// This is the tier that may be cited as Hardened evidence.
/// </summary>
public sealed class DockerHardenedSandboxFactAttribute : FactAttribute
{
    public DockerHardenedSandboxFactAttribute()
    {
        Skip = DockerSandboxEnvironment.HardenedSkipReason;
    }
}
