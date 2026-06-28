using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ETL_SQL.Core.Common;
/// <summary>
/// Machine-bound symmetric encryption.
/// Windows: uses DPAPI <see cref="DataProtectionScope.LocalMachine"/> so data is
/// unreadable if moved to another machine.
/// Linux/macOS: derives a machine-unique AES-256 key from <c>/etc/machine-id</c>
/// (or the hostname as a fallback), then encrypts with authenticated AES-256-GCM.
/// </summary>
public static class MachineBoundCrypto
{
    private static readonly byte[] MagicV2 = Encoding.ASCII.GetBytes("ETLSQLM2");
    private static readonly byte[] FileMagicV2 = Encoding.ASCII.GetBytes("ETLSQLMF2");
    private const int AesIvLength = 16;
    private const int AesGcmNonceLength = 12;
    private const int AesGcmTagLength = 16;
    private const int FileTagLength = 32;

    // ── Public API ────────────────────────────────────────────────────────────

    public static void EncryptFile(string inputPath, string outputPath)
        => EncryptFileAsync(inputPath, outputPath).GetAwaiter().GetResult();

    public static async Task EncryptFileAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        var fileKey = GetMachineFileKey();
        using var aes = Aes.Create();
        aes.Key = fileKey;
        aes.GenerateIV();

        var tempCipher = outputPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
            await using (var fsIn = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            await using (var fsCipher = new FileStream(tempCipher, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await using (var cs = new CryptoStream(fsCipher, encryptor, CryptoStreamMode.Write))
            {
                await fsIn.CopyToAsync(cs, cancellationToken).ConfigureAwait(false);
            }

            await WriteAuthenticatedFileAsync(outputPath, aes.IV, fileKey, tempCipher, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(tempCipher);
        }
    }

    public static void DecryptFile(string inputPath, string outputPath)
        => DecryptFileAsync(inputPath, outputPath).GetAwaiter().GetResult();

    public static async Task DecryptFileAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        await using var fsIn = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        if (await TryReadFileMagicAsync(fsIn, cancellationToken).ConfigureAwait(false))
        {
            var iv = new byte[AesIvLength];
            await fsIn.ReadExactlyAsync(iv, cancellationToken).ConfigureAwait(false);
            var cipherStart = fsIn.Position;
            var cipherLength = fsIn.Length - cipherStart - FileTagLength;
            if (cipherLength < 0)
                throw new CryptographicException("Ciphertext is too short to contain authenticated payload metadata.");

            var fileKey = GetMachineFileKey();
            await VerifyAuthenticatedFileAsync(fsIn, iv, fileKey, cipherStart, cipherLength, cancellationToken).ConfigureAwait(false);

            fsIn.Position = cipherStart;
            using var limitedCipher = new LimitedReadStream(fsIn, cipherLength);
            using var aes = Aes.Create();
            using var decryptor = aes.CreateDecryptor(fileKey, iv);
            await using var fsOut = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            await using var cs = new CryptoStream(limitedCipher, decryptor, CryptoStreamMode.Read);
            await cs.CopyToAsync(fsOut, cancellationToken).ConfigureAwait(false);
            return;
        }

        fsIn.Position = 0;
        using var ms = new MemoryStream();
        await fsIn.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        var plaintext = Unprotect(ms.ToArray());
        await File.WriteAllBytesAsync(outputPath, plaintext, cancellationToken).ConfigureAwait(false);
    }

    // ── Core protect/unprotect ────────────────────────────────────────────────

    /// <summary>
    /// Encrypts an in-memory payload bound to the current machine (DPAPI LocalMachine on Windows;
    /// authenticated AES-256-GCM keyed from the machine id elsewhere). The result is not portable to
    /// another host. Use for at-rest data when no portable, key-managed secret is configured.
    /// </summary>
    public static byte[] Protect(byte[] data)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ProtectedData.Protect(data, null, DataProtectionScope.LocalMachine);

        return AesEncrypt(data, GetMachineKey());
    }

    /// <summary>Decrypts a payload produced by <see cref="Protect(byte[])"/> on the same machine.</summary>
    public static byte[] Unprotect(byte[] data)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ProtectedData.Unprotect(data, null, DataProtectionScope.LocalMachine);

