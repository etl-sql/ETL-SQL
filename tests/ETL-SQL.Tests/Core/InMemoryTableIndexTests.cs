using ETL_SQL.Data;
using Xunit;

namespace ETL_SQL.Tests.Core;

[Trait("CompatBreak", "0.21.0")]
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

    [Fact]
    public void ASingleColumnUniqueIndexAllowsManyNullsHoweverTheyArrive()
    {
        var index = new InMemoryTableIndex();
        index.AddIndexDefinition("id", new() { "id" }, isUnique: true);
        var table = new DataTable();
        table.SetColumns(new[] { "id" });
        foreach (var value in new object?[] { DBNull.Value, DBNull.Value, null, 1 })
        {
            var row = table.NewRow();
            row["id"] = value;
            table.Rows.Add(row);
        }

        index.RebuildIndex(new[] { "id" }, new[] { table });

        Assert.True(index.ContainsKey("id", 1));
        Assert.False(index.ContainsKey("id", DBNull.Value));
        Assert.False(index.IsDuplicate(new[] { "id" }, table.Rows[0], new[] { table }));
    }

    [Theory]
    [InlineData("b", "z")] // stays second in key order
    [InlineData("b", "0")] // moves ahead of "a", so the composite key is reordered
    public void RenamingAColumnCarriesItsSingleAndCompositeIndexes(string oldName, string newName)
    {
        var index = new InMemoryTableIndex();
        index.AddIndexDefinition(index.GetIndexKey(new[] { "b" }), new() { "b" }, isUnique: true);
        index.AddIndexDefinition(index.GetIndexKey(new[] { "a", "b" }), new() { "a", "b" }, isUnique: true);
        var table = new DataTable();
        table.SetColumns(new[] { "a", "b" });
        var row = table.NewRow();
        row["a"] = 1;
        row["b"] = 2;
        table.Rows.Add(row);
        index.RebuildIndex(new[] { "b" }, new[] { table });
        index.RebuildIndex(new[] { "a", "b" }, new[] { table });

        var renamed = index.RenameColumn(oldName, newName);
        table.RenameColumn(oldName, newName);
        foreach (var columns in renamed) index.RebuildIndex(columns, new[] { table });

        Assert.Equal(2, renamed.Count);
        Assert.False(index.HasIndex("b"));
        Assert.False(index.HasIndex("a,b"));
        var single = index.GetIndexKey(new[] { newName });
        var composite = index.GetIndexKey(new[] { "a", newName });
        Assert.True(index.IsUnique(single));
        Assert.True(index.IsUnique(composite));
        Assert.True(index.TryGetColumns(composite, out var stored));
        Assert.Contains(newName, stored!);
        Assert.DoesNotContain(oldName, stored!);

        // The keys are findable under the new name, and a duplicate is still a duplicate.
        Assert.True(index.ContainsKey(single, 2));
        Assert.True(index.ContainsKey(composite, index.GetRowKey(new[] { "a", newName }, row)!));
        var duplicate = new DataTable();
        duplicate.SetColumns(new[] { "a", newName });
        var copy = duplicate.NewRow();
        copy["a"] = 1;
        copy[newName] = 2;
        duplicate.Rows.Add(copy);
        Assert.Throws<ETL_SQL.Core.Common.Exceptions.ExecutionException>(
            () => index.UpdateIndexWithBatch(new[] { "a", newName }, duplicate));
    }
}
