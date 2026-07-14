using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json.Nodes;
using ETL_SQL.Orchestrator.Service;
using ETL_SQL.ReportPortal;
using ETL_SQL.ReportPortal.Data;
using ETL_SQL.ReportPortal.Services;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace ETL_SQL.ReportPortal.Tests;

[Trait("Category", "Smoke.Security")]
public class RuntimeSecretRotationTests
{
    private const string CurrentJwtSecret = "current-jwt-secret-value-that-is-at-least-32-bytes";
    private const string PreviousJwtSecret = "previous-jwt-secret-value-that-is-at-least-32-bytes";

    [Fact]
    public void JwtKeyRing_SignsWithCurrentAndValidatesPreviousSecret()
    {
        var config = new PortalConfig
        {
            Jwt = new JwtConfig
            {
                Secret = CurrentJwtSecret,
                PreviousSecrets = [PreviousJwtSecret]
            }
        };
        var user = new PortalUser
        {
            Id = 42,
            UserName = "rotation-user",
            SecurityStamp = "stamp"
        };
        var currentToken = new TokenService(config).GenerateJwt(user, ["Viewer"]);

        var oldToken = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            claims: [new Claim(JwtRegisteredClaimNames.Sub, "42")],
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(PreviousJwtSecret)),
                SecurityAlgorithms.HmacSha256)));
        var validation = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = JwtSigningKeyRing.ValidationKeys(config.Jwt)
        };

        var principal = new JwtSecurityTokenHandler().ValidateToken(oldToken, validation, out _);
        var currentOnlyValidation = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = JwtSigningKeyRing.Current(config.Jwt)
        };

        Assert.Equal("42", principal.FindFirstValue(ClaimTypes.NameIdentifier));
        new JwtSecurityTokenHandler().ValidateToken(currentToken, currentOnlyValidation, out _);
        Assert.ThrowsAny<SecurityTokenException>(() =>
            new JwtSecurityTokenHandler().ValidateToken(
                currentToken,
                new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = JwtSigningKeyRing.Current(
                        new JwtConfig { Secret = PreviousJwtSecret })
                },
                out _));
    }

    [Fact]
    public void OrchestratorApiKeyRing_AcceptsCurrentAndPreviousOnly()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestrator:ApiKey"] = "current-key",
                ["Orchestrator:PreviousApiKeys:0"] = "previous-key"
            })
            .Build();

        Assert.True(JobApiEndpoints.ApiKeyAccepted(configuration, "current-key"));
        Assert.True(JobApiEndpoints.ApiKeyAccepted(configuration, "previous-key"));
        Assert.False(JobApiEndpoints.ApiKeyAccepted(configuration, "wrong-key"));
        Assert.False(JobApiEndpoints.ApiKeyAccepted(configuration, null));
    }

    [Fact]
    public void OrchestratorStartup_RejectsMoreThanOnePreviousApiKey()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestrator:ApiKey"] = "current-key",
                ["Orchestrator:PreviousApiKeys:0"] = "previous-key",
                ["Orchestrator:PreviousApiKeys:1"] = "older-key"
            })
            .Build();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            OrchestratorStartup.ValidateApiKeyBinding(configuration));
        Assert.Contains("at most 1", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OrchestratorApiKeyRing_AllowsConfiguredPreviousKeyLimit()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Orchestrator:ApiKey"] = "current-key",
                ["Orchestrator:MaxPreviousApiKeys"] = "2",
                ["Orchestrator:PreviousApiKeys:0"] = "previous-key",
                ["Orchestrator:PreviousApiKeys:1"] = "older-key"
            })
            .Build();

        OrchestratorStartup.ValidateApiKeyBinding(configuration);
        Assert.True(JobApiEndpoints.ApiKeyAccepted(configuration, "current-key"));
        Assert.True(JobApiEndpoints.ApiKeyAccepted(configuration, "previous-key"));
        Assert.True(JobApiEndpoints.ApiKeyAccepted(configuration, "older-key"));
    }

    [Fact]
    public void JwtKeyRing_UsesOnlyOnePreviousSecret()
    {
        var keys = JwtSigningKeyRing.ValidationKeys(new JwtConfig
        {
            Secret = CurrentJwtSecret,
            PreviousSecrets =
            [
                PreviousJwtSecret,
                "older-jwt-secret-value-that-is-at-least-32-bytes"
            ]
        });

        Assert.Equal(2, keys.Count);
    }

    [Fact]
    public void OrchestratorSettings_MigratesPlaintextSidecarToProtectedValue()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"portal_secret_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var databasePath = Path.Combine(tempDir, "portal.db");
            var settingsPath = Path.Combine(tempDir, "portal-orchestrator.json");
            File.WriteAllText(settingsPath,
                """{"ApiUrl":"https://orchestrator.example","ApiKey":"plaintext-secret"}""");
            var provider = DataProtectionProvider.Create(
                new DirectoryInfo(Path.Combine(tempDir, "keys")),
                options => options.SetApplicationName("ETL-SQL.ReportPortal.Tests"));
            var protector = new OrchestratorApiKeyProtector(provider);

            var settings = new OrchestratorSettingsService(
                new PortalConfig { DatabasePath = databasePath },
                protector);

            var persisted = JsonNode.Parse(File.ReadAllText(settingsPath))!.AsObject();
            Assert.Equal("plaintext-secret", settings.ApiKey);
            Assert.Null(persisted["ApiKey"]);
            var protectedValue = persisted["ProtectedApiKey"]?.GetValue<string>();
            Assert.NotNull(protectedValue);
            Assert.DoesNotContain("plaintext-secret", protectedValue, StringComparison.Ordinal);
            Assert.Equal("plaintext-secret", protector.Unprotect(protectedValue));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
