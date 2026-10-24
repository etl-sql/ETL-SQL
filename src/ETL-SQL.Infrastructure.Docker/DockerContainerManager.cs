using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Docker.DotNet;
using Docker.DotNet.Models;
using DotNet.Testcontainers.Configurations;
using DotNet.Testcontainers.Containers;
using ETL_SQL.Common;
using ETL_SQL.Core.Common.Exceptions;
using Microsoft.Extensions.Logging;
using Testcontainers.MsSql;
using Testcontainers.MySql;
using Testcontainers.Oracle;
using Testcontainers.PostgreSql;

namespace ETL_SQL.Core;

public class DockerContainerManager : IDockerManager
{
    // Keyed by scope and alias (see ScopedKey). One manager serves a whole host process, so an
    // alias, the last container and the container itself belong to one tenant's scope.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IContainer> _activeContainers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _connectionStrings = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string? Alias, string? ConnectionString)> _last = new(StringComparer.Ordinal);
    public bool HasActiveContainers => !_activeContainers.IsEmpty;
    private const char ScopeSeparator = '\u001f';

    public string? GetLastConnectionString(string scope) => _last.TryGetValue(scope, out var last) ? last.ConnectionString : null;
    public string? GetLastAlias(string scope) => _last.TryGetValue(scope, out var last) ? last.Alias : null;

    private static string ScopedKey(string scope, string key) => scope + ScopeSeparator + key;
    private static string ScopePrefix(string scope) => scope + ScopeSeparator;

    private void Remember(string scope, string? alias, string connectionString) =>
        _last.AddOrUpdate(scope, (alias, connectionString), (_, previous) => (alias ?? previous.Alias, connectionString));

