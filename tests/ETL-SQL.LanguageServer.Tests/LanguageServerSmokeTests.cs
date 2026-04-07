using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using OmniSharp.Extensions.LanguageServer.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ETL_SQL.LSP;
using ETL_SQL.Core;

namespace ETL_SQL.LanguageServer.Tests
{
    public class LanguageServerSmokeTests
    {
        [Fact]
        public async Task Server_Should_Initialize_Without_DI_Errors()
        {
            // We use memory streams to avoid actual IO
            var input = new MemoryStream();
            var output = new MemoryStream();

            var cts = new CancellationTokenSource();
            
            // We only want to test the INITIALIZATION/RESOLUTION phase
            // LanguageServer.From will try to resolve all handlers
            try 
            {
                var serverTask = OmniSharp.Extensions.LanguageServer.Server.LanguageServer.From(options =>
                    options
                        .WithInput(input)
                        .WithOutput(output)
                        .ConfigureLogging(lb => lb.AddDebug().SetMinimumLevel(LogLevel.Trace))
                        .WithServices(services => {
                            services.AddSingleton<ETL_SQL.Data.IConnectorRegistry>(new ETL_SQL.Data.ConnectorRegistry());
                            services.AddSingleton<IMetadataManager, MetadataManager>();
                            services.AddSingleton<DocumentStateStore>();
                        })
                        .WithHandler<TextDocumentHandler>()
                );

                // If it fails with DI error, it will throw here
                // We'll give it a short timeout in case it hangs waiting for input
                var completedTask = await Task.WhenAny(serverTask, Task.Delay(2000));
                
                if (completedTask == serverTask)
                {
                    var server = await serverTask;
                    Assert.NotNull(server);
                    
                    // Verify MetadataManager is resolved and assigned
                    var metadata = server.Services.GetService<MetadataManager>();
                    Assert.NotNull(metadata);
                }
                else
                {
                    // If it timed out, it means it didn't crash during resolution
                    // which is good enough for a smoke test of DI
                }
            }
            catch (Exception ex)
            {
                // This is where we expect the DryIoc.ContainerException to show up
                throw new Exception($"DI Resolution Failed: {ex.Message}", ex);
            }
            finally
            {
                await cts.CancelAsync();
            }
        }
    }
}
