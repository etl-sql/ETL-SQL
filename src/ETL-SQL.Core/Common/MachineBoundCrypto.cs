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
/// Windows: in-memory payloads use DPAPI <see cref="DataProtectionScope.LocalMachine"/> so data
/// is unreadable if moved to another machine.
/// Linux/macOS, and encrypted files everywhere: keys are derived from a random 256-bit key file
/// created on first use (<see cref="KeyFileName"/> under the account's local application data, or
/// <c>ETLSQL_MACHINE_KEY_PATH</c>), readable only by its owner. Payloads written before that key
/// existed were keyed from <c>/etc/machine-id</c> or the host name, which anyone with a copy of the
/// image can read; they still decrypt, and are rewritten under the key file the next time they are
/// saved.
/// </summary>
public static class MachineBoundCrypto
{
    private static readonly byte[] MagicV2 = Encoding.ASCII.GetBytes("ETLSQLM2");
    private static readonly byte[] MagicV3 = Encoding.ASCII.GetBytes("ETLSQLM3");
    private static readonly byte[] FileMagicV2 = Encoding.ASCII.GetBytes("ETLSQLMF2");
    private static readonly byte[] FileMagicV3 = Encoding.ASCII.GetBytes("ETLSQLMF3");

    /// <summary>The key file's name. The application-file guard refuses it to scripts anywhere.</summary>
    public const string KeyFileName = ".etlsql-machine-key";
    private const string KeyPathVariable = "ETLSQL_MACHINE_KEY_PATH";
    private const int KeyFileSecretLength = 32;
    private static readonly Lazy<byte[]> KeyFileSecret = new(LoadOrCreateKeyFileSecret, LazyThreadSafetyMode.ExecutionAndPublication);
    private const int AesIvLength = 16;
    private const int AesGcmNonceLength = 12;
    private const int AesGcmTagLength = 16;
    private const int FileTagLength = 32;

    // ── Public API ────────────────────────────────────────────────────────────

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

