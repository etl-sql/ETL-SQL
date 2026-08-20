using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ETL_SQL.Common;
using ETL_SQL.Core.Governance;
using Spectre.Console;

namespace ETL_SQL.App;

public sealed record GatewayConfig(
    string PortalUrl,
    string BrokerUrl,
    string TenantId,
    string GatewayId,
    string NodeId,
    string WorkloadPublicKeyThumbprint,
    string ProtectedWorkloadPrivateKeyPkcs8);

public static class GatewaySetupService
{
    internal static string ConfigDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ETL-SQL", "gateway");
    internal static string ConfigPath => Path.Combine(ConfigDirectory, "gateway-config.json");

    private sealed record ConsumeRequest(string TenantId, string OneTimeToken, string WorkloadPublicKeyThumbprint);
    private sealed record ConsumeResponse(string TenantId, string GatewayId, string WorkloadPublicKeyThumbprint);

    public static async Task<int> RunSetupAsync(
        string? portalUrl, string? token, string? tenantId, string? gatewayId, string? nodeId,
        bool installService, bool nonInteractive)
    {
        AnsiConsole.MarkupLine("[bold blue]=== ETL-SQL Data Gateway Setup Wizard ===[/]");

        if (string.IsNullOrWhiteSpace(portalUrl))
        {
            if (nonInteractive) return Error("--portal URL is required in non-interactive mode.");
            portalUrl = AnsiConsole.Ask<string>("Portal base URL:");
        }
        if (!Uri.TryCreate(portalUrl, UriKind.Absolute, out var portalUri)
            || (portalUri.Scheme != Uri.UriSchemeHttps
                && !(portalUri.Scheme == Uri.UriSchemeHttp && portalUri.IsLoopback)))
            return Error("The Portal URL must use HTTPS (HTTP is allowed only for loopback testing).");

        if (string.IsNullOrWhiteSpace(token))
        {
            if (nonInteractive) return Error("--token is required in non-interactive mode.");
            token = AnsiConsole.Prompt(new TextPrompt<string>("One-time enrollment token:").Secret('*'));
        }
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            if (nonInteractive) return Error("--tenant is required in non-interactive mode.");
            tenantId = AnsiConsole.Ask<string>("Tenant identifier:");
        }

        nodeId = string.IsNullOrWhiteSpace(nodeId) ? Environment.MachineName : nodeId.Trim();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var publicKey = key.ExportSubjectPublicKeyInfo();
        var thumbprint = Convert.ToHexString(SHA256.HashData(publicKey));

        ConsumeResponse enrollment;
        try
        {
            using var client = PolicyBoundHttp.CreateClient(timeout: TimeSpan.FromSeconds(30));
            using var response = await client.PostAsJsonAsync(
                new Uri(portalUri, "/api/gateway/enrollment/consume"),
                new ConsumeRequest(tenantId.Trim(), token, thumbprint)).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return Error("Portal refused the one-time enrollment token.");
            enrollment = await response.Content.ReadFromJsonAsync<ConsumeResponse>().ConfigureAwait(false)
                ?? throw new InvalidDataException("Portal returned an empty enrollment response.");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidDataException)
        {
            return Error("Gateway enrollment could not be completed with Portal.");
        }

        if (!string.IsNullOrWhiteSpace(gatewayId)
            && !string.Equals(gatewayId, enrollment.GatewayId, StringComparison.Ordinal))
            return Error("The enrolled Gateway ID does not match --gateway-id.");
        if (!string.Equals(thumbprint, enrollment.WorkloadPublicKeyThumbprint, StringComparison.OrdinalIgnoreCase))
            return Error("Portal returned a different workload identity than the one enrolled.");

        var entropy = $"gateway:{enrollment.TenantId}:{enrollment.GatewayId}";
        var protectedPrivateKey = CryptoUtils.ProtectMachine(
            Convert.ToBase64String(key.ExportPkcs8PrivateKey()), entropy);
        var brokerUri = new UriBuilder(portalUri)
        {
            Scheme = portalUri.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
            Port = portalUri.IsDefaultPort ? -1 : portalUri.Port,
            Path = "/api/gateway/broker",
            Query = string.Empty
        }.Uri.AbsoluteUri;
        var config = new GatewayConfig(
            portalUri.GetLeftPart(UriPartial.Authority).TrimEnd('/'), brokerUri,
            enrollment.TenantId, enrollment.GatewayId, nodeId, thumbprint, protectedPrivateKey);

        Directory.CreateDirectory(ConfigDirectory);
        await File.WriteAllTextAsync(
            ConfigPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8)
            .ConfigureAwait(false);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(ConfigPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);

        AnsiConsole.MarkupLine($"[green]✓[/] Enrolled Gateway [grey]{Markup.Escape(enrollment.GatewayId)}[/].");
        AnsiConsole.MarkupLine($"[green]✓[/] Protected configuration saved to [grey]{Markup.Escape(ConfigPath)}[/].");
        if (installService) PrintServiceInstructions();
        AnsiConsole.MarkupLine("Run [bold]etlsql gateway start[/] to start the foreground daemon.");
        return 0;
    }

    private static int Error(string message)
    {
        AnsiConsole.MarkupLine($"[red]Error: {Markup.Escape(message)}[/]");
        return 1;
    }

    private static void PrintServiceInstructions()
    {
        if (OperatingSystem.IsWindows())
            AnsiConsole.MarkupLine($"Run as Administrator: [grey]sc.exe create ETLSQLGateway binPath= \"{Markup.Escape(Environment.ProcessPath ?? "etlsql")} gateway start\" start= auto[/]");
        else if (OperatingSystem.IsLinux())
            AnsiConsole.MarkupLine("Install deploy/systemd/etlsql-gateway.service, then run [grey]systemctl enable --now etlsql-gateway[/].");
    }
}
