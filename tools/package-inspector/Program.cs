using System.Text.Json;

namespace ETL_SQL.PackageInspector;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        try
        {
            if (args.Length != 4 || !Path.IsPathFullyQualified(args[0]))
                throw new ArgumentException("Usage: package-inspector <absolute binary path> <assembly name> <version> <RID>.");
            var result = await BundleInspector.InspectAsync(args[0], args[1], args[2], args[3], cancellation.Token);
            await JsonSerializer.SerializeAsync(Console.OpenStandardOutput(), result, options, cancellation.Token);
            return 0;
        }
        catch (Exception error) when (error is ArgumentException or InvalidDataException or IOException
            or BadImageFormatException or JsonException or OperationCanceledException or UnauthorizedAccessException)
        {
            await JsonSerializer.SerializeAsync(Console.OpenStandardError(), new { error = error.Message }, options, CancellationToken.None);
            return 1;
        }
    }
}