            await WriteAuthenticatedFileAsync(outputPath, FileMagicV3, aes.IV, fileKey, tempCipher, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(tempCipher);
        }
    }

    public static async Task DecryptFileAsync(string inputPath, string outputPath, CancellationToken cancellationToken = default)
    {
        await using var fsIn = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        if (await TryReadFileMagicAsync(fsIn, cancellationToken).ConfigureAwait(false) is { } magic)
        {
            var iv = new byte[AesIvLength];
            await fsIn.ReadExactlyAsync(iv, cancellationToken).ConfigureAwait(false);
            var cipherStart = fsIn.Position;
            var cipherLength = fsIn.Length - cipherStart - FileTagLength;
            if (cipherLength < 0)
                throw new CryptographicException("Ciphertext is too short to contain authenticated payload metadata.");

            var fileKey = FileKeyFor(magic);
            await VerifyAuthenticatedFileAsync(fsIn, magic, iv, fileKey, cipherStart, cipherLength, cancellationToken).ConfigureAwait(false);

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

        return AesEncrypt(data, GetMachineKey(), MagicV3);
    }

    /// <summary>Decrypts a payload produced by <see cref="Protect(byte[])"/> on the same machine.</summary>
    public static byte[] Unprotect(byte[] data)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return ProtectedData.Unprotect(data, null, DataProtectionScope.LocalMachine);

        return AesDecrypt(data);
    }

    // ── Non-Windows key derivation ────────────────────────────────────────────

    private static byte[] GetMachineKey() =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, KeyFileSecret.Value, 32, info: "etl-sql-machine-bound-v3"u8.ToArray());

    private static byte[] GetMachineFileKey() =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, KeyFileSecret.Value, 32, info: "etl-sql-machine-bound-file-stream-v3"u8.ToArray());

    private static byte[] FileKeyFor(byte[] magic) =>
        ReferenceEquals(magic, FileMagicV3) ? GetMachineFileKey() : GetLegacyMachineFileKey();

    // Read-only: keys for payloads written before the key file existed.
    private static byte[] GetLegacyMachineKey()
    {
        var machineSecret = ReadMachineSecret();
        // HKDF-SHA256: IKM = machine secret, no salt, label = context string
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(machineSecret),
            32,
            info: "etl-sql-machine-bound"u8.ToArray());
    }

    private static byte[] GetLegacyMachineFileKey()
    {
        return HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes($"{Environment.MachineName}:{Environment.UserName}:{ReadMachineSecret()}"),
            32,
            info: "etl-sql-machine-bound-file-stream"u8.ToArray());
    }

    /// <summary>Where the key file lives: <c>ETLSQL_MACHINE_KEY_PATH</c>, else local application data.</summary>
    internal static string KeyFilePath()
    {
        var configured = Environment.GetEnvironmentVariable(KeyPathVariable);
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var root = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.DoNotVerify);
        if (string.IsNullOrEmpty(root)) root = Path.Combine(Path.GetTempPath(), "etl-sql-" + Environment.UserName);
        return Path.Combine(root, "etl-sql", KeyFileName);
    }

    private static byte[] LoadOrCreateKeyFileSecret()
    {
        var path = KeyFilePath();
        for (int attempt = 0; attempt < 2; attempt++)
        {
            if (File.Exists(path))
                return ReadKeyFile(path);
            try
            {
                CreateKeyFile(path);
            }
            catch (IOException) when (File.Exists(path))
            {
                // Another process created it first; read theirs.
            }
        }
        return ReadKeyFile(path);
    }

    private static void CreateKeyFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var secret = RandomNumberGenerator.GetBytes(KeyFileSecretLength);
        var stored = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ProtectedData.Protect(secret, null, DataProtectionScope.CurrentUser)
            : secret;
        var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        var temp = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(temp, options))
            {
                stream.Write(stored);
                stream.Flush(flushToDisk: true);
            }
            // Never replace an existing key: payloads written under it would become unreadable.
            File.Move(temp, path, overwrite: false);
        }
        finally
        {
            TryDeleteFile(temp);
        }
    }

    private static byte[] ReadKeyFile(string path)
    {
        var stored = File.ReadAllBytes(path);
        var secret = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? ProtectedData.Unprotect(stored, null, DataProtectionScope.CurrentUser)
            : stored;
        if (secret.Length != KeyFileSecretLength)
            throw new CryptographicException($"The machine key file '{path}' is not a {KeyFileSecretLength}-byte key.");
        return secret;
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

    private static byte[] AesEncrypt(byte[] data, byte[] key, byte[] magic)
    {
        var nonce = RandomNumberGenerator.GetBytes(AesGcmNonceLength);
        var ciphertext = new byte[data.Length];
        var tag = new byte[AesGcmTagLength];
        using (var aes = new AesGcm(key, AesGcmTagLength))
        {
            aes.Encrypt(nonce, data, ciphertext, tag);
        }

        using var ms = new MemoryStream(magic.Length + AesGcmNonceLength + AesGcmTagLength + ciphertext.Length);
        ms.Write(magic, 0, magic.Length);
        ms.Write(nonce, 0, nonce.Length);
        ms.Write(tag, 0, tag.Length);
        ms.Write(ciphertext, 0, ciphertext.Length);
        return ms.ToArray();
    }

    private static byte[] AesDecrypt(byte[] data)
    {
        var magic = HasMagic(data, MagicV3) ? MagicV3 : HasMagic(data, MagicV2) ? MagicV2 : null;
        var key = ReferenceEquals(magic, MagicV3) ? GetMachineKey() : GetLegacyMachineKey();
        if (magic is not null)
        {
            if (data.Length < magic.Length + AesGcmNonceLength + AesGcmTagLength)
                throw new CryptographicException("Ciphertext is too short to contain authenticated payload metadata.");

            var offset = magic.Length;
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

    private static bool HasMagic(byte[] data, byte[] magic) =>
        data.Length >= magic.Length && data.AsSpan(0, magic.Length).SequenceEqual(magic);

    private static async Task WriteAuthenticatedFileAsync(
        string outputPath,
        byte[] magic,
        byte[] iv,
        byte[] hmacKey,
        string tempCipher,
        CancellationToken cancellationToken)
    {
        using var hmac = new HMACSHA256(hmacKey);
        await using var fsOut = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await WriteAndHashAsync(fsOut, hmac, magic, cancellationToken).ConfigureAwait(false);
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

    private static async Task VerifyAuthenticatedFileAsync(FileStream fsIn, byte[] magic, byte[] iv, byte[] hmacKey, long cipherStart, long cipherLength, CancellationToken cancellationToken)
    {
        using var hmac = new HMACSHA256(hmacKey);
        hmac.TransformBlock(magic, 0, magic.Length, null, 0);
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

    /// <summary>Returns the file format's magic (V2 or V3), or null for a whole-file payload.</summary>
    private static async Task<byte[]?> TryReadFileMagicAsync(FileStream fs, CancellationToken cancellationToken)
    {
        if (fs.Length < FileMagicV2.Length) return null;
        var magic = new byte[FileMagicV2.Length];
        await fs.ReadExactlyAsync(magic, cancellationToken).ConfigureAwait(false);
        return MatchFileMagic(magic);
    }

    private static byte[]? TryReadFileMagic(Stream fs)
    {
        if (fs.Length < FileMagicV2.Length) return null;
        var magic = new byte[FileMagicV2.Length];
        fs.ReadExactly(magic, 0, FileMagicV2.Length);
        return MatchFileMagic(magic);
    }

    private static byte[]? MatchFileMagic(byte[] magic) =>
        magic.AsSpan().SequenceEqual(FileMagicV3) ? FileMagicV3
        : magic.AsSpan().SequenceEqual(FileMagicV2) ? FileMagicV2
        : null;

    public static Stream DecryptStream(Stream fsIn)
    {
        if (TryReadFileMagic(fsIn) is { } magic)
        {
            var iv = new byte[AesIvLength];
            fsIn.ReadExactly(iv, 0, AesIvLength);
            var cipherStart = fsIn.Position;
            var cipherLength = fsIn.Length - cipherStart - FileTagLength;
            if (cipherLength < 0)
                throw new CryptographicException("Ciphertext is too short.");

            var fileKey = FileKeyFor(magic);
            fsIn.Position = cipherStart;
            var limitedCipher = new LimitedReadStream(fsIn, cipherLength);
            var aes = Aes.Create();
            var decryptor = aes.CreateDecryptor(fileKey, iv);
            return new ChainedStream(new CryptoStream(limitedCipher, decryptor, CryptoStreamMode.Read), aes);
        }

        fsIn.Position = 0;
        using var ms = new MemoryStream();
        fsIn.CopyTo(ms);
        var plaintext = Unprotect(ms.ToArray());
        return new MemoryStream(plaintext);
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
