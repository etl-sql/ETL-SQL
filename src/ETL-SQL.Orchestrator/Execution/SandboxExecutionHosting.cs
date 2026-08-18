using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ETL_SQL.Orchestrator.Execution;

public static class SandboxExecutionServiceCollectionExtensions
{
    public static IServiceCollection AddHardenedSandboxExecution(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Orchestration:SandboxExecution");
        if (!section.GetValue("Enabled", false)) return services;
        if (!configuration.GetValue("Orchestration:SandboxAdmission:Enabled", false))
            throw new InvalidOperationException(
                "Hardened sandbox execution requires durable sandbox admission to be enabled.");

        var provider = new DockerSandboxExecutionOptions
        {
            DockerExecutable = section["DockerExecutable"] ?? "docker",
            Image = Require(section, "Image"),
            ImageDigest = Require(section, "ImageDigest").ToLowerInvariant(),
            Runtime = Require(section, "Runtime"),
            HostPolicyVersion = Require(section, "HostPolicyVersion"),
            SessionRoot = RequireAbsolute(section, "SessionRoot"),
            MachineKeyRoot = RequireAbsolute(section, "MachineKeyRoot"),
            Entrypoint = section["Entrypoint"] ?? "etl-sql",
            User = section["User"] ?? "65532:65532",
            DedicatedTenantId = section["DedicatedTenantId"],
            DedicatedPoolId = section["DedicatedPoolId"],
            IopsThrottleDevice = section["IopsThrottleDevice"]
        };
        var profiles = ReadProfiles(section.GetSection("Profiles"));
        var tenants = ReadTenants(section.GetSection("Tenants"));
        var policyCatalog = new SandboxWorkloadPolicyCatalog { Profiles = profiles, Tenants = tenants };
        // Constructor validation is intentionally performed during registration so malformed or
        // incomplete hostile-runtime policy fails host startup before any scheduler loop begins.
        _ = new SandboxWorkloadPolicyResolver(policyCatalog);
        DockerSandboxExecutionProvider.ValidateOptions(provider);
        ValidatePlacement(configuration, provider, profiles, tenants);

        services.AddSingleton(provider);
        services.AddSingleton(new SandboxScheduledJobExecutorOptions
        {
            PolicyVersion = Require(section, "PolicyVersion"),
            BindingVersion = Require(section, "BindingVersion")
        });
        services.AddSingleton(new FileSystemSandboxWorkspaceOptions
        {
            RootPath = RequireAbsolute(section, "WorkspaceRoot")
        });
        services.AddSingleton(new ImmutableSandboxArtifactStoreOptions
        {
            RootPath = RequireAbsolute(section, "ArtifactRoot")
        });
        services.AddSingleton(policyCatalog);
        services.AddSingleton<ISandboxCommandRunner, ProcessSandboxCommandRunner>();
        services.AddSingleton<IImmutableSandboxArtifactStore, FileSystemImmutableSandboxArtifactStore>();
        services.AddSingleton<ISandboxWorkspaceProvider, FileSystemSandboxWorkspaceProvider>();
        services.AddSingleton<ISandboxWorkloadPolicyResolver, SandboxWorkloadPolicyResolver>();
        services.AddSingleton<ISandboxTenantContextResolver>(
            new SandboxTenantContextResolver(configuration["Orchestrator:TenantId"]));
        // Capabilities resolve through the governance secret provider, so a sandbox capability is the
        // same material, with the same custody, as every other secret the deployment holds. A host
        // with no secret provider configured has no resolver, and the provider then refuses work
        // that was granted capabilities rather than running it without them.
        services.AddSingleton<ISandboxCapabilityResolver>(sp =>
            new SecretBackedSandboxCapabilityResolver(
                sp.GetRequiredService<ETL_SQL.Core.Governance.ISecretProvider>()));
        services.AddSingleton<ISandboxExecutionProvider>(sp => new DockerSandboxExecutionProvider(
            sp.GetRequiredService<DockerSandboxExecutionOptions>(),
            sp.GetRequiredService<ISandboxCommandRunner>(),
            sp.GetRequiredService<IImmutableSandboxArtifactStore>(),
            sp.GetService<ISandboxCapabilityResolver>()));
        services.AddSingleton<ISandboxRuntimeReconciler, DockerSandboxRuntimeReconciler>();
        services.AddSingleton<SandboxExecutionCoordinator>();
        services.AddSingleton<ISandboxScheduledJobExecutor, SandboxScheduledJobExecutor>();
        return services;
    }

