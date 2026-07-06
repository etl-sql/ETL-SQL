using ETL_SQL.ReportPortal.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ETL_SQL.ReportPortal.Tests;

public sealed class ReportCatalogScaleAssessmentTests
{
    [Theory]
    [InlineData(20)]
    [InlineData(100)]
    [InlineData(1000)]
    [Trait("Category", "ScaleAssessment")]
    public async Task FolderCatalogIsBoundedUntrackedAndIndexed(int reportCount)
    {
        var path = Path.Combine(Path.GetTempPath(), $"portal-catalog-scale-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<PortalDbContext>()
            .UseSqlite($"Data Source={path}")
            .Options;
        try
        {
            await using var db = new PortalDbContext(options);
            await db.Database.EnsureCreatedAsync();
            var folder = new Folder { Name = "Scale", Path = "/Scale", OwnerId = 1 };
            db.Folders.Add(folder);
            await db.SaveChangesAsync();

            db.Reports.AddRange(Enumerable.Range(0, reportCount).Select(i => new Report
            {
                FolderId = folder.Id,
                Name = $"Report {i:D4}",
                ScriptPath = $"report-{i:D4}.rptsql",
                ScriptLastModified = DateTime.UtcNow,
                CreatedBy = 1
            }));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var page = await db.Reports
                .AsNoTracking()
                .Where(report => report.FolderId == folder.Id && !report.IsDeleted)
                .OrderBy(report => report.Name)
                .Take(100)
                .Select(report => new { report.Id, report.Name })
                .ToListAsync();

            Assert.Equal(Math.Min(100, reportCount), page.Count);
            Assert.Empty(db.ChangeTracker.Entries());
            Assert.Contains("IX_Reports_FolderId", await QueryPlanAsync(path, folder.Id));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            try { File.Delete(path); } catch (IOException) { }
            try { File.Delete(path + "-wal"); } catch (IOException) { }
            try { File.Delete(path + "-shm"); } catch (IOException) { }
        }
    }

    private static async Task<string> QueryPlanAsync(string path, int folderId)
    {
        await using var connection = new SqliteConnection($"Data Source={path}");
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "EXPLAIN QUERY PLAN SELECT Id, Name FROM Reports WHERE FolderId = $folder AND IsDeleted = 0 ORDER BY Name LIMIT 100";
        command.Parameters.AddWithValue("$folder", folderId);
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) lines.Add(reader.GetString(3));
        return string.Join(Environment.NewLine, lines);
    }
}
