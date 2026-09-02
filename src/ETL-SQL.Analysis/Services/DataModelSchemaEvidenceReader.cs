using ETL_SQL.Core;

namespace ETL_SQL.Analysis.Services;

/// <summary>
/// Collects the declared keys and foreign keys for the tables a projected model already names.
///
/// <para>It reads the model rather than the script, and only the tables in it, for two reasons. A
/// database has far more relationships than any one script touches, and drawing all of them buries
/// the handful the author is working with. And a design-time view must not become a schema crawl:
/// the cost has to be bounded by the size of the script on screen, not by the size of the database
/// behind it.</para>
///
/// <para>Shared by every host, and the reason the data-model view answers the same way on the
/// desktop as it does in the Portal — both reach a catalog through the same
/// <see cref="IMetadataManager"/> they already register their connections with.</para>
/// </summary>
public static class DataModelSchemaEvidenceReader
{
    /// <summary>How many tables one projection will interrogate. A diagram is not a schema crawl.</summary>
    public const int MaxTables = 40;

    public static async Task<DataModelSchemaEvidence> ReadAsync(
        IMetadataManager? metadata,
        ScriptDataModel model,
        string? documentUri = null,
        CancellationToken cancellationToken = default)
    {
        if (metadata is null || !model.Parsed) return DataModelSchemaEvidence.None;

        var tables = model.Entities
            .Where(entity => entity.Kind == "table" && !string.IsNullOrWhiteSpace(entity.Connection))
            .Select(entity => (Connection: entity.Connection!, entity.Name))
            .Distinct()
            .Take(MaxTables)
            .ToList();
        if (tables.Count == 0) return DataModelSchemaEvidence.None;

        var keys = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        var foreignKeys = new List<DataModelForeignKey>();

        foreach (var (connection, table) in tables)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var evidence = await metadata.GetKeyEvidenceAsync(connection, table, documentUri);
            if (evidence.IsEmpty) continue;

            var qualified = $"{connection}.{table}";
            if (evidence.KeyColumns.Count > 0) keys[qualified] = evidence.KeyColumns;

            foreignKeys.AddRange(evidence.ForeignKeys.Select(relationship => new DataModelForeignKey(
                qualified,
                relationship.ForeignKeyColumn,
                // A catalog names the referenced table within its own database; the model qualifies
                // every table by the connection it was reached through, so the two have to be put
                // back together here or the edge points at an entity that does not exist.
                $"{connection}.{Unqualify(relationship.ReferencedTable)}",
                relationship.ReferencedColumn)));
        }

        return keys.Count == 0 && foreignKeys.Count == 0
            ? DataModelSchemaEvidence.None
            : new DataModelSchemaEvidence(keys, foreignKeys);
    }

    /// <summary>Drops a schema prefix, because the model keys tables by the name the script writes.</summary>
    private static string Unqualify(string tableName)
    {
        var dot = tableName.LastIndexOf('.');
        return dot < 0 ? tableName : tableName[(dot + 1)..];
    }
}
