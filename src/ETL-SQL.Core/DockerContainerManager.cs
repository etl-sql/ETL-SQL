using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Testcontainers.Oracle;
using Testcontainers.MySql;
using Microsoft.Extensions.Logging;
using DotNet.Testcontainers.Configurations;
using System.Linq;
using ETL_SQL.Core.Common.Exceptions;
using Docker.DotNet;
using Docker.DotNet.Models;
using System.Runtime.InteropServices;
using ETL_SQL.Common;

namespace ETL_SQL.Core
{
    public class DockerContainerManager : IDockerManager
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IContainer> _activeContainers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _connectionStrings = new(StringComparer.OrdinalIgnoreCase);
        public string? LastConnectionString { get; private set; }
        public string? LastAlias { get; private set; }
        public bool HasActiveContainers => !_activeContainers.IsEmpty;
        
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
        public string? GetConnectionString(string alias)
        {
            if (_connectionStrings.TryGetValue(alias, out var connStr)) return connStr;
            // Fallback to image name if alias is not found (backward compatibility)
            var existing = _activeContainers.Keys.FirstOrDefault(k => k.Contains(alias, StringComparison.OrdinalIgnoreCase) || alias.Equals("DOCKER", StringComparison.OrdinalIgnoreCase));
            return existing != null ? LastConnectionString : null;
        }

        /// <summary>
        /// Starts a new Docker container for a given image or re-uses an existing running one.
        /// </summary>
        /// <param name="imageName">The Docker image name (e.g., 'mcr.microsoft.com/mssql/server').</param>
        /// <param name="alias">An optional alias to refer to this container instance.</param>
        /// <returns>The connection string for the started container.</returns>
        public async Task<string> StartContainer(string imageName, string? alias = null)
        {
            string key = alias ?? imageName;
            if (_activeContainers.TryGetValue(key, out var activeContainer) && activeContainer.State.ToString() == "Running")
            {
                _logger.WriteLine($"Using existing session Docker container for {key}...", ConsoleColor.Cyan);
                string activeConnStr = GetConnectionString(activeContainer, imageName);
                _connectionStrings[key] = activeConnStr;
                LastConnectionString = activeConnStr;
                return activeConnStr;
            }

            // Attempt to find an already running container on the system by name
            string containerName = "etlsql_" + (alias ?? imageName.Split('/').Last().Split(':').First()).Replace(".", "_").Replace(":", "_");
            var existingConnStr = await GetExistingContainerConnectionString(containerName, imageName);
            if (existingConnStr != null)
            {
                _logger.WriteLine($"Re-attached to existing Docker container: {containerName}", ConsoleColor.Cyan);
                _connectionStrings[key] = existingConnStr;
                LastConnectionString = existingConnStr;
                return existingConnStr;
            }

            IContainer container;

            if (imageName.Contains("mssql", StringComparison.OrdinalIgnoreCase))
            {
                container = new MsSqlBuilder(imageName)
                    .WithName(containerName)
                    .WithPassword("Password123!")
                    .WithLogger(_loggerFactory.CreateLogger<MsSqlBuilder>())
                    .Build();
            }
            else if (imageName.Contains("postgres", StringComparison.OrdinalIgnoreCase))
            {
                container = new PostgreSqlBuilder(imageName)
                    .WithName(containerName)
                    .WithUsername("postgres")
                    .WithPassword("postgres")
                    .WithDatabase("postgres")
                    .WithLogger(_loggerFactory.CreateLogger<PostgreSqlBuilder>())
                    .Build();
            }
            else if (imageName.Contains("oracle", StringComparison.OrdinalIgnoreCase))
            {
                container = new OracleBuilder(imageName)
                    .WithName(containerName)
                    .WithLogger(_loggerFactory.CreateLogger<OracleBuilder>())
                    .Build();
            }
            else if (imageName.Contains("mysql", StringComparison.OrdinalIgnoreCase) || imageName.Contains("mariadb", StringComparison.OrdinalIgnoreCase))
            {
                container = new MySqlBuilder(imageName)
                    .WithName(containerName)
                    .WithUsername("mysql")
                    .WithPassword("mysql")
                    .WithDatabase("mysql")
                    .WithLogger(_loggerFactory.CreateLogger<MySqlBuilder>())
                    .Build();
            }
            else
            {
                throw new ExecutionException($"Unsupported Docker image for database: {imageName}. Currently supported: MsSql, Postgres, Oracle.");
            }

            try 
            {
                await container.StartAsync();
            }
            catch (Exception ex) when (ex.Message.Contains("already in use", StringComparison.OrdinalIgnoreCase))
            {
                // Final fallback if name exists but wasn't found by ListContainers
                var retryConnStr = await GetExistingContainerConnectionString(containerName, imageName);
                if (retryConnStr != null) return retryConnStr;
                throw;
            }

            _activeContainers[key] = container;

            string connectionString = GetConnectionString(container, imageName);
            _connectionStrings[key] = connectionString;
            LastConnectionString = connectionString;
            LastAlias = key;
            return connectionString;
        }