    /// <summary>
    /// The Docker container name for an alias. A tenant's containers carry a tag derived from its
    /// id, so two tenants using the same alias never share, or re-attach to, one container.
    /// </summary>
    internal static string ContainerName(string scope, string alias)
    {
        var tag = scope.Length == 0
            ? ""
            : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(scope)))[..12].ToLowerInvariant() + "_";
        return "etlsql_" + tag + alias.Replace(".", "_").Replace(":", "_");
    }

    private readonly ILoggerFactory _loggerFactory;
    private readonly ETL_SQL.Common.ILogger _logger;

    public DockerContainerManager(ETL_SQL.Common.ILogger logger)
    {
        _logger = logger;
        _loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder =>
            builder.AddProvider(new TestcontainersLoggerProvider(_logger)));
    }

    /// <summary>
    /// Retrieves the connection string for a given container alias or image name.
    /// </summary>
    /// <param name="alias">The alias or image name to look up.</param>
    /// <returns>The connection string if found, otherwise null.</returns>
    public string? GetConnectionString(string scope, string alias)
    {
        if (_connectionStrings.TryGetValue(ScopedKey(scope, alias), out var connStr)) return connStr;
        // Fall back to a partial image name only when it names exactly one container, and return
        // that container's own connection string. It used to return the most recently started one.
        var prefix = ScopePrefix(scope);
        var matches = _connectionStrings.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal) && k[prefix.Length..].Contains(alias, StringComparison.OrdinalIgnoreCase))
            .Take(2).ToList();
        return matches.Count == 1 && _connectionStrings.TryGetValue(matches[0], out var match) ? match : null;
    }

    /// <summary>
    /// Starts a new Docker container for a given image or re-uses an existing running one.
    /// </summary>
    /// <param name="imageName">The Docker image name (e.g., 'mcr.microsoft.com/mssql/server').</param>
    /// <param name="alias">An optional alias to refer to this container instance.</param>
    /// <returns>The connection string for the started container.</returns>
    public async Task<string> StartContainer(string scope, string imageName, string? alias = null, CancellationToken cancellationToken = default)
    {
        var kind = ImageKind(imageName);
        string name = alias ?? imageName;
        string key = ScopedKey(scope, name);
        if (_activeContainers.TryGetValue(key, out var activeContainer) && activeContainer.State.ToString() == "Running")
        {
            _logger.Info("Using existing session Docker container for {Key}.", name);
            string activeConnStr = _connectionStrings.TryGetValue(key, out var cached) ? cached : GetConnectionString(activeContainer, imageName, null);
            _connectionStrings[key] = activeConnStr;
            Remember(scope, name, activeConnStr);
            return activeConnStr;
        }

        // Attempt to find an already running container on the system by name
        string containerName = ContainerName(scope, alias ?? imageName.Split('/').Last().Split(':').First());
        var credentials = GetCredentials(containerName, imageName);
        var existingConnStr = await GetExistingContainerConnectionString(containerName, imageName, credentials, cancellationToken);
        if (existingConnStr != null)
        {
            _logger.Info("Re-attached to existing Docker container: {ContainerName}", containerName);
            _connectionStrings[key] = existingConnStr;
            Remember(scope, name, existingConnStr);
            return existingConnStr;
        }

        IContainer container;

        if (kind == DatabaseImage.MsSql)
        {
            container = new MsSqlBuilder(imageName)
                .WithName(containerName)
                .WithPassword(credentials.Password)
                .WithCreateParameterModifier(BindPortsToLoopback)
                .WithLogger(_loggerFactory.CreateLogger<MsSqlBuilder>())
                .Build();
        }
        else if (kind == DatabaseImage.Postgres)
        {
            container = new PostgreSqlBuilder(imageName)
                .WithName(containerName)
                .WithUsername(credentials.Username)
                .WithPassword(credentials.Password)
                .WithDatabase(credentials.Database)
                .WithCreateParameterModifier(BindPortsToLoopback)
                .WithLogger(_loggerFactory.CreateLogger<PostgreSqlBuilder>())
                .Build();
        }
        else if (kind == DatabaseImage.Oracle)
        {
            container = new OracleBuilder(imageName)
                .WithName(containerName)
                .WithPassword(credentials.Password)
                .WithCreateParameterModifier(BindPortsToLoopback)
                .WithLogger(_loggerFactory.CreateLogger<OracleBuilder>())
                .Build();
        }
        else
        {
            container = new MySqlBuilder(imageName)
                .WithName(containerName)
                .WithUsername(credentials.Username)
                .WithPassword(credentials.Password)
                .WithDatabase(credentials.Database)
                .WithCreateParameterModifier(BindPortsToLoopback)
                .WithLogger(_loggerFactory.CreateLogger<MySqlBuilder>())
                .Build();
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await container.StartAsync(cancellationToken);
        }
        catch (Exception ex) when (ex.Message.Contains("already in use", StringComparison.OrdinalIgnoreCase))
        {
            // Final fallback if name exists but wasn't found by ListContainers
            var retryConnStr = await GetExistingContainerConnectionString(containerName, imageName, credentials, cancellationToken);
            if (retryConnStr != null)
            {
                _connectionStrings[key] = retryConnStr;
                Remember(scope, name, retryConnStr);
                return retryConnStr;
            }
            await DisposeBuiltContainerAsync(container, key);
            throw;
        }
        catch
        {
            await DisposeBuiltContainerAsync(container, key);
            throw;
        }

        _activeContainers[key] = container;

        string connectionString = GetConnectionString(container, imageName, credentials);
        _connectionStrings[key] = connectionString;
        Remember(scope, name, connectionString);
        return connectionString;
    }

    private async Task<Uri> GetValidDockerUri(CancellationToken cancellationToken)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Try common Windows pipes in order of prevalence
            var pipes = new[]
            {
                "npipe://./pipe/docker_engine",
                "npipe://./pipe/docker_desktop_linux",
                "npipe://./pipe/docker_desktop_windows"
            };

            foreach (var pipe in pipes)
            {
                try
                {
                    var uri = new Uri(pipe);
                    using var client = new DockerClientBuilder()
                        .WithEndpoint(uri)
                        .Build();
                    await client.System.PingAsync(cancellationToken); // Reliable check
                    return uri;
                }
                catch { /* Continue to next pipe */ }
            }
            return new Uri("npipe://./pipe/docker_engine"); // Fallback to default
        }
        return new Uri("unix:///var/run/docker.sock");
    }

    private async Task<string?> GetExistingContainerConnectionString(
        string name,
        string imageName,
        DockerCredentials? credentials,
        CancellationToken cancellationToken)
    {
        try
        {
            var uri = await GetValidDockerUri(cancellationToken);
            using var client = new DockerClientBuilder()
                .WithEndpoint(uri)
                .Build();
            var containers = await client.Containers.ListContainersAsync(new ContainersListParameters { All = false }, cancellationToken);
            var target = containers.FirstOrDefault(c => c.Names.Any(n => n.Equals("/" + name, StringComparison.OrdinalIgnoreCase)));

            if (target != null)
            {
                // Credentials are generated per start, so a container from an earlier session
                // carries its own: read them back from the environment it was created with.
                var inspected = await client.Containers.InspectContainerAsync(target.ID, cancellationToken);
                credentials = CredentialsFromEnvironment(imageName, credentials, inspected.Config?.Env);
                if (credentials is null) return null;

                var kind = ImageKind(imageName);
                int internalPort = kind switch { DatabaseImage.MsSql => 1433, DatabaseImage.Postgres => 5432, DatabaseImage.MySql => 3306, _ => 1521 };
                var portMap = target.Ports.FirstOrDefault(p => p.PrivatePort == internalPort);
                if (portMap != null)
                {
                    var host = "localhost";
                    var publicPort = portMap.PublicPort;

                    if (kind == DatabaseImage.MsSql)
                        return $"Server={host},{publicPort};Database=master;User Id=sa;Password={credentials.Password};Trusted_Connection=False;Encrypt=False;";
                    if (kind == DatabaseImage.Postgres)
                        return $"Host={host};Port={publicPort};Database={credentials.Database};Username={credentials.Username};Password={credentials.Password}";
                    if (kind == DatabaseImage.MySql)
                        return $"Server={host};Port={publicPort};Database={credentials.Database};User ID={credentials.Username};Password={credentials.Password}";
                    if (kind == DatabaseImage.Oracle)
                    {
                        if (imageName.Contains("free", StringComparison.OrdinalIgnoreCase))
                            return $"User Id=system;Password={credentials.Password};Data Source={host}:{publicPort}/FREE";
                        return $"User Id=oracle;Password={credentials.Password};Data Source={host}:{publicPort}/XE";
                    }
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Stops a running container by its alias or image name.
    /// </summary>
    /// <param name="alias">The alias of the container to stop.</param>
    public async Task StopContainer(string scope, string? alias, CancellationToken cancellationToken = default)
    {
        alias ??= GetLastAlias(scope);
        if (alias != null && _activeContainers.TryGetValue(ScopedKey(scope, alias), out var container))
        {
            _logger.Warning("Stopping Docker container {Alias}.", alias);
            await container.StopAsync(cancellationToken);
        }
    }

    public async Task PauseContainer(string scope, string? alias, CancellationToken cancellationToken = default)
    {
        alias ??= GetLastAlias(scope);
        if (alias != null && _activeContainers.TryGetValue(ScopedKey(scope, alias), out var container))
        {
            _logger.Warning("Pausing Docker container {Alias}.", alias);
            using var client = await CreateDockerClient(cancellationToken);
            await client.Containers.PauseContainerAsync(container.Id, cancellationToken);
        }
    }

    public async Task ResumeContainer(string scope, string? alias, CancellationToken cancellationToken = default)
    {
        alias ??= GetLastAlias(scope);
        if (alias != null && _activeContainers.TryGetValue(ScopedKey(scope, alias), out var container))
        {
            _logger.Info("Resuming Docker container {Alias}.", alias);
            using var client = await CreateDockerClient(cancellationToken);
            await client.Containers.UnpauseContainerAsync(container.Id, cancellationToken);
        }
    }

    private async Task<DockerClient> CreateDockerClient(CancellationToken cancellationToken)
    {
        var uri = await GetValidDockerUri(cancellationToken);
        return new DockerClientBuilder()
            .WithEndpoint(uri)
            .Build();
    }

    /// <summary>
    /// Stops and disposes of one or all active containers.
    /// </summary>
    /// <param name="nameOrAlias">The optional alias to target. If null, all containers are closed.</param>
    public async Task CloseContainers(string scope, string? nameOrAlias = null, CancellationToken cancellationToken = default)
    {
        var prefix = ScopePrefix(scope);
        var targets = _activeContainers
            .Where(c => c.Key.StartsWith(prefix, StringComparison.Ordinal)
                && (nameOrAlias == null || c.Key[prefix.Length..].Contains(nameOrAlias, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        foreach (var kvp in targets)
        {
            _logger.Warning("Closing Docker container for {ContainerKey}.", kvp.Key);
            try { await kvp.Value.StopAsync(cancellationToken); await kvp.Value.DisposeAsync(); }
            catch (Exception ex) { _logger.Debug("[DockerContainerManager] Container cleanup error for '{ContainerKey}': {Message}", kvp.Key, ex.Message); }
            _activeContainers.TryRemove(kvp.Key, out _);
            _connectionStrings.TryRemove(kvp.Key, out _);
        }
    }

    /// <summary>
    /// Extracts the connection string from a supported IContainer implementation.
    /// </summary>
    private static string GetConnectionString(IContainer container, string imageName, DockerCredentials? credentials)
    {
        if (container is MsSqlContainer mssql) return mssql.GetConnectionString();
        if (container is PostgreSqlContainer pg) return pg.GetConnectionString();
        if (container is MySqlContainer mysql) return mysql.GetConnectionString();
        if (container is OracleContainer oracle)
        {
            var port = oracle.GetMappedPublicPort(1521);
            var password = credentials?.Password ?? throw new ExecutionException("Oracle container credentials are unavailable.");
            if (imageName.Contains("free", StringComparison.OrdinalIgnoreCase))
                return $"User Id=system;Password={password};Data Source={oracle.Hostname}:{port}/FREE";
            return $"User Id=oracle;Password={password};Data Source={oracle.Hostname}:{port}/XE";
        }
        return "";
    }

    /// <summary>
    /// Publishes container ports on the loopback interface only. Testcontainers otherwise binds
    /// every interface, which puts a database with a generated password on the LAN.
    /// </summary>
    private static void BindPortsToLoopback(CreateContainerParameters parameters)
    {
        if (parameters.HostConfig?.PortBindings is null) return;
        foreach (var bindings in parameters.HostConfig.PortBindings.Values)
            foreach (var binding in bindings ?? [])
                binding.HostIP = "127.0.0.1";
    }

    /// <summary>
    /// Reads the credentials a container was created with from its environment. Returns null when
    /// they cannot be recovered, so the caller does not hand out a connection string that fails.
    /// </summary>
    internal static DockerCredentials? CredentialsFromEnvironment(string imageName, DockerCredentials? defaults, IEnumerable<string>? environment)
    {
        var env = (environment ?? [])
            .Select(e => e.Split('=', 2))
            .Where(p => p.Length == 2)
            .GroupBy(p => p[0], StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Last()[1], StringComparer.Ordinal);
        string? Get(string key) => env.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

        var kind = ImageKind(imageName);
        if (kind == DatabaseImage.MsSql)
            return Get("MSSQL_SA_PASSWORD") is { } sa ? new DockerCredentials("sa", sa, "master") : null;
        if (kind == DatabaseImage.Postgres)
            return Get("POSTGRES_PASSWORD") is { } pg
                ? new DockerCredentials(Get("POSTGRES_USER") ?? "postgres", pg, Get("POSTGRES_DB") ?? Get("POSTGRES_USER") ?? "postgres")
                : null;
        if (kind == DatabaseImage.MySql)
        {
            if (Get("MYSQL_USER") is { } user && Get("MYSQL_PASSWORD") is { } mysqlPassword)
                return new DockerCredentials(user, mysqlPassword, Get("MYSQL_DATABASE") ?? defaults?.Database ?? "etldb");
            return Get("MYSQL_ROOT_PASSWORD") is { } root
                ? new DockerCredentials("root", root, Get("MYSQL_DATABASE") ?? defaults?.Database ?? "etldb")
                : null;
        }
        return Get("ORACLE_PASSWORD") is { } oracle ? new DockerCredentials("oracle", oracle, "XE") : null;
    }

    public enum DatabaseImage { MsSql, Postgres, MySql, Oracle }

    /// <summary>
    /// Identifies the database an image runs from its repository name, ignoring registry, tag and
    /// digest. Only the official repositories are accepted: a name that merely contains
    /// <c>postgres</c> or <c>mssql</c> is refused.
    /// </summary>
    internal static DatabaseImage ImageKind(string imageName)
    {
        // COMPAT_BREAK: 0.22.0 — exact repository names replace the substring match.
        var repository = imageName.Split('@')[0];
        int lastSlash = repository.LastIndexOf('/');
        int tagColon = repository.IndexOf(':', lastSlash + 1);
        if (tagColon >= 0) repository = repository[..tagColon];
        var segments = repository.ToLowerInvariant().Split('/');
        var last = segments[^1];
        var parent = segments.Length > 1 ? segments[^2] : "";

        if (parent == "mssql" && last == "server") return DatabaseImage.MsSql;
        if (last == "postgres") return DatabaseImage.Postgres;
        if (last is "mysql" or "mariadb") return DatabaseImage.MySql;
        if (last is "oracle-xe" or "oracle-free" || parent == "database" && last is "free" or "express")
            return DatabaseImage.Oracle;
        throw new ExecutionException(
            $"Unsupported Docker image for database: {imageName}. Supported repositories: mssql/server, postgres, mysql, mariadb, oracle-xe, oracle-free.");
    }

    private static DockerCredentials GetCredentials(string containerName, string imageName)
    {
        var rawBytes = RandomNumberGenerator.GetBytes(16);
        var seed = Convert.ToHexString(rawBytes).ToLowerInvariant();
        var password = $"Et1!{seed}";

        var kind = ImageKind(imageName);
        if (kind is DatabaseImage.Postgres or DatabaseImage.MySql)
            return new DockerCredentials("etluser", password, "etldb");
        if (kind == DatabaseImage.MsSql)
            return new DockerCredentials("sa", password, "master");
        // Oracle images build the password into unquoted SQL, so keep it to letters and digits.
        return new DockerCredentials("oracle", $"Et1{seed[..24]}", "XE");
    }

    private async Task DisposeBuiltContainerAsync(IContainer container, string key)
    {
        try
        {
            await container.DisposeAsync();
        }
        catch (Exception ex)
        {
            _logger.Debug("[DockerContainerManager] Container disposal error for '{ContainerKey}': {Message}", key, ex.Message);
        }
    }

    internal sealed record DockerCredentials(string Username, string Password, string Database);

    public Dictionary<string, string> GetState(string scope)
    {
        var prefix = ScopePrefix(scope);
        return _connectionStrings
            .Where(kvp => kvp.Key.StartsWith(prefix, StringComparison.Ordinal))
            .ToDictionary(kvp => kvp.Key[prefix.Length..], kvp => kvp.Value, StringComparer.OrdinalIgnoreCase);
    }

    public void LoadState(string scope, Dictionary<string, string> connectionStrings, string? lastConnectionString)
    {
        foreach (var kvp in connectionStrings)
        {
            _connectionStrings[ScopedKey(scope, kvp.Key)] = kvp.Value;
        }
        if (lastConnectionString != null)
            _last.AddOrUpdate(scope, (null, lastConnectionString), (_, previous) => (previous.Alias, lastConnectionString));
    }

    public ValueTask DisposeAsync()
    {
        // Do not automatically close containers!
        // We want them to persist across multiple "Run" commands in the same session.
        return ValueTask.CompletedTask;
    }
}
