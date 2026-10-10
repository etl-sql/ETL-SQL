using System.Buffers.Binary;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ETL_SQL.PackageInspector;

internal sealed record BundleEntry(string Name, byte Type, long Offset, long Size, long CompressedSize);
internal sealed record BinaryInspection(string AssemblyName, string Version, string InformationalVersion,
    string Platform, string Sha256, long Size, IReadOnlyList<string> NativeLibraries, IReadOnlyList<string> Entries);

internal static class BundleInspector
{
    // Parse the runtime's documented single-file layout without executing a foreign apphost.
    // https://github.com/dotnet/runtime/blob/main/src/installer/managed/Microsoft.NET.HostModel/Bundle/Manifest.cs
    // https://github.com/dotnet/runtime/blob/main/src/native/corehost/apphost/bundle_marker.c
    public static async Task<BinaryInspection> InspectAsync(string file, string expectedName,
        string expectedVersion, string expectedPlatform, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        var length = stream.Length;
        var before = await SHA256.HashDataAsync(stream, cancellationToken);
        stream.Position = 0;
        var prefix = new byte[(int)Math.Min(length, 4096)];
        await stream.ReadExactlyAsync(prefix, cancellationToken);
        var platform = NativePlatform(prefix);
        if (platform != expectedPlatform) throw new InvalidDataException($"Binary architecture/platform is {platform}; expected {expectedPlatform}.");
        var headerOffset = await HeaderOffsetAsync(stream, cancellationToken);
        if (headerOffset <= 0 || headerOffset >= length) throw new InvalidDataException("Binary is not a supported single-file bundle.");
        stream.Position = headerOffset;
        var header = new byte[(int)Math.Min(length - headerOffset, 1024 * 1024)];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var entries = ReadManifest(header, headerOffset);
        var main = entries.SingleOrDefault(entry => entry.Name == expectedName + ".dll" && entry.Type == 1)
            ?? throw new InvalidDataException($"Bundle is missing the main assembly {expectedName}.dll.");
        var assemblyBytes = await ReadEntryAsync(stream, main, 32 * 1024 * 1024, cancellationToken);
        var (assemblyName, informationalVersion) = ReadAssembly(assemblyBytes);
        var version = informationalVersion.Split('+', 2)[0];
        if (assemblyName != expectedName || version != expectedVersion)
            throw new InvalidDataException($"Bundled assembly is {assemblyName} {version}; expected {expectedName} {expectedVersion}.");
        var runtimeConfig = entries.SingleOrDefault(entry => entry.Name == expectedName + ".runtimeconfig.json" && entry.Type == 4)
            ?? throw new InvalidDataException("Bundle is missing its runtime configuration.");
        using var runtime = JsonDocument.Parse(await ReadEntryAsync(stream, runtimeConfig, 16 * 1024 * 1024, cancellationToken));
        if (!runtime.RootElement.TryGetProperty("runtimeOptions", out var runtimeOptions) ||
            runtimeOptions.TryGetProperty("framework", out _) || runtimeOptions.TryGetProperty("frameworks", out _) ||
            !runtimeOptions.TryGetProperty("includedFrameworks", out var frameworks) ||
            frameworks.ValueKind != JsonValueKind.Array || frameworks.GetArrayLength() == 0)
            throw new InvalidDataException("Bundled tool is not self-contained.");
        var depsEntry = entries.SingleOrDefault(entry => entry.Name == expectedName + ".deps.json" && entry.Type == 3)
            ?? throw new InvalidDataException("Bundle is missing its dependency configuration.");
        using var deps = JsonDocument.Parse(await ReadEntryAsync(stream, depsEntry, 16 * 1024 * 1024, cancellationToken));
        if (!deps.RootElement.TryGetProperty("runtimeTarget", out var runtimeTarget) ||
            !runtimeTarget.TryGetProperty("name", out var targetName) || targetName.ValueKind != JsonValueKind.String ||
            targetName.GetString()?.EndsWith("/" + platform, StringComparison.Ordinal) != true)
            throw new InvalidDataException("Bundled dependency target disagrees with the native architecture.");
        // CoreCLR is statically linked into the SDK's singlefilehost on these targets.
        // NativeLibraries reports additional bundled bindings, such as SQLite, not CoreCLR.
        // https://github.com/dotnet/runtime/blob/main/src/native/corehost/apphost/static/CMakeLists.txt
        var managedRuntime = entries.SingleOrDefault(entry => entry.Name == "System.Private.CoreLib.dll");
        if (managedRuntime?.Type != 1)
            throw new InvalidDataException("Self-contained bundle is missing its managed .NET runtime.");
        stream.Position = 0;
        var after = await SHA256.HashDataAsync(stream, cancellationToken);
        if (stream.Length != length || !before.AsSpan().SequenceEqual(after))
            throw new InvalidDataException("Binary changed during inspection.");
        return new BinaryInspection(assemblyName, version, informationalVersion, platform,
            Convert.ToHexStringLower(after), length, entries.Where(entry => entry.Type == 2).Select(entry => entry.Name).ToArray(),
            entries.Select(entry => entry.Name).ToArray());
    }