    public static IServiceCollection AddStandardDockerSandboxExecution(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var section = configuration.GetSection("Orchestration:SandboxExecution");
        if (!section.GetValue("Enabled", false)) return services;
        if (!configuration.GetValue("Orchestration:SandboxAdmission:Enabled", false))
            throw new InvalidOperationException(
                "Standard sandbox execution requires durable sandbox admission to be enabled.");

        var provider = new DockerSandboxExecutionOptions
        {
            Mode = DockerSandboxMode.Standard,
            DockerExecutable = section["DockerExecutable"] ?? "docker",
            Image = Require(section, "Image"),
            ImageDigest = section["ImageDigest"],
            LocalImageId = section["LocalImageId"],
            Runtime = Require(section, "Runtime"),
            HostPolicyVersion = Require(section, "HostPolicyVersion"),
            SessionRoot = RequireAbsolute(section, "SessionRoot"),
            MachineKeyRoot = RequireAbsolute(section, "MachineKeyRoot"),
            Entrypoint = section["Entrypoint"] ?? "etl-sql",
            User = section["User"] ?? "65532:65532",
            DedicatedTenantId = section["DedicatedTenantId"],
            DedicatedPoolId = section["DedicatedPoolId"],
            IopsThrottleDevice = section["IopsThrottleDevice"]
        };
        var profiles = ReadProfiles(section.GetSection("Profiles"));
        var tenants = ReadTenants(section.GetSection("Tenants"));
        var policyCatalog = new SandboxWorkloadPolicyCatalog { Profiles = profiles, Tenants = tenants };
        _ = new SandboxWorkloadPolicyResolver(policyCatalog);
        DockerSandboxExecutionProvider.ValidateOptions(provider);
        ValidatePlacement(configuration, provider, profiles, tenants);

        services.AddSingleton(provider);
        services.AddSingleton(new SandboxScheduledJobExecutorOptions
        {
            PolicyVersion = Require(section, "PolicyVersion"),
            BindingVersion = Require(section, "BindingVersion")
        });
        services.AddSingleton(new FileSystemSandboxWorkspaceOptions
        {
            RootPath = RequireAbsolute(section, "WorkspaceRoot")
        });
        services.AddSingleton(new ImmutableSandboxArtifactStoreOptions
        {
            RootPath = RequireAbsolute(section, "ArtifactRoot")
        });
        services.AddSingleton(policyCatalog);
        services.AddSingleton<ISandboxCommandRunner, ProcessSandboxCommandRunner>();
        services.AddSingleton<IImmutableSandboxArtifactStore, FileSystemImmutableSandboxArtifactStore>();
        services.AddSingleton<ISandboxWorkspaceProvider, FileSystemSandboxWorkspaceProvider>();
        services.AddSingleton<ISandboxWorkloadPolicyResolver, SandboxWorkloadPolicyResolver>();
        services.AddSingleton<ISandboxTenantContextResolver>(
            new SandboxTenantContextResolver(configuration["Orchestrator:TenantId"]));
        // Capabilities resolve through the governance secret provider, so a sandbox capability is the
        // same material, with the same custody, as every other secret the deployment holds. A host
        // with no secret provider configured has no resolver, and the provider then refuses work
        // that was granted capabilities rather than running it without them.
        services.AddSingleton<ISandboxCapabilityResolver>(sp =>
            new SecretBackedSandboxCapabilityResolver(
                sp.GetRequiredService<ETL_SQL.Core.Governance.ISecretProvider>()));
        services.AddSingleton<ISandboxExecutionProvider>(sp => new DockerSandboxExecutionProvider(
            sp.GetRequiredService<DockerSandboxExecutionOptions>(),
            sp.GetRequiredService<ISandboxCommandRunner>(),
            sp.GetRequiredService<IImmutableSandboxArtifactStore>(),
            sp.GetService<ISandboxCapabilityResolver>()));
        services.AddSingleton<ISandboxRuntimeReconciler, DockerSandboxRuntimeReconciler>();
        services.AddSingleton<SandboxExecutionCoordinator>();
        services.AddSingleton<ISandboxScheduledJobExecutor, SandboxScheduledJobExecutor>();
        return services;
    }