        private async Task<Uri> GetValidDockerUri()
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
                        using var config = new DockerClientConfiguration(uri);
                        using var client = config.CreateClient();
                        await client.System.PingAsync(); // Reliable check
                        return uri;
                    }
                    catch { /* Continue to next pipe */ }
                }
                return new Uri("npipe://./pipe/docker_engine"); // Fallback to default
            }
            return new Uri("unix:///var/run/docker.sock");
        }

        private async Task<string?> GetExistingContainerConnectionString(string name, string imageName)
        {
            try
            {
                var uri = await GetValidDockerUri();
                using var config = new DockerClientConfiguration(uri);
                using var client = config.CreateClient();
                var containers = await client.Containers.ListContainersAsync(new ContainersListParameters { All = false });
                var target = containers.FirstOrDefault(c => c.Names.Any(n => n.Equals("/" + name, StringComparison.OrdinalIgnoreCase)));
                
                if (target != null)
                {
                    int internalPort = imageName.Contains("mssql") ? 1433 : (imageName.Contains("postgres") ? 5432 : (imageName.Contains("mysql") || imageName.Contains("mariadb") ? 3306 : 1521));
                    var portMap = target.Ports.FirstOrDefault(p => p.PrivatePort == internalPort);
                    if (portMap != null)
                    {
                        var host = "localhost";
                        var publicPort = portMap.PublicPort;
                        
                        if (imageName.Contains("mssql"))
                            return $"Server={host},{publicPort};Database=master;User Id=sa;Password=Password123!;Trusted_Connection=False;Encrypt=False;";
                        if (imageName.Contains("postgres"))
                            return $"Host={host};Port={publicPort};Database=postgres;Username=postgres;Password=postgres";
                        if (imageName.Contains("mysql") || imageName.Contains("mariadb"))
                            return $"Server={host};Port={publicPort};Database=mysql;User ID=mysql;Password=mysql";
                        if (imageName.Contains("oracle"))
                        {
                             if (imageName.Contains("free", StringComparison.OrdinalIgnoreCase))
                                return $"User Id=system;Password=oracle;Data Source={host}:{publicPort}/FREE";
                            return $"User Id=oracle;Password=oracle;Data Source={host}:{publicPort}/XE";
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
        public async Task StopContainer(string? alias)
        {
            alias ??= LastAlias;
            if (alias != null && _activeContainers.TryGetValue(alias, out var container))
            {
                _logger.WriteLine($"Stopping Docker container {alias}...", ConsoleColor.Yellow);
                await container.StopAsync();
            }
        }

        public async Task PauseContainer(string? alias)
        {
            alias ??= LastAlias;
            if (alias != null && _activeContainers.TryGetValue(alias, out var container))
            {
                _logger.WriteLine($"Pausing Docker container {alias}...", ConsoleColor.Yellow);
                await container.StopAsync();
            }
        }

        public async Task ResumeContainer(string? alias)
        {
            alias ??= LastAlias;
            if (alias != null && _activeContainers.TryGetValue(alias, out var container))
            {
                _logger.WriteLine($"Resuming Docker container {alias}...", ConsoleColor.Green);
                await container.StartAsync();
            }
        }

        /// <summary>
        /// Stops and disposes of one or all active containers.
        /// </summary>
        /// <param name="nameOrAlias">The optional alias to target. If null, all containers are closed.</param>
        public async Task CloseContainers(string? nameOrAlias = null)
        {
            var targets = nameOrAlias != null 
                ? _activeContainers.Where(c => c.Key.Contains(nameOrAlias, StringComparison.OrdinalIgnoreCase)).ToList()
                : _activeContainers.ToList();

            foreach (var kvp in targets)
            {
                _logger.WriteLine($"Closing Docker container for {kvp.Key}...", ConsoleColor.Yellow);
                try { await kvp.Value.StopAsync(); await kvp.Value.DisposeAsync(); }
                catch (Exception ex) { _logger.Debug("[DockerContainerManager] Container cleanup error for '{ContainerKey}': {Message}", kvp.Key, ex.Message); }
                _activeContainers.TryRemove(kvp.Key, out _);
                _connectionStrings.TryRemove(kvp.Key, out _);
            }
        }

        /// <summary>
        /// Extracts the connection string from a supported IContainer implementation.
        /// </summary>
        private string GetConnectionString(IContainer container, string imageName)
        {
            if (container is MsSqlContainer mssql) return mssql.GetConnectionString();
            if (container is PostgreSqlContainer pg) return pg.GetConnectionString();
            if (container is MySqlContainer mysql) return mysql.GetConnectionString();
            if (container is OracleContainer oracle) 
            {
                var port = oracle.GetMappedPublicPort(1521);
                if (imageName.Contains("free", StringComparison.OrdinalIgnoreCase))
                    return $"User Id=system;Password=oracle;Data Source={oracle.Hostname}:{port}/FREE";
                return $"User Id=oracle;Password=oracle;Data Source={oracle.Hostname}:{port}/XE";
            }
            return "";
        }

        public Dictionary<string, string> GetState()
        {
            return new Dictionary<string, string>(_connectionStrings, StringComparer.OrdinalIgnoreCase);
        }

        public void LoadState(Dictionary<string, string> connectionStrings, string? lastConnectionString)
        {
            foreach (var kvp in connectionStrings)
            {
                _connectionStrings[kvp.Key] = kvp.Value;
            }
            if (lastConnectionString != null) LastConnectionString = lastConnectionString;
        }

        public ValueTask DisposeAsync()
        {
            // Do not automatically close containers! 
            // We want them to persist across multiple "Run" commands in the same session.
            return ValueTask.CompletedTask;
        }
    }
}