    private static string NativePlatform(byte[] prefix)
    {
        if (prefix.Length < 64) throw new InvalidDataException("Truncated native binary.");
        if (prefix[0] == 'M' && prefix[1] == 'Z')
        {
            var offset = BinaryPrimitives.ReadInt32LittleEndian(prefix.AsSpan(60, 4));
            if (offset < 64 || offset > prefix.Length - 24 ||
                BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(offset, 4)) != 0x00004550 ||
                BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(offset + 4, 2)) != 0x8664)
                throw new InvalidDataException("Unsupported or invalid PE architecture.");
            return "win-x64";
        }
        if (prefix[0] == 0x7f && prefix[1] == 'E' && prefix[2] == 'L' && prefix[3] == 'F')
        {
            if (prefix[4] != 2 || prefix[5] != 1 || BinaryPrimitives.ReadUInt16LittleEndian(prefix.AsSpan(18, 2)) != 62)
                throw new InvalidDataException("Unsupported ELF architecture.");
            return "linux-x64";
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(0, 4)) == 0xfeedfacf)
        {
            return BinaryPrimitives.ReadUInt32LittleEndian(prefix.AsSpan(4, 4)) switch
            {
                0x01000007 => "osx-x64",
                0x0100000c => "osx-arm64",
                _ => throw new InvalidDataException("Unsupported Mach-O architecture.")
            };
        }
        throw new InvalidDataException("Unsupported native binary format.");
    }

    private static async Task<long> HeaderOffsetAsync(FileStream stream, CancellationToken cancellationToken)
    {
        // The published runtime format fixes these bytes. Its source comment about hashing a
        // string is not a serialization contract; use the marker written by the actual apphost.
        var marker = Convert.FromHexString("8B1202B96A612038727B930214D7A03213F5B9E6EFAE3318EE3B2DCE24B36AAE");
        const int chunk = 1024 * 1024;
        var buffer = new byte[chunk + 40];
        var carry = 0;
        stream.Position = 0;
        while (true)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(carry, chunk), cancellationToken);
            if (read == 0) throw new InvalidDataException("Native binary has no bundle marker.");
            var count = carry + read;
            var index = buffer.AsSpan(0, count).IndexOf(marker);
            if (index >= 0)
            {
                if (index < 8) throw new InvalidDataException("Invalid native bundle marker.");
                return BinaryPrimitives.ReadInt64LittleEndian(buffer.AsSpan(index - 8, 8));
            }
            carry = Math.Min(40, count);
            buffer.AsSpan(count - carry, carry).CopyTo(buffer);
        }
    }

    private static IReadOnlyList<BundleEntry> ReadManifest(byte[] header, long headerOffset)
    {
        using var reader = new BinaryReader(new MemoryStream(header, writable: false), new UTF8Encoding(false, true));
        if (reader.ReadUInt32() != 6 || reader.ReadUInt32() != 0)
            throw new InvalidDataException("Unsupported single-file bundle format; expected version 6.0.");
        var count = reader.ReadInt32();
        if (count is < 1 or > 10000) throw new InvalidDataException("Invalid bundle entry count.");
        _ = ReadString(reader); // Extraction identifier.
        var depsOffset = reader.ReadInt64();
        var depsSize = reader.ReadInt64();
        var runtimeOffset = reader.ReadInt64();
        var runtimeSize = reader.ReadInt64();
        if (reader.ReadUInt64() != 0) throw new InvalidDataException("Unsupported bundle extraction mode.");
        var entries = new List<BundleEntry>(count);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < count; i++)
        {
            var offset = reader.ReadInt64();
            var size = reader.ReadInt64();
            var compressed = reader.ReadInt64();
            var type = reader.ReadByte();
            var name = ReadString(reader);
            var stored = compressed == 0 ? size : compressed;
            if (offset < 0 || size < 0 || compressed < 0 || offset > headerOffset || stored > headerOffset - offset ||
                type > 5 || name.Contains('\\') || name.Contains(':') ||
                name.Split('/').Any(segment => segment is "" or "." or "..") || !names.Add(name))
                throw new InvalidDataException("Invalid, unsafe, or duplicate bundle entry.");
            entries.Add(new BundleEntry(name, type, offset, size, compressed));
        }
        if (!entries.Any(entry => entry.Type == 3 && entry.Offset == depsOffset && entry.Size == depsSize) ||
            !entries.Any(entry => entry.Type == 4 && entry.Offset == runtimeOffset && entry.Size == runtimeSize))
            throw new InvalidDataException("Bundle configuration locations disagree with the manifest.");
        return entries;
    }

    private static string ReadString(BinaryReader reader)
    {
        var length = reader.Read7BitEncodedInt();
        if (length is < 1 or > 4096) throw new InvalidDataException("Invalid bundle string length.");
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new InvalidDataException("Truncated bundle string.");
        return new UTF8Encoding(false, true).GetString(bytes);
    }

    private static async Task<byte[]> ReadEntryAsync(FileStream stream, BundleEntry entry, int limit, CancellationToken cancellationToken)
    {
        if (entry.Size is < 1 || entry.Size > limit || entry.CompressedSize > limit)
            throw new InvalidDataException("Invalid or oversized tool metadata.");
        stream.Position = entry.Offset;
        var bytes = new byte[(int)entry.Size];
        if (entry.CompressedSize == 0)
        {
            await stream.ReadExactlyAsync(bytes, cancellationToken);
        }
        else
        {
            var compressed = new byte[(int)entry.CompressedSize];
            await stream.ReadExactlyAsync(compressed, cancellationToken);
            await using var inflater = new DeflateStream(new MemoryStream(compressed, writable: false), CompressionMode.Decompress);
            await inflater.ReadExactlyAsync(bytes, cancellationToken);
            if (await inflater.ReadAsync(new byte[1], cancellationToken) != 0)
                throw new InvalidDataException("Compressed metadata exceeds its declared size.");
        }
        return bytes;
    }

    private static (string Name, string Version) ReadAssembly(byte[] bytes)
    {
        using var pe = new PEReader(new MemoryStream(bytes, writable: false));
        if (!pe.HasMetadata) throw new InvalidDataException("Bundled main assembly has no managed metadata.");
        var metadata = pe.GetMetadataReader();
        var definition = metadata.GetAssemblyDefinition();
        var versions = new List<string>();
        foreach (var handle in definition.GetCustomAttributes())
        {
            var attribute = metadata.GetCustomAttribute(handle);
            if (attribute.Constructor.Kind != HandleKind.MemberReference) continue;
            var constructor = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
            if (constructor.Parent.Kind != HandleKind.TypeReference) continue;
            var type = metadata.GetTypeReference((TypeReferenceHandle)constructor.Parent);
            if (metadata.GetString(type.Namespace) != "System.Reflection" ||
                metadata.GetString(type.Name) != "AssemblyInformationalVersionAttribute") continue;
            var value = metadata.GetBlobReader(attribute.Value);
            if (value.ReadUInt16() != 1) throw new InvalidDataException("Invalid assembly version attribute.");
            versions.Add(value.ReadSerializedString() ?? throw new InvalidDataException("Missing informational version."));
        }
        if (versions.Count != 1) throw new InvalidDataException("Expected one assembly informational version.");
        return (metadata.GetString(definition.Name), versions[0]);
    }
}
