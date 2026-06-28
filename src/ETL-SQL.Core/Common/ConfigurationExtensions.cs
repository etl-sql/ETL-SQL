using System;
using System.Collections.Generic;
using ETL_SQL.Services;
using Microsoft.Extensions.Configuration;

namespace ETL_SQL.Common;

public static class ConfigurationExtensions
{
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
