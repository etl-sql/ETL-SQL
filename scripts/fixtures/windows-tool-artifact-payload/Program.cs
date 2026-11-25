using System.Reflection;
using System.Text.Json;

string? generation = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "FixtureGeneration").Value;
var marker = $"XT08 Windows launch fixture generation {generation}";
await Console.Out.WriteLineAsync(args is ["--json"] ? JsonSerializer.Serialize(new { marker }) : marker);
