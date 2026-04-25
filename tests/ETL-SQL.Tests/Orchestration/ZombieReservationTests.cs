using System;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.DependencyInjection;
using ETL_SQL.Core.Execution;
using ETL_SQL.Orchestrator.Execution;
using ETL_SQL.Engine;
using ETL_SQL.Engine.Services;
using ETL_SQL.Core;
using ETL_SQL.Data;
using ETL_SQL.Common;
using ETL_SQL.Services;
using ETL_SQL.Core.Spill;
using ETL_SQL.Core.Functions;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Moq;

// Fully qualify to avoid ambiguity between ETL_SQL.Common.ILogger and Microsoft.Extensions.Logging.ILogger
using IEngineLogger = ETL_SQL.Common.ILogger;

namespace ETL_SQL.Tests.Orchestration
{
    public class ZombieReservationTests
    {
        [Fact]
        public async Task EvaluatorDispose_ShouldReclaimUnreleasedReservations()
        {
            // 1. Setup DI and BufferManager
            var services = new ServiceCollection();
            var loggerMock = new Mock<ILogger<BufferManager>>();
            var options = Options.Create(new BufferManagerOptions 
            { 
                MaxGlobalMemoryMB = 10,
                HysteresisMemoryMB = 1
            });
            var mockSys = new Mock<ISystemResources>();
            mockSys.Setup(r => r.GetAvailableMemoryBytes()).Returns(32L * 1024 * 1024 * 1024);
            var bufferManager = new BufferManager(options, loggerMock.Object, mockSys.Object);
            services.AddSingleton<IBufferManager>(bufferManager);
            
            // Mocks for Evaluator dependencies
            var engineLoggerMock = new Mock<IEngineLogger>();
            var securityService = new SecurityService(engineLoggerMock.Object);
            var config = new ConfigurationBuilder().Build();
            var sessionStateManager = new SessionStateManager(engineLoggerMock.Object, securityService, config);
            
            services.AddSingleton(new Mock<IFunctionRegistry>().Object);
            services.AddSingleton(new Mock<ILineageTracker>().Object);
            services.AddSingleton(new Mock<IDockerManager>().Object);
            services.AddSingleton(new Mock<IConnectorRegistry>().Object);
            services.AddSingleton(sessionStateManager);
            services.AddSingleton(securityService);
            services.AddSingleton(engineLoggerMock.Object);
            services.AddSingleton(new Mock<ISpillStore>().Object);
            services.AddSingleton<IEnumerable<IStatementHandler>>(new List<IStatementHandler>());
            
            var provider = services.BuildServiceProvider();
            
            string sessionId = "zombie-test-session";
            
            // 2. Create Evaluator
            var evaluator = new Evaluator(
                provider.GetServices<IStatementHandler>(),
                provider,
                provider.GetRequiredService<IFunctionRegistry>(),
                provider.GetRequiredService<ILineageTracker>(),
                provider.GetRequiredService<IDockerManager>(),
                provider.GetRequiredService<IConnectorRegistry>(),
                provider.GetRequiredService<SessionStateManager>(),
                provider.GetRequiredService<SecurityService>(),
                provider.GetRequiredService<IEngineLogger>(),
                new ETL_SQL.Core.Metadata.LanguageHelpRegistry(),
                new EvaluatorComponentRegistry())
            {
                SessionId = sessionId
            };

            // 3. Leak some resources
            await bufferManager.ReserveMemoryAsync(sessionId, 5 * 1024 * 1024); // 5MB leaked
            await bufferManager.AcquireCursorAsync(sessionId); // 1 cursor leaked
            
            // 4. Dispose the Evaluator (this should trigger ReleaseAllForSession)
            await evaluator.DisposeAsync();

            // 5. Verify the resources are free by reserving the full amount again
            var reserveTask = bufferManager.ReserveMemoryAsync("other", 10 * 1024 * 1024);
            var completedTask = await Task.WhenAny(reserveTask, Task.Delay(2000));
            
            Assert.Equal(reserveTask, completedTask); // Should have completed immediately because 10MB is free now
        }
 
        [Fact]
        public async Task AutomaticReclamation_ShouldFreeResourcesWhenOwnerIsGCed()
        {
            // 1. Setup
            var loggerMock = new Mock<ILogger<BufferManager>>();
            var options = Options.Create(new BufferManagerOptions { MaxGlobalMemoryMB = 10 });
            var mockSys = new Mock<ISystemResources>();
            mockSys.Setup(r => r.GetAvailableMemoryBytes()).Returns(32L * 1024 * 1024 * 1024);
            var bufferManager = new BufferManager(options, loggerMock.Object, mockSys.Object);
            
            // 2. Reserve with an owner in a separate scope to ensure GC can collect it
            WeakReference weakRef = await CreateLeakedReservation(bufferManager, "gc-test");
 
            // 3. Force GC
            for (int i = 0; i < 3; i++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
            }
 
            Assert.False(weakRef.IsAlive, "Owner should have been GCed");
 
            // 4. Trigger the zombie sweep manually
            bufferManager.PruneZombies();
 
            // 5. Verify 8MB is free again
            var reserveTask = bufferManager.ReserveMemoryAsync("other", 8 * 1024 * 1024);
            var completedTask = await Task.WhenAny(reserveTask, Task.Delay(2000));
 
            Assert.Equal(reserveTask, completedTask); 
        }
 
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private async Task<WeakReference> CreateLeakedReservation(BufferManager bm, string sessionId)
        {
            object owner = new object();
            var wr = new WeakReference(owner);
            await bm.ReserveMemoryAsync(sessionId, 8 * 1024 * 1024, owner: owner);
            return wr;
        }
    }
}
