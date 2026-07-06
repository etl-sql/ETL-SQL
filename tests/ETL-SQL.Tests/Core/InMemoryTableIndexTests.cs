using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

public sealed class InMemoryTableIndexTests
{
    [Fact]
    public void UniqueIndexStoresCompactKeysWithoutRowLists()
    {
        var index = new InMemoryTableIndex();
        index.AddIndexDefinition("id", new() { "id" }, isUnique: true);
        var table = new DataTable();
        table.SetColumns(new[] { "id", "payload" });
        for (var i = 0; i < 3; i++)
        {
            var row = table.NewRow();
            row["id"] = i;
            row["payload"] = new string('x', 1_000);
            table.Rows.Add(row);
        }

        index.RebuildIndex(new[] { "id" }, new[] { table });

        Assert.True(index.ContainsKey("id", 1));
        Assert.Null(index.Lookup("id", 1));
        Assert.Equal(3 * (32 + Row.EstimateValueBytes(1)), index.EstimatedUniqueKeyBytes);
    }

    [Fact]
    public void ClearDataCanPreserveOrRemoveUniqueKeysWithoutDroppingDefinition()
    {
        var index = new InMemoryTableIndex();
        index.AddIndexDefinition("id", new() { "id" }, isUnique: true);
        var table = new DataTable();
        table.SetColumns(new[] { "id" });
        var row = table.NewRow();
        row["id"] = 7;
        table.Rows.Add(row);
        index.RebuildIndex(new[] { "id" }, new[] { table });

        index.ClearData(preserveUniqueKeys: true);
        Assert.True(index.HasIndex("id"));
        Assert.True(index.ContainsKey("id", 7));

        index.ClearData();
        Assert.True(index.HasIndex("id"));
        Assert.False(index.ContainsKey("id", 7));
    }
}
