using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using ETL_SQL.Core.Common;

namespace ETL_SQL.TUI.UI
{
    /// <summary>
    /// Demonstrates the graphical execution tree with simulated parallel branches.
    /// </summary>
    public static class ExecuteTreeDemoRunner
    {
        public static async Task RunDemoAsync()
        {
            var tree = new ExecutionTree();

            // Root
            var script = new ExecutionNode { Name = "Main Pipeline", Status = ExecutionStatus.Running, StartTicks = Stopwatch.GetTimestamp() };
            tree.AddNode(script);

            // Level 1
            var loadStep = new ExecutionNode { Name = "Intake Stage", Status = ExecutionStatus.Running, StartTicks = Stopwatch.GetTimestamp() };
            tree.AddNode(loadStep, script.Id);

            // Parallel Branches
            var branchA = new ExecutionNode { Name = "Branch A: Postgres Import", Status = ExecutionStatus.Waiting };
            var branchB = new ExecutionNode { Name = "Branch B: S3 Archive", Status = ExecutionStatus.Waiting };
            tree.AddNode(branchA, loadStep.Id);
            tree.AddNode(branchB, loadStep.Id);

            var visualizer = new ExecuteTreeVisualizer(tree);
            var cts = new CancellationTokenSource();

            // Start visualizer task
            var renderTask = visualizer.RenderLiveAsync(cts.Token);

            // Simulate execution
            await Task.Delay(1000);

            // Start branches in parallel
            branchA.Status = ExecutionStatus.Running;
            branchA.StartTicks = Stopwatch.GetTimestamp();
            branchB.Status = ExecutionStatus.Running;
            branchB.StartTicks = Stopwatch.GetTimestamp();

            var simA = Task.Run(async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    branchA.IncrementRows(new Random().Next(100, 500));
                    await Task.Delay(150);
                }
                branchA.Status = ExecutionStatus.Completed;
                branchA.EndTicks = Stopwatch.GetTimestamp();
            });

            var simB = Task.Run(async () =>
            {
                for (int i = 0; i < 30; i++)
                {
                    branchB.IncrementRows(new Random().Next(50, 200));
                    await Task.Delay(250);
                }
                branchB.Status = ExecutionStatus.Completed;
                branchB.EndTicks = Stopwatch.GetTimestamp();
            });

            await Task.WhenAll(simA, simB);

            loadStep.Status = ExecutionStatus.Completed;
            loadStep.EndTicks = Stopwatch.GetTimestamp();
            script.Status = ExecutionStatus.Completed;
            script.EndTicks = Stopwatch.GetTimestamp();

            await Task.Delay(500);
            cts.Cancel();
            await renderTask;

            Console.WriteLine("\n[Done] Demo complete.");
        }
    }
}
