using System.Net;
using System.Security.Cryptography;

namespace ETL_SQL.WorkstationEditor;

public sealed record WorkstationEditorOptions(
    string WorkspaceRoot,
    string? InitialFile,
    int Port,
    bool ReadOnly,
    string SessionToken,
    bool OpenBrowser = false,
    bool StudioMode = false,
    string? InstanceId = null,
    int IdleShutdownMinutes = 0)
{
    public static WorkstationEditorOptions Parse(string[] args, string invocationDirectory)
    {
        string? path = null;
        int port = 0;
        bool readOnly = false;
        bool openBrowser = false;
        bool studioMode = false;
        string? token = null;
        string? instanceId = null;
        var idleShutdownMinutes = 0;

        for (var i = 0; i < args.Length; i++)
        {
            if ((args[i] == "--port" || args[i] == "-p") && i + 1 < args.Length && int.TryParse(args[++i], out var parsedPort))
            {
                port = parsedPort;
            }
            else if (args[i] == "--readonly" || args[i] == "--read-only")
            {
                readOnly = true;
            }
            else if (args[i] == "--open")
            {
                openBrowser = true;
            }
            else if (args[i] == "--studio")
            {
                studioMode = true;
            }
            else if (args[i] == "--token" && i + 1 < args.Length)
            {
                token = args[++i];
            }
            else if (args[i] == "--instance-id" && i + 1 < args.Length)
            {
                instanceId = Guid.TryParse(args[++i], out var parsedInstanceId)
                    ? parsedInstanceId.ToString("D")
                    : throw new ArgumentException("--instance-id must be a GUID.");
            }
            else if (args[i] == "--idle-timeout-minutes" && i + 1 < args.Length)
            {
                if (!int.TryParse(args[++i], out idleShutdownMinutes) || idleShutdownMinutes < 0)
                    throw new ArgumentException("--idle-timeout-minutes must be zero or a positive integer.");
            }
            else if (args[i].StartsWith("-", StringComparison.Ordinal))
            {
                // Reject rather than ignore. An unrecognised flag used to fall through silently and
                // its value was then taken as the positional path, so `--profile dev` quietly opened
                // a workspace called "dev" instead of reporting that the flag does not exist.
                throw new ArgumentException(
                    $"Unknown option '{args[i]}'. Usage: etl-sql-editor <path-or-folder> [--port <n>] [--open] [--readonly].");
            }
            else
            {
                path ??= args[i];
            }
        }

        var resolvedPath = string.IsNullOrWhiteSpace(path)
            ? invocationDirectory
            : Path.GetFullPath(path, invocationDirectory);

        string workspaceRoot;
        string? initialFile = null;
        if (File.Exists(resolvedPath))
        {
            workspaceRoot = Path.GetDirectoryName(resolvedPath) ?? invocationDirectory;
            initialFile = resolvedPath;
        }
        else
        {
            workspaceRoot = Directory.Exists(resolvedPath) ? resolvedPath : invocationDirectory;
        }

        return new WorkstationEditorOptions(
            Path.GetFullPath(workspaceRoot),
            initialFile is null ? null : Path.GetFullPath(initialFile),
            port,
            readOnly,
            string.IsNullOrWhiteSpace(token) ? GenerateToken() : token,
            openBrowser,
            studioMode,
            instanceId ?? Guid.NewGuid().ToString("D"),
            idleShutdownMinutes);
    }

    internal string LocalhostUrl => $"http://{IPAddress.Loopback}:{Port}";

    private static string GenerateToken()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-", StringComparison.Ordinal)
            .Replace("/", "_", StringComparison.Ordinal)
            .TrimEnd('=');
    }
}
