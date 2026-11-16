using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ETL_SQL.Core.Common.Exceptions;
using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Connectors;

/// <summary>
/// FTP and SFTP rename deleted an existing destination before renaming, so a missing source or a
/// failed rename lost the destination. The replacement now moves the old destination aside, renames,
/// and puts it back if the rename fails (ROADMAP RF10).
/// </summary>
public sealed class RemoteRenameTests
{
    private sealed class FakeServer
    {
        public Dictionary<string, string> Files { get; } = new();
        public Func<string, string, bool>? FailRename { get; set; }

        public Task<bool> Exists(string path) => Task.FromResult(Files.ContainsKey(path));

        public Task Rename(string from, string to)
        {
            if (FailRename?.Invoke(from, to) == true) throw new InvalidOperationException("550 rename refused");
            if (!Files.Remove(from, out var content)) throw new InvalidOperationException("550 no such file");
            Files[to] = content;
            return Task.CompletedTask;
        }

        public Task Delete(string path)
        {
            Files.Remove(path);
            return Task.CompletedTask;
        }

        public Task Run(string source, string dest, bool overwrite) =>
            RemoteRename.RunAsync("FTP", source, dest, overwrite, Exists, Rename, Delete);
    }

    [Fact]
    public async Task NewDestination_IsRenamed()
    {
        var server = new FakeServer { Files = { ["/in/a.csv"] = "new" } };
        await server.Run("/in/a.csv", "/out/a.csv", overwrite: true);
        Assert.Equal(new Dictionary<string, string> { ["/out/a.csv"] = "new" }, server.Files);
    }

    [Fact]
    public async Task MissingSource_LeavesTheDestinationAlone()
    {
        var server = new FakeServer { Files = { ["/out/a.csv"] = "old" } };
        var error = await Assert.ThrowsAsync<ExecutionException>(() => server.Run("/in/typo.csv", "/out/a.csv", overwrite: true));
        Assert.Contains("does not exist", error.Message);
        Assert.Equal("old", server.Files["/out/a.csv"]);
        Assert.Single(server.Files);
    }

    [Fact]
    public async Task ExistingDestination_WithOverwriteOff_Fails()
    {
        var server = new FakeServer { Files = { ["/in/a.csv"] = "new", ["/out/a.csv"] = "old" } };
        var error = await Assert.ThrowsAsync<ExecutionException>(() => server.Run("/in/a.csv", "/out/a.csv", overwrite: false));
        Assert.Contains("OVERWRITE is OFF", error.Message);
        Assert.Equal("old", server.Files["/out/a.csv"]);
        Assert.Equal("new", server.Files["/in/a.csv"]);
    }

    [Fact]
    public async Task ExistingDestination_IsReplaced_AndNoBackupIsLeft()
    {
        var server = new FakeServer { Files = { ["/in/a.csv"] = "new", ["/out/a.csv"] = "old" } };
        await server.Run("/in/a.csv", "/out/a.csv", overwrite: true);
        Assert.Equal(new Dictionary<string, string> { ["/out/a.csv"] = "new" }, server.Files);
    }

    [Fact]
    public async Task FailedRename_PutsTheOldDestinationBack()
    {
        var server = new FakeServer
        {
            Files = { ["/in/a.csv"] = "new", ["/out/a.csv"] = "old" },
            FailRename = (from, _) => from == "/in/a.csv",
        };
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => server.Run("/in/a.csv", "/out/a.csv", overwrite: true));
        Assert.Contains("rename refused", error.Message);
        Assert.Equal(new Dictionary<string, string> { ["/in/a.csv"] = "new", ["/out/a.csv"] = "old" }, server.Files);
    }

    [Fact]
    public async Task FailedRestore_NamesWhereTheOldDestinationIs()
    {
        var server = new FakeServer
        {
            Files = { ["/in/a.csv"] = "new", ["/out/a.csv"] = "old" },
            FailRename = (from, _) => from != "/out/a.csv",
        };
        var error = await Assert.ThrowsAsync<ExecutionException>(() => server.Run("/in/a.csv", "/out/a.csv", overwrite: true));
        var backup = Assert.Single(server.Files.Keys, k => k.StartsWith("/out/a.csv.etlsql-replace-", StringComparison.Ordinal));
        Assert.Contains(backup, error.Message);
        Assert.Equal("old", server.Files[backup]);
    }
}
