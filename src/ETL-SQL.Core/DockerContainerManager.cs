using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DotNet.Testcontainers.Containers;
using Testcontainers.MsSql;
using Testcontainers.PostgreSql;
using Testcontainers.Oracle;
using Microsoft.Extensions.Logging;
using DotNet.Testcontainers.Configurations;
using System.Linq;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Core
{
    public class DockerContainerManager : IDockerManager
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IContainer> _activeContainers = new(StringComparer.OrdinalIgnoreCase);
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> _connectionStrings = new(StringComparer.OrdinalIgnoreCase);
        public string? LastConnectionString { get; private set; }
        private static readonly ILoggerFactory _loggerFactory = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddProvider(new TestcontainersLoggerProvider()));

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
                ETL_SQL.Common.Logger.WriteLine($"Using existing Docker container for {key}...", ConsoleColor.Cyan);
                string activeConnStr = GetConnectionString(activeContainer, imageName);
                _connectionStrings[key] = activeConnStr;
                LastConnectionString = activeConnStr;
                return activeConnStr;
            }

            IContainer container;

            if (imageName.Contains("mssql", StringComparison.OrdinalIgnoreCase))
            {
                container = new MsSqlBuilder(imageName)
                    .WithLogger(_loggerFactory.CreateLogger<MsSqlBuilder>())
                    .Build();
            }
            else if (imageName.Contains("postgres", StringComparison.OrdinalIgnoreCase))
            {
                container = new PostgreSqlBuilder(imageName)
                    .WithLogger(_loggerFactory.CreateLogger<PostgreSqlBuilder>())
                    .Build();
            }
            else if (imageName.Contains("oracle", StringComparison.OrdinalIgnoreCase))
            {
                container = new OracleBuilder(imageName)
                    .WithLogger(_loggerFactory.CreateLogger<OracleBuilder>())
                    .Build();
            }
            else
            {
                throw new ExecutionException($"Unsupported Docker image for database: {imageName}. Currently supported: MsSql, Postgres, Oracle.");
            }

            await container.StartAsync();
            _activeContainers[key] = container;

            string connectionString = GetConnectionString(container, imageName);
            _connectionStrings[key] = connectionString;
            LastConnectionString = connectionString;
            return connectionString;
        }

        /// <summary>
        /// Stops a running container by its alias or image name.
        /// </summary>
        /// <param name="alias">The alias of the container to stop.</param>
        public async Task StopContainer(string alias)
        {
            if (_activeContainers.TryGetValue(alias, out var container))
            {
                ETL_SQL.Common.Logger.WriteLine($"Stopping Docker container {alias}...", ConsoleColor.Yellow);
                await container.StopAsync();
            }
        }

        public async Task PauseContainer(string alias)
        {
            if (_activeContainers.TryGetValue(alias, out var container))
            {
                ETL_SQL.Common.Logger.WriteLine($"Pausing Docker container {alias}...", ConsoleColor.Yellow);
                // Testcontainers doesn't have a direct PauseAsync in basic IContainer, 
                // but some implementations might. We'll stick to Stop if Pause is not available,
                // or just log it for now if we want to add full docker exec support later.
                // Actually, let's just Stop/Start for now as a simple implementation of Pause/Resume
                // unless IContainer has it.
                await container.StopAsync();
            }
        }

        public async Task ResumeContainer(string alias)
        {
            if (_activeContainers.TryGetValue(alias, out var container))
            {
                ETL_SQL.Common.Logger.WriteLine($"Resuming Docker container {alias}...", ConsoleColor.Green);
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
                ETL_SQL.Common.Logger.WriteLine($"Closing Docker container for {kvp.Key}...", ConsoleColor.Yellow);
                try { await kvp.Value.StopAsync(); await kvp.Value.DisposeAsync(); }
                catch (Exception ex) { ETL_SQL.Common.Logger.Verbose($"[DockerContainerManager] Container cleanup error for '{kvp.Key}': {ex.Message}"); }
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
            if (container is OracleContainer oracle) 
            {
                var port = oracle.GetMappedPublicPort(1521);
                if (imageName.Contains("free", StringComparison.OrdinalIgnoreCase))
                    return $"User Id=system;Password=oracle;Data Source={oracle.Hostname}:{port}/FREE";
                return $"User Id=oracle;Password=oracle;Data Source={oracle.Hostname}:{port}/XE";
            }
            return "";
        }

        public async ValueTask DisposeAsync()
        {
            await CloseContainers();
        }
    }
}
