using System.Net;
using System.Security.Cryptography;

namespace ETL_SQL.WorkstationEditor;

public sealed record WorkstationEditorOptions(
    string WorkspaceRoot,
    string? InitialFile,
    int Port,
    bool ReadOnly,
    string SessionToken)
{
    public static WorkstationEditorOptions Parse(string[] args, string invocationDirectory)
    {
        string? path = null;
        int port = 0;
        bool readOnly = false;
        string? token = null;

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
            else if (args[i] == "--token" && i + 1 < args.Length)
            {
                token = args[++i];
            }
            else if (!args[i].StartsWith("-", StringComparison.Ordinal))
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
            string.IsNullOrWhiteSpace(token) ? GenerateToken() : token);
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
