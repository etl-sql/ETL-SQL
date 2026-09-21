using ETL_SQL.Core.Common;
using Xunit;

namespace ETL_SQL.Tests.Reporting;

public class AsyncEncryptionOptionsTests
{
    [Theory]
    [InlineData("OFF", false)]
    [InlineData("OFF", true)]
    [InlineData("PASSWORD", false)]
    [InlineData("PASSWORD", true)]
    [InlineData("MACHINE", false)]
    [InlineData("MACHINE", true)]
    [InlineData("KEYFILE", false)]
    [InlineData("KEYFILE", true)]
    public async Task CancelledOperation_DoesNotOpenFiles(string mode, bool decrypt)
    {
        var options = new EncryptionOptions(new Dictionary<string, string>
        {
            ["ENCRYPT"] = mode,
            ["PASSWORD"] = "test-password",
            ["KEYFILE"] = mode == "KEYFILE" ? "missing-test-key.pem" : ""
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var missingInput = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "missing.bin");
        var output = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => decrypt
            ? options.DecryptFileAsync(missingInput, output, cancellation.Token)
            : options.EncryptFileAsync(missingInput, output, cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.False(File.Exists(output));
    }

    [Fact]
    public async Task DisabledEncryption_CopiesAndOverwritesBothDirections()
    {
        var root = Path.Combine(Path.GetTempPath(), "etlsql-async-copy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var input = Path.Combine(root, "input.bin");
            var output = Path.Combine(root, "output.bin");
            var restored = Path.Combine(root, "restored.bin");
            byte[] expected = [1, 2, 3];
            await File.WriteAllBytesAsync(input, expected);
            await File.WriteAllBytesAsync(output, new byte[100]);
            await File.WriteAllBytesAsync(restored, new byte[100]);
            var options = new EncryptionOptions(null);

            await options.EncryptFileAsync(input, output);
            await options.DecryptFileAsync(output, restored);

            Assert.Equal(expected, await File.ReadAllBytesAsync(output));
            Assert.Equal(expected, await File.ReadAllBytesAsync(restored));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
