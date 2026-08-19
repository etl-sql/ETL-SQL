using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Spectre.Console;

namespace ETL_SQL.App
{
    public sealed record GatewayConfig(
        string PortalUrl,
        string TenantId,
        string GatewayId,
        string NodeId,
        string WorkloadPublicKeyThumbprint,
        string WorkloadPrivateKeyHex);

    public static class GatewaySetupService
    {
        public static async Task<int> RunSetupAsync(
            string? portalUrl,
            string? token,
            string? gatewayId,
            string? nodeId,
            bool installService,
            bool nonInteractive)
        {
            AnsiConsole.MarkupLine("[bold blue]=== ETL-SQL Data Gateway Setup Wizard ===[/]");
            AnsiConsole.MarkupLine("[grey]Configuring on-premises Gateway daemon for secure cloud connectivity.[/]\n");

            if (string.IsNullOrWhiteSpace(portalUrl))
            {
                if (nonInteractive)
                {
                    AnsiConsole.MarkupLine("[red]Error: --portal URL is required in non-interactive mode.[/]");
                    return 1;
                }
                portalUrl = AnsiConsole.Ask<string>("Enter the Portal base URL (e.g. [green]https://portal.company.com[/]):");
            }

            if (string.IsNullOrWhiteSpace(token))
            {
                if (nonInteractive)
                {
                    AnsiConsole.MarkupLine("[red]Error: --token is required in non-interactive mode.[/]");
                    return 1;
                }
                token = AnsiConsole.Prompt(
                    new TextPrompt<string>("Enter the one-time [green]Enrollment Token[/] issued by Portal:")
                        .Secret('*'));
            }

            if (string.IsNullOrWhiteSpace(nodeId))
            {
                nodeId = Environment.MachineName;
                if (!nonInteractive)
                {
                    nodeId = AnsiConsole.Ask("Node Identifier (machine name):", nodeId);
                }
            }

            // Generate workload identity key pair (ECDSA P-256)
            using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var pubKeyBytes = ecdsa.ExportSubjectPublicKeyInfo();
            var thumbprint = Convert.ToHexString(SHA256.HashData(pubKeyBytes)).ToLowerInvariant();
            var privKeyHex = Convert.ToHexString(ecdsa.ExportECPrivateKey());

            AnsiConsole.MarkupLine($"[green]✓[/] Generated local cryptographic workload identity: [grey]{thumbprint[..Math.Min(12, thumbprint.Length)]}...[/]");

            // Save local configuration
            var configDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ETL-SQL", "gateway");
            Directory.CreateDirectory(configDir);
            var configPath = Path.Combine(configDir, "gateway-config.json");

            var config = new GatewayConfig(
                PortalUrl: portalUrl.TrimEnd('/'),
                TenantId: "default",
                GatewayId: gatewayId ?? "default-gw",
                NodeId: nodeId,
                WorkloadPublicKeyThumbprint: thumbprint,
                WorkloadPrivateKeyHex: privKeyHex);

            var json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configPath, json, Encoding.UTF8);

            AnsiConsole.MarkupLine($"[green]✓[/] Gateway configuration saved to: [grey]{configPath}[/]");

            // Check if service installation was requested
            if (!installService && !nonInteractive)
            {
                installService = AnsiConsole.Confirm("Would you like to register this Gateway as a background system service?", false);
            }

            if (installService)
            {
                if (OperatingSystem.IsWindows())
                {
                    AnsiConsole.MarkupLine("\n[yellow]To register Windows Service, run as Administrator:[/]");
                    AnsiConsole.MarkupLine($"[grey]sc.exe create ETLSQLGateway binPath= \"{Environment.ProcessPath} gateway start\" start= auto[/]");
                }
                else if (OperatingSystem.IsLinux())
                {
                    AnsiConsole.MarkupLine("\n[yellow]To enable systemd service on Linux, run:[/]");
                    AnsiConsole.MarkupLine($"[grey]sudo systemctl enable --now etlsql-gateway.service[/]");
                }
            }

            AnsiConsole.MarkupLine("\n[bold green]✓ Gateway setup complete![/]");
            AnsiConsole.MarkupLine("To start the Gateway daemon in foreground mode, run: [bold]etlsql gateway start[/]\n");
            return 0;
        }
    }
}