        return AesDecrypt(data, GetMachineKey());
    }

    // ── Non-Windows key derivation ────────────────────────────────────────────

    private static byte[] GetMachineKey()
    {
        var machineSecret = ReadMachineSecret();
        // HKDF-SHA256: IKM = machine secret, no salt, label = context string
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(machineSecret),
            32,
            info: "etl-sql-machine-bound"u8.ToArray());
    }

    private static byte[] GetMachineFileKey()
    {
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes($"{Environment.MachineName}:{Environment.UserName}:{ReadMachineSecret()}"),
            32,
            info: "etl-sql-machine-bound-file-stream"u8.ToArray());
    }

    private static string ReadMachineSecret()
    {
        // Linux: systemd machine-id is stable across reboots, unique per installation
        if (File.Exists("/etc/machine-id"))
            return File.ReadAllText("/etc/machine-id").Trim();

        // macOS / containers without machine-id: fall back to hostname
        return Environment.MachineName;
    }

    // ── AES-256-GCM helpers ───────────────────────────────────────────────────

    private static byte[] AesEncrypt(byte[] data, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(AesGcmNonceLength);
        var ciphertext = new byte[data.Length];
        var tag = new byte[AesGcmTagLength];
        using (var aes = new AesGcm(key, AesGcmTagLength))
        {
            aes.Encrypt(nonce, data, ciphertext, tag);
        }

        using var ms = new MemoryStream(MagicV2.Length + AesGcmNonceLength + AesGcmTagLength + ciphertext.Length);
        ms.Write(MagicV2, 0, MagicV2.Length);
        ms.Write(nonce, 0, nonce.Length);
        ms.Write(tag, 0, tag.Length);
        ms.Write(ciphertext, 0, ciphertext.Length);
        return ms.ToArray();
    }

    private static byte[] AesDecrypt(byte[] data, byte[] key)
    {
        if (HasMagicV2(data))
        {
            if (data.Length < MagicV2.Length + AesGcmNonceLength + AesGcmTagLength)
                throw new CryptographicException("Ciphertext is too short to contain authenticated payload metadata.");

            var offset = MagicV2.Length;
            var nonce = data.AsSpan(offset, AesGcmNonceLength).ToArray();
            offset += AesGcmNonceLength;
            var tag = data.AsSpan(offset, AesGcmTagLength).ToArray();
            offset += AesGcmTagLength;
            var ciphertext = data.AsSpan(offset).ToArray();
            var plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(key, AesGcmTagLength))
            {
                aes.Decrypt(nonce, ciphertext, tag, plaintext);
            }

            return plaintext;
        }

        // Legacy non-Windows payloads used AES-CBC with the IV prepended. Keep read compatibility.
        if (data.Length < AesIvLength)
            throw new CryptographicException("Ciphertext is too short to contain an IV.");

        var iv = new byte[AesIvLength];
        Array.Copy(data, 0, iv, 0, AesIvLength);

        using var legacyAes = Aes.Create();
        legacyAes.Key = key;
        legacyAes.IV = iv;

        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, legacyAes.CreateDecryptor(), CryptoStreamMode.Write))
        {
            cs.Write(data, AesIvLength, data.Length - AesIvLength);
            cs.FlushFinalBlock();
        }
        return ms.ToArray();
    }

    private static bool HasMagicV2(byte[] data) =>
        data.Length >= MagicV2.Length && data.AsSpan(0, MagicV2.Length).SequenceEqual(MagicV2);

    private static async Task WriteAuthenticatedFileAsync(
        string outputPath,
        byte[] iv,
        byte[] hmacKey,
        string tempCipher,
        CancellationToken cancellationToken)
    {
        using var hmac = new HMACSHA256(hmacKey);
        await using var fsOut = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await WriteAndHashAsync(fsOut, hmac, FileMagicV2, cancellationToken).ConfigureAwait(false);
        await WriteAndHashAsync(fsOut, hmac, iv, cancellationToken).ConfigureAwait(false);

        var buffer = new byte[81920];
        await using (var fsCipher = new FileStream(tempCipher, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        {
            int read;
            while ((read = await fsCipher.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false)) > 0)
            {
                await fsOut.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                hmac.TransformBlock(buffer, 0, read, null, 0);
            }
        }

        hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        await fsOut.WriteAsync(hmac.Hash!, cancellationToken).ConfigureAwait(false);
    }

    private static async Task VerifyAuthenticatedFileAsync(FileStream fsIn, byte[] iv, byte[] hmacKey, long cipherStart, long cipherLength, CancellationToken cancellationToken)
    {
        using var hmac = new HMACSHA256(hmacKey);
        hmac.TransformBlock(FileMagicV2, 0, FileMagicV2.Length, null, 0);
        hmac.TransformBlock(iv, 0, iv.Length, null, 0);

        var buffer = new byte[81920];
        var remaining = cipherLength;
        while (remaining > 0)
        {
            var read = await fsIn.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                throw new CryptographicException("Ciphertext is too short to contain authenticated payload metadata.");
            hmac.TransformBlock(buffer, 0, read, null, 0);
            remaining -= read;
        }
        hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        var expectedTag = new byte[FileTagLength];
        await fsIn.ReadExactlyAsync(expectedTag.AsMemory(0, expectedTag.Length), cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(hmac.Hash!, expectedTag))
            throw new CryptographicException("Machine-bound encrypted file authentication failed.");
    }

    private static async Task WriteAndHashAsync(Stream stream, HMAC hmac, byte[] bytes, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        hmac.TransformBlock(bytes, 0, bytes.Length, null, 0);
    }

    private static async Task<bool> TryReadFileMagicAsync(FileStream fs, CancellationToken cancellationToken)
    {
        if (fs.Length < FileMagicV2.Length) return false;
        var magic = new byte[FileMagicV2.Length];
        await fs.ReadExactlyAsync(magic, cancellationToken).ConfigureAwait(false);
        return magic.AsSpan().SequenceEqual(FileMagicV2);
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _length;
        private long _remaining;

        public LimitedReadStream(Stream inner, long length)
        {
            _inner = inner;
            _length = length;
            _remaining = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _length;
        public override long Position { get => _length - _remaining; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_remaining <= 0) return 0;
            var read = _inner.Read(buffer, offset, (int)Math.Min(count, _remaining));
            _remaining -= read;
            return read;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
        }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_remaining <= 0) return 0;
            var read = await _inner.ReadAsync(buffer.Slice(0, (int)Math.Min(buffer.Length, _remaining)), cancellationToken).ConfigureAwait(false);
            _remaining -= read;
            return read;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
