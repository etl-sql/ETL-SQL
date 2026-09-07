using System.Runtime.CompilerServices;
using System.Text;
using ETL_SQL.Core.Common.Exceptions;

namespace ETL_SQL.Engine.Handlers;

internal static class BoundedToolOutput
{
    public static async IAsyncEnumerable<string> LinesAsync(StreamReader reader, int maxLineChars, long maxBytes,
        [EnumeratorCancellation] CancellationToken token)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        var encoder = Encoding.UTF8.GetEncoder();
        long bytes = 0;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            bytes += encoder.GetByteCount(buffer, 0, count, flush: false);
            if (bytes > maxBytes) throw new ExecutionException($"Tool output exceeded {maxBytes} bytes.");
            for (var i = 0; i < count; i++)
            {
                if (buffer[i] == '\n')
                {
                    yield return line.ToString().TrimEnd('\r');
                    line.Clear();
                }
                else
                {
                    if (line.Length >= maxLineChars)
                        throw new ExecutionException($"Tool output line exceeded {maxLineChars} characters.");
                    line.Append(buffer[i]);
                }
            }
        }
        bytes += encoder.GetByteCount(Array.Empty<char>(), 0, 0, flush: true);
        if (bytes > maxBytes) throw new ExecutionException($"Tool output exceeded {maxBytes} bytes.");
        if (line.Length > 0) yield return line.ToString().TrimEnd('\r');
    }

    public static async Task<string> CaptureAsync(StreamReader reader, int maxChars, Action truncated, CancellationToken token)
    {
        var head = new StringBuilder();
        var tail = new char[Math.Max(1, maxChars / 2)];
        var headLimit = maxChars - tail.Length;
        var tailCount = 0;
        var tailPosition = 0;
        var didTruncate = false;
        var buffer = new char[4096];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) != 0)
        {
            for (var i = 0; i < count; i++)
            {
                if (head.Length < headLimit) head.Append(buffer[i]);
                else
                {
                    if (tailCount == tail.Length && !didTruncate)
                    {
                        didTruncate = true;
                        truncated();
                    }
                    tail[tailPosition] = buffer[i];
                    tailPosition = (tailPosition + 1) % tail.Length;
                    tailCount = Math.Min(tailCount + 1, tail.Length);
                }
            }
        }
        if (didTruncate) head.Append("\n[stderr truncated]\n");
        var start = tailCount == tail.Length ? tailPosition : 0;
        for (var i = 0; i < tailCount; i++) head.Append(tail[(start + i) % tail.Length]);
        return head.ToString();
    }
}
