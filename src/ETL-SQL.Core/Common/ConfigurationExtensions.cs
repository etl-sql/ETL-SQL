using System;
using System.Collections.Generic;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Common;

public static class ConfigurationExtensions
{
    /// <summary>
    /// File beside appsettings.json that holds the service secrets the installers generate.
    /// appsettings.json is shared with the CLI and readable by every local user, so the JWT signing
    /// secret, dataset at-rest key, API keys and identity signing secret live here instead, readable
    /// only by the service account and administrators.
    /// </summary>
    public const string InstalledSecretsFileName = "appsettings.secrets.json";

    /// <summary>
    /// The settings the installers move into <see cref="InstalledSecretsFileName"/>, as
    /// colon-separated paths. Keep in sync with build-linux-packages.sh and configure-portal-jwt.ps1
    /// (InstallerConfigurationScriptTests compares all three).
    /// </summary>
    public static readonly IReadOnlyList<string> InstalledSecretPaths =
    [
        "Portal:Jwt:Secret", "Portal:Jwt:PreviousSecrets", "Portal:Dataset:AtRestKey",
        "Portal:Dataset:PreviousAtRestKeys", "Portal:FirstRun:AdminPassword", "Orchestrator:ApiKey",
        "Orchestrator:PreviousApiKeys", "Orchestrator:IdentitySigningSecret", "Portal:Orchestrator:ApiKey",
        "Portal:Orchestrator:IdentitySigningSecret",
    ];

    /// <summary>
    /// Layers <see cref="InstalledSecretsFileName"/> from <paramref name="directory"/> directly above
    /// the appsettings*.json sources, so it overrides them and environment variables and the command
    /// line still override it. A caller who cannot read the file (an ordinary user running the CLI)
    /// simply does not see it. Call before <see cref="AddSecureConfiguration"/>, which snapshots the
    /// sources present when it is added.
    /// </summary>
    public static IConfigurationBuilder AddInstalledSecrets(this IConfigurationBuilder builder, string directory)
    {
        var path = System.IO.Path.Combine(directory, InstalledSecretsFileName);
        if (!CanRead(path)) return builder;

        var source = new Microsoft.Extensions.Configuration.Json.JsonConfigurationSource
        {
            Path = System.IO.Path.GetFullPath(path),
            Optional = true,
            ReloadOnChange = true,
        };
        source.ResolveFileProvider();
        var sources = builder.Sources;
        var lastAppSettings = -1;
        for (var i = 0; i < sources.Count; i++)
        {
            if (sources[i] is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource json
                && IsAppSettingsFile(json.Path))
                lastAppSettings = i;
        }
        sources.Insert(lastAppSettings + 1, source);
        return builder;
    }

    private static bool IsAppSettingsFile(string? path)
    {
        var name = System.IO.Path.GetFileName(path ?? string.Empty);
        return name.StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(name, InstalledSecretsFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanRead(string path)
    {
        try
        {
            using var stream = System.IO.File.Open(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.IO.IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Adds a secure configuration layer that automatically decrypts any values starting with 'ENC:'.
    /// Decryption is performed using the machine-bound key by default.
    /// </summary>
    public static IConfigurationBuilder AddSecureConfiguration(this IConfigurationBuilder builder)
    {
        // We use a separate source that will be added to the end of the builder's list.
        // When Build() is called, this source will run its provider.
        return builder.Add(new SecureConfigurationSource(builder.Build()));
    }

    private class SecureConfigurationSource : IConfigurationSource
    {
        private readonly IConfiguration _existingConfig;

        public SecureConfigurationSource(IConfiguration existingConfig)
        {
            _existingConfig = existingConfig;
        }

        public IConfigurationProvider Build(IConfigurationBuilder builder)
        {
            return new SecureConfigurationProvider(_existingConfig);
        }
    }

    private class SecureConfigurationProvider : ConfigurationProvider
    {
        private readonly IConfiguration _existingConfig;

        public SecureConfigurationProvider(IConfiguration existingConfig)
        {
            _existingConfig = existingConfig;
        }

        public override void Load()
        {
            var machineKey = SecurityService.GetMachineKey();

            foreach (var pair in _existingConfig.AsEnumerable())
            {
                if (pair.Value != null && pair.Value.StartsWith("ENC:"))
                {
                    try
                    {
                        Data[pair.Key] = CryptoUtils.Decrypt(pair.Value, machineKey);
                    }
                    catch
                    {
                        // If decryption fails, we leave the original value (might be an invalid secret or different key)
                        // The application will likely fail later when it tries to use the 'ENC:...' string as a real secret.
                    }
                }
            }
        }
    }
}