    private static void ValidatePlacement(
        IConfiguration configuration,
        DockerSandboxExecutionOptions provider,
        IReadOnlyDictionary<string, SandboxExecutionProfile> profiles,
        IReadOnlyDictionary<string, SandboxTenantAdmissionPolicy> tenants)
    {
        var capacities = configuration.GetSection("Orchestration:SandboxAdmission:PoolCapacities")
            .GetChildren().ToDictionary(child => child.Key, child => child.Value, StringComparer.Ordinal);
        foreach (var profile in profiles.Values)
        {
            if (provider.Mode == DockerSandboxMode.Hardened && profile.IsolationTier < SandboxIsolationTier.Hardened)
                throw new InvalidOperationException(
                    "The hardened sandbox host cannot advertise Local or Standard profiles.");
            if (provider.Mode == DockerSandboxMode.Standard && profile.IsolationTier >= SandboxIsolationTier.Hardened)
                throw new InvalidOperationException(
                    "The standard sandbox host cannot advertise Hardened or Dedicated profiles.");

            if (!capacities.TryGetValue(profile.PoolId, out var capacity) ||
                !int.TryParse(capacity, out var parsed) || parsed <= 0)
                throw new InvalidOperationException(
                    $"Sandbox profile pool '{profile.PoolId}' has no positive durable admission capacity.");
        }

        var dedicatedProfiles = profiles
            .Where(pair => pair.Value.IsolationTier == SandboxIsolationTier.Dedicated)
            .ToArray();
        if (dedicatedProfiles.Length == 0) return;
        var hostTenant = configuration["Orchestrator:TenantId"];
        if (string.IsNullOrWhiteSpace(hostTenant) ||
            !string.Equals(hostTenant, provider.DedicatedTenantId, StringComparison.Ordinal))
            throw new InvalidOperationException(
                "Dedicated sandbox profiles require matching Orchestrator:TenantId and DedicatedTenantId authority.");
        if (dedicatedProfiles.Any(profile =>
                !string.Equals(profile.Value.PoolId, provider.DedicatedPoolId, StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "Every Dedicated profile on a tenant-dedicated worker must use its fixed DedicatedPoolId.");
        if (tenants.Count != 1 || !tenants.ContainsKey(hostTenant))
            throw new InvalidOperationException(
                "A tenant-dedicated worker policy catalog must contain exactly its host-fixed tenant.");
    }

    private static Dictionary<string, SandboxExecutionProfile> ReadProfiles(IConfigurationSection section)
    {
        var profiles = new Dictionary<string, SandboxExecutionProfile>(StringComparer.Ordinal);
        foreach (var child in section.GetChildren())
        {
            if (!Enum.TryParse<SandboxIsolationTier>(child["IsolationTier"], true, out var tier))
                throw new InvalidOperationException($"Sandbox profile '{child.Key}' has an invalid isolation tier.");
            profiles.Add(child.Key, new SandboxExecutionProfile
            {
                PoolId = Require(child, "PoolId"),
                IsolationTier = tier,
                Limits = new SandboxResourceLimits
                {
                    MaxDuration = TimeSpan.FromSeconds(RequirePositiveDouble(child, "MaxDurationSeconds")),
                    MaxMemoryBytes = RequirePositiveLong(child, "MaxMemoryBytes"),
                    MaxScratchBytes = RequirePositiveLong(child, "MaxScratchBytes"),
                    MaxProcesses = checked((int)RequirePositiveLong(child, "MaxProcesses")),
                    MaxCpuCores = RequirePositiveDouble(child, "MaxCpuCores"),
                    MaxConnectorConcurrency = checked((int)RequirePositiveLong(child, "MaxConnectorConcurrency")),
                    MaxIops = OptionalPositiveInt(child, "MaxIops"),
                    MaxRows = OptionalPositiveLong(child, "MaxRows")
                }
            });
        }
        return profiles;
    }

    private static Dictionary<string, SandboxTenantAdmissionPolicy> ReadTenants(IConfigurationSection section)
    {
        var tenants = new Dictionary<string, SandboxTenantAdmissionPolicy>(StringComparer.Ordinal);
        foreach (var child in section.GetChildren())
        {
            var allowed = child.GetSection("AllowedProfiles").GetChildren()
                .Select(item => item.Value)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Cast<string>()
                .ToArray();
            tenants.Add(child.Key, new SandboxTenantAdmissionPolicy
            {
                DefaultProfile = Require(child, "DefaultProfile"),
                AllowedProfiles = allowed,
                Weight = checked((int)RequirePositiveLong(child, "Weight")),
                MaxConcurrentAttempts = checked((int)RequirePositiveLong(child, "MaxConcurrentAttempts")),
                MaxQueuedAttempts = checked((int)RequirePositiveLong(child, "MaxQueuedAttempts"))
            });
        }
        return tenants;
    }

    private static string Require(IConfigurationSection section, string key) =>
        string.IsNullOrWhiteSpace(section[key])
            ? throw new InvalidOperationException($"Sandbox execution configuration '{section.Path}:{key}' is required.")
            : section[key]!;

    private static string RequireAbsolute(IConfigurationSection section, string key)
    {
        var value = Require(section, key);
        if (!Path.IsPathFullyQualified(value))
            throw new InvalidOperationException($"Sandbox execution configuration '{section.Path}:{key}' must be absolute.");
        return Path.GetFullPath(value);
    }

    private static long RequirePositiveLong(IConfigurationSection section, string key) =>
        long.TryParse(section[key], out var value) && value > 0
            ? value
            : throw new InvalidOperationException($"Sandbox execution configuration '{section.Path}:{key}' must be positive.");

    private static long? OptionalPositiveLong(IConfigurationSection section, string key)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return long.TryParse(raw, out var value) && value > 0
            ? value
            : throw new InvalidOperationException(
                $"Sandbox execution configuration '{section.Path}:{key}' must be a positive integer when present.");
    }

    private static int? OptionalPositiveInt(IConfigurationSection section, string key)
    {
        var raw = section[key];
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return int.TryParse(raw, out var value) && value > 0
            ? value
            : throw new InvalidOperationException(
                $"Sandbox execution configuration '{section.Path}:{key}' must be a positive integer when present.");
    }

    private static double RequirePositiveDouble(IConfigurationSection section, string key) =>
        double.TryParse(section[key], System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value > 0
            ? value
            : throw new InvalidOperationException($"Sandbox execution configuration '{section.Path}:{key}' must be positive.");
}
