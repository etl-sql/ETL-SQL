using System.Reflection;

string? generation = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>()
    .Single(attribute => attribute.Key == "FixtureGeneration").Value;
await Console.Out.WriteLineAsync($"XT08 Windows launch fixture generation {generation}");
