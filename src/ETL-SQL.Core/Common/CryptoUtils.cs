using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using ETL_SQL.Core.Common;
using ETL_SQL.Core.Common.Exceptions;
using PgpCore;

namespace ETL_SQL.Common;
/// <summary>
/// Utility class for encryption and decryption of strings and files.
/// Uses AES-256 with PBKDF2 key derivation or RSA (SSH) key pairs.
/// </summary>
public static class CryptoUtils
{
    private const int KeySize = 256;
    private const int Iterations = 600000;
    private const int SaltSize = 16;
    private const int IvSize = 16;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private const int FileTagSize = 32;
    private const byte CURRENT_VERSION = 2;
    private static readonly byte[] FileMagicV2 = Encoding.ASCII.GetBytes("ETLSQL2");
    private static readonly byte[] SshFileMagicV2 = Encoding.ASCII.GetBytes("ETLSQLSSH2");

    /// <summary>
    /// Encrypts a string using the specified password and optional algorithm.
    /// </summary>
    public static string Encrypt(string plainText, string password, HashAlgorithmName? algo = null)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password cannot be null or empty.", nameof(password));
        var hashAlgo = algo ?? HashAlgorithmName.SHA256;
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, hashAlgo, KeySize / 8);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plainText);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];
        using (var aes = new AesGcm(key, TagSize))
        {
            aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        using var ms = new MemoryStream(1 + SaltSize + NonceSize + TagSize + ciphertext.Length);
        ms.WriteByte(CURRENT_VERSION);
        ms.Write(salt, 0, SaltSize);
        ms.Write(nonce, 0, NonceSize);
        ms.Write(tag, 0, TagSize);
        ms.Write(ciphertext, 0, ciphertext.Length);

        return "ENC:" + Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>
    /// Decrypts a string that was encrypted using <see cref="Encrypt"/>.
    /// </summary>
    public static string Decrypt(string cipherText, string password, HashAlgorithmName? algo = null)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password cannot be null or empty.", nameof(password));
        if (string.IsNullOrEmpty(cipherText)) return cipherText;
        if (!cipherText.StartsWith("ENC:")) return cipherText;

        try
        {
            var hashAlgo = algo ?? HashAlgorithmName.SHA256;
            byte[] fullBytes = Convert.FromBase64String(cipherText.Substring(4));

            int offset = 0;
            int iterations = Iterations;
            int keySize = KeySize;

            if (fullBytes.Length > 0 && fullBytes[0] == CURRENT_VERSION)
            {
                if (fullBytes.Length < 1 + SaltSize + NonceSize + TagSize)
                    throw new ExecutionException("Invalid encrypted connection string format.");

                var v2Salt = fullBytes.AsSpan(1, SaltSize).ToArray();
                var nonce = fullBytes.AsSpan(1 + SaltSize, NonceSize).ToArray();
                var tag = fullBytes.AsSpan(1 + SaltSize + NonceSize, TagSize).ToArray();
                var v2Encrypted = fullBytes.AsSpan(1 + SaltSize + NonceSize + TagSize).ToArray();
                var v2Key = Rfc2898DeriveBytes.Pbkdf2(password, v2Salt, iterations, hashAlgo, keySize / 8);
                var plaintext = new byte[v2Encrypted.Length];
                using (var aesGcm = new AesGcm(v2Key, TagSize))
                {
                    aesGcm.Decrypt(nonce, v2Encrypted, tag, plaintext);
                }

                return Encoding.UTF8.GetString(plaintext);
            }

            // Legacy AES-CBC payloads used version 1. Keep read compatibility.
            if (fullBytes.Length > 0 && fullBytes[0] == 1)
            {
                offset = 1;
            }

            if (fullBytes.Length < offset + SaltSize + IvSize)
                throw new ExecutionException("Invalid encrypted connection string format.");

            byte[] salt = new byte[SaltSize];
            byte[] iv = new byte[IvSize];
            byte[] encrypted = new byte[fullBytes.Length - offset - SaltSize - IvSize];

            Buffer.BlockCopy(fullBytes, offset, salt, 0, SaltSize);
            Buffer.BlockCopy(fullBytes, offset + SaltSize, iv, 0, IvSize);
            Buffer.BlockCopy(fullBytes, offset + SaltSize + IvSize, encrypted, 0, encrypted.Length);

            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, hashAlgo, keySize / 8);

            using var aes = Aes.Create();
            using var decryptor = aes.CreateDecryptor(key, iv);
            using var ms = new MemoryStream(encrypted);
            using var cs = new CryptoStream(ms, decryptor, CryptoStreamMode.Read);
            using var sr = new StreamReader(cs, Encoding.UTF8);

            return sr.ReadToEnd();
        }
        catch (CryptographicException)
        {
            throw new ExecutionException("Failed to decrypt: Invalid password or corrupted data.");
        }
        catch (Exception ex) when (!(ex is ExecutionException))
        {
            throw new ExecutionException($"Decryption error: {ex.Message}");
        }
    }
    /// <summary>
    /// Encrypts a file on disk using a password.
    /// </summary>
    public static void EncryptFile(string inputFile, string outputFile, string password, bool overwrite, HashAlgorithmName? algo = null)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password cannot be null or empty.", nameof(password));
        if (File.Exists(outputFile) && !overwrite)
            throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {outputFile}");

        var hashAlgo = algo ?? HashAlgorithmName.SHA256;
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        var (encryptionKey, hmacKey) = DeriveFileKeys(password, salt, hashAlgo);
        var tempCipher = outputFile + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            using (var aes = Aes.Create())
            {
                aes.Key = encryptionKey;
                aes.GenerateIV();

                using (var fsIn = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                using (var fsCipher = new FileStream(tempCipher, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                using (var cs = new CryptoStream(fsCipher, encryptor, CryptoStreamMode.Write))
                {
                    fsIn.CopyTo(cs);
                }

                WriteAuthenticatedFile(outputFile, salt, aes.IV, hmacKey, tempCipher);
            }
        }
        finally
        {
            TryDeleteFile(tempCipher);
        }
    }

    /// <summary>
    /// Decrypts a file on disk using a password.
    /// </summary>
    public static void DecryptFile(string inputFile, string outputFile, string password, bool overwrite, HashAlgorithmName? algo = null)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password cannot be null or empty.", nameof(password));
        if (File.Exists(outputFile) && !overwrite)
            throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {outputFile}");

        var hashAlgo = algo ?? HashAlgorithmName.SHA256;
        using (var fsIn = new FileStream(inputFile, FileMode.Open))
        {
            if (TryReadFileMagicV2(fsIn))
            {
                byte[] v2Salt = new byte[SaltSize];
                byte[] v2Iv = new byte[IvSize];
                fsIn.ReadExactly(v2Salt, 0, SaltSize);
                fsIn.ReadExactly(v2Iv, 0, IvSize);
                var cipherStart = fsIn.Position;
                var cipherLength = fsIn.Length - cipherStart - FileTagSize;
                if (cipherLength < 0)
                    throw new ExecutionException("Invalid encrypted file format.");

                var (encryptionKey, hmacKey) = DeriveFileKeys(password, v2Salt, hashAlgo);
                VerifyAuthenticatedFile(fsIn, v2Salt, v2Iv, hmacKey, cipherStart, cipherLength);

                fsIn.Position = cipherStart;
                using var limitedCipher = new LimitedReadStream(fsIn, cipherLength);
                using var v2Aes = Aes.Create();
                v2Aes.Key = encryptionKey;
                v2Aes.IV = v2Iv;
                using var v2Decryptor = v2Aes.CreateDecryptor(v2Aes.Key, v2Aes.IV);
                using var cs = new CryptoStream(limitedCipher, v2Decryptor, CryptoStreamMode.Read);
                using var fsOut = new FileStream(outputFile, FileMode.Create);
                cs.CopyTo(fsOut);
                return;
            }

            fsIn.Position = 0;
            byte[] salt = new byte[SaltSize];
            byte[] iv = new byte[IvSize];
            fsIn.ReadExactly(salt, 0, SaltSize);
            fsIn.ReadExactly(iv, 0, IvSize);

            byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, hashAlgo, KeySize / 8);

            using var aes = Aes.Create();
            using var decryptor = aes.CreateDecryptor(key, iv);
            using (var cs = new CryptoStream(fsIn, decryptor, CryptoStreamMode.Read))
            using (var fsOut = new FileStream(outputFile, FileMode.Create))
            {
                cs.CopyTo(fsOut);
            }
        }
    }

    private static bool TryReadFileMagicV2(Stream fs)
    {
        if (fs.Length < FileMagicV2.Length) return false;
        Span<byte> magic = stackalloc byte[FileMagicV2.Length];
        fs.ReadExactly(magic);
        return magic.SequenceEqual(FileMagicV2);
    }

    public static Stream DecryptStream(Stream fsIn, string password, HashAlgorithmName? algo = null)
    {
        if (string.IsNullOrWhiteSpace(password)) throw new ArgumentException("Password cannot be null or empty.", nameof(password));

        var hashAlgo = algo ?? HashAlgorithmName.SHA256;
        if (TryReadFileMagicV2(fsIn))
        {
            byte[] salt = new byte[SaltSize];
            byte[] iv = new byte[IvSize];
            fsIn.ReadExactly(salt, 0, SaltSize);
            fsIn.ReadExactly(iv, 0, IvSize);
            var cipherStart = fsIn.Position;
            var cipherLength = fsIn.Length - cipherStart - FileTagSize;
            if (cipherLength < 0)
                throw new ExecutionException("Invalid encrypted file format.");

            var (encryptionKey, hmacKey) = DeriveFileKeys(password, salt, hashAlgo);
            var limited = new LimitedReadStream(fsIn, cipherLength);
            var aes = Aes.Create();
            aes.Key = encryptionKey;
            aes.IV = iv;
            var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
            return new ChainedStream(new CryptoStream(limited, decryptor, CryptoStreamMode.Read), aes);
        }

        fsIn.Position = 0;
        byte[] saltV1 = new byte[SaltSize];
        byte[] ivV1 = new byte[IvSize];
        fsIn.ReadExactly(saltV1, 0, SaltSize);
        fsIn.ReadExactly(ivV1, 0, IvSize);

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, saltV1, Iterations, hashAlgo, KeySize / 8);
        var aesV1 = Aes.Create();
        var decryptorV1 = aesV1.CreateDecryptor(key, ivV1);
        return new ChainedStream(new CryptoStream(fsIn, decryptorV1, CryptoStreamMode.Read), aesV1);
    }

    public static Stream DecryptStreamWithSsh(Stream fsIn, string keyFile, string? passphrase = null)
    {
        string pem = File.ReadAllText(keyFile);
        var rsa = RSA.Create();
        if (string.IsNullOrEmpty(passphrase)) rsa.ImportFromPem(pem);
        else rsa.ImportFromEncryptedPem(pem, passphrase);

        if (TryReadMagic(fsIn, SshFileMagicV2))
        {
            byte[] lenBytes = new byte[4];
            fsIn.ReadExactly(lenBytes, 0, 4);
            int keyLen = BitConverter.ToInt32(lenBytes, 0);
            if (keyLen <= 0)
                throw new ExecutionException("Invalid SSH encrypted file format.");

            byte[] encryptedKey = new byte[keyLen];
            fsIn.ReadExactly(encryptedKey, 0, keyLen);

            byte[] iv = new byte[IvSize];
            fsIn.ReadExactly(iv, 0, IvSize);

            byte[] keyMaterial = rsa.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA256);
            if (keyMaterial.Length < 64)
                throw new ExecutionException("Invalid SSH encrypted file key material.");

            byte[] aesKey = keyMaterial.AsSpan(0, 32).ToArray();
            var cipherStart = fsIn.Position;
            var cipherLength = fsIn.Length - cipherStart - FileTagSize;
            if (cipherLength < 0)
                throw new ExecutionException("Invalid SSH encrypted file format.");

            var limitedCipher = new LimitedReadStream(fsIn, cipherLength);
            var aes = Aes.Create();
            var decryptor = aes.CreateDecryptor(aesKey, iv);
            return new ChainedStream(new CryptoStream(limitedCipher, decryptor, CryptoStreamMode.Read), aes, rsa);
        }

        throw new ExecutionException("SSH stream decryption is not in a recognized format.");
    }

    private static bool TryReadMagic(Stream fs, byte[] expectedMagic)
    {
        if (fs.Length < expectedMagic.Length) return false;
        Span<byte> magic = stackalloc byte[expectedMagic.Length];
        fs.ReadExactly(magic);
        return magic.SequenceEqual(expectedMagic);
    }

    private static (byte[] EncryptionKey, byte[] HmacKey) DeriveFileKeys(string password, byte[] salt, HashAlgorithmName hashAlgo)
    {
        var keyMaterial = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, hashAlgo, 64);
        return (keyMaterial[..32], keyMaterial[32..]);
    }

    private static void WriteAuthenticatedFile(string outputFile, byte[] salt, byte[] iv, byte[] hmacKey, string tempCipher)
    {
        using var hmac = new HMACSHA256(hmacKey);
        using var fsOut = new FileStream(outputFile, FileMode.Create);
        WriteAndHash(fsOut, hmac, FileMagicV2);
        WriteAndHash(fsOut, hmac, salt);
        WriteAndHash(fsOut, hmac, iv);

        var buffer = new byte[81920];
        using (var fsCipher = new FileStream(tempCipher, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            int read;
            while ((read = fsCipher.Read(buffer, 0, buffer.Length)) > 0)
            {
                fsOut.Write(buffer, 0, read);
                hmac.TransformBlock(buffer, 0, read, null, 0);
            }
        }

        hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        fsOut.Write(hmac.Hash!, 0, hmac.Hash!.Length);
    }

    private static void VerifyAuthenticatedFile(FileStream fsIn, byte[] salt, byte[] iv, byte[] hmacKey, long cipherStart, long cipherLength)
    {
        using var hmac = new HMACSHA256(hmacKey);
        hmac.TransformBlock(FileMagicV2, 0, FileMagicV2.Length, null, 0);
        hmac.TransformBlock(salt, 0, salt.Length, null, 0);
        hmac.TransformBlock(iv, 0, iv.Length, null, 0);

        var buffer = new byte[81920];
        var remaining = cipherLength;
        while (remaining > 0)
        {
            var read = fsIn.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read <= 0)
                throw new ExecutionException("Invalid encrypted file format.");
            hmac.TransformBlock(buffer, 0, read, null, 0);
            remaining -= read;
        }
        hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        var expectedTag = new byte[FileTagSize];
        fsIn.ReadExactly(expectedTag, 0, expectedTag.Length);
        if (!CryptographicOperations.FixedTimeEquals(hmac.Hash!, expectedTag))
            throw new CryptographicException("Encrypted file authentication failed.");
    }

    private static void WriteAndHash(Stream stream, HMAC hmac, byte[] bytes)
    {
        stream.Write(bytes, 0, bytes.Length);
        hmac.TransformBlock(bytes, 0, bytes.Length, null, 0);
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

    /// <summary>
    /// Encrypts a file using an SSH (RSA) public key.
    /// </summary>
    public static void EncryptFileWithSsh(string inputFile, string outputFile, string keyFile, bool overwrite)
        => EncryptFileWithSshAsync(inputFile, outputFile, keyFile, overwrite).GetAwaiter().GetResult();

    public static async Task EncryptFileWithSshAsync(
        string inputFile,
        string outputFile,
        string keyFile,
        bool overwrite,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(outputFile) && !overwrite)
            throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {outputFile}");

        string pem = await File.ReadAllTextAsync(keyFile, cancellationToken).ConfigureAwait(false);
        using var rsa = RSA.Create();
        rsa.ImportFromPem(pem);

        byte[] aesKey = RandomNumberGenerator.GetBytes(KeySize / 8);
        byte[] hmacKey = RandomNumberGenerator.GetBytes(32);
        byte[] keyMaterial = new byte[aesKey.Length + hmacKey.Length];
        Buffer.BlockCopy(aesKey, 0, keyMaterial, 0, aesKey.Length);
        Buffer.BlockCopy(hmacKey, 0, keyMaterial, aesKey.Length, hmacKey.Length);
        byte[] encryptedKey = rsa.Encrypt(keyMaterial, RSAEncryptionPadding.OaepSHA256);

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.GenerateIV();
        byte[] iv = aes.IV;
        var tempCipher = outputFile + ".tmp-" + Guid.NewGuid().ToString("N");

        try
        {
            using (var encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
            await using (var fsIn = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
            await using (var fsCipher = new FileStream(tempCipher, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            await using (var cs = new CryptoStream(fsCipher, encryptor, CryptoStreamMode.Write))
            {
                await fsIn.CopyToAsync(cs, cancellationToken).ConfigureAwait(false);
            }

            await WriteAuthenticatedSshFileAsync(outputFile, encryptedKey, iv, hmacKey, tempCipher, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            TryDeleteFile(tempCipher);
        }
    }

    /// <summary>
    /// Decrypts a file using an SSH (RSA) private key.
    /// </summary>
    public static void DecryptFileWithSsh(string inputFile, string outputFile, string keyFile, bool overwrite, string? passphrase = null)
        => DecryptFileWithSshAsync(inputFile, outputFile, keyFile, overwrite, passphrase).GetAwaiter().GetResult();

    public static async Task DecryptFileWithSshAsync(
        string inputFile,
        string outputFile,
        string keyFile,
        bool overwrite,
        string? passphrase = null,
        CancellationToken cancellationToken = default)
    {
        if (File.Exists(outputFile) && !overwrite)
            throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {outputFile}");

        string pem = await File.ReadAllTextAsync(keyFile, cancellationToken).ConfigureAwait(false);
        using var rsa = RSA.Create();
        if (string.IsNullOrEmpty(passphrase)) rsa.ImportFromPem(pem);
        else rsa.ImportFromEncryptedPem(pem, passphrase);

        await using (var fsIn = new FileStream(inputFile, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true))
        {
            if (await TryReadMagicAsync(fsIn, SshFileMagicV2, cancellationToken).ConfigureAwait(false))
            {
                byte[] lenBytesV2 = new byte[4];
                await fsIn.ReadExactlyAsync(lenBytesV2, cancellationToken).ConfigureAwait(false);
                int keyLenV2 = BitConverter.ToInt32(lenBytesV2, 0);
                if (keyLenV2 <= 0)
                    throw new ExecutionException("Invalid SSH encrypted file format.");

                byte[] encryptedKeyV2 = new byte[keyLenV2];
                await fsIn.ReadExactlyAsync(encryptedKeyV2, cancellationToken).ConfigureAwait(false);

                byte[] ivV2 = new byte[IvSize];
                await fsIn.ReadExactlyAsync(ivV2, cancellationToken).ConfigureAwait(false);

                byte[] keyMaterial = rsa.Decrypt(encryptedKeyV2, RSAEncryptionPadding.OaepSHA256);
                if (keyMaterial.Length < 64)
                    throw new ExecutionException("Invalid SSH encrypted file key material.");

                byte[] aesKeyV2 = keyMaterial.AsSpan(0, 32).ToArray();
                byte[] hmacKeyV2 = keyMaterial.AsSpan(32, 32).ToArray();
                var cipherStart = fsIn.Position;
                var cipherLength = fsIn.Length - cipherStart - FileTagSize;
                if (cipherLength < 0)
                    throw new ExecutionException("Invalid SSH encrypted file format.");

                await VerifyAuthenticatedSshFileAsync(fsIn, encryptedKeyV2, ivV2, hmacKeyV2, cipherStart, cipherLength, cancellationToken).ConfigureAwait(false);

                fsIn.Position = cipherStart;
                using var limitedCipher = new LimitedReadStream(fsIn, cipherLength);
                using var aesV2 = Aes.Create();
                using var decryptorV2 = aesV2.CreateDecryptor(aesKeyV2, ivV2);
                await using var fsOutV2 = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
                await using var csV2 = new CryptoStream(limitedCipher, decryptorV2, CryptoStreamMode.Read);
                await csV2.CopyToAsync(fsOutV2, cancellationToken).ConfigureAwait(false);
                return;
            }

            fsIn.Position = 0;
            byte[] lenBytes = new byte[4];
            await fsIn.ReadExactlyAsync(lenBytes, cancellationToken).ConfigureAwait(false);
            int keyLen = BitConverter.ToInt32(lenBytes, 0);

            byte[] encryptedKey = new byte[keyLen];
            await fsIn.ReadExactlyAsync(encryptedKey, cancellationToken).ConfigureAwait(false);

            byte[] iv = new byte[IvSize];
            await fsIn.ReadExactlyAsync(iv, cancellationToken).ConfigureAwait(false);

            byte[] aesKey = rsa.Decrypt(encryptedKey, RSAEncryptionPadding.OaepSHA256);

            using var aes = Aes.Create();
            using var decryptor = aes.CreateDecryptor(aesKey, iv);
            await using (var cs = new CryptoStream(fsIn, decryptor, CryptoStreamMode.Read))
            await using (var fsOut = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
            {
                await cs.CopyToAsync(fsOut, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private static async Task WriteAuthenticatedSshFileAsync(
        string outputFile,
        byte[] encryptedKey,
        byte[] iv,
        byte[] hmacKey,
        string tempCipher,
        CancellationToken cancellationToken)
    {
        using var hmac = new HMACSHA256(hmacKey);
        await using var fsOut = new FileStream(outputFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await WriteAndHashAsync(fsOut, hmac, SshFileMagicV2, cancellationToken).ConfigureAwait(false);
        await WriteAndHashAsync(fsOut, hmac, BitConverter.GetBytes(encryptedKey.Length), cancellationToken).ConfigureAwait(false);
        await WriteAndHashAsync(fsOut, hmac, encryptedKey, cancellationToken).ConfigureAwait(false);
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

    private static async Task VerifyAuthenticatedSshFileAsync(FileStream fsIn, byte[] encryptedKey, byte[] iv, byte[] hmacKey, long cipherStart, long cipherLength, CancellationToken cancellationToken)
    {
        using var hmac = new HMACSHA256(hmacKey);
        hmac.TransformBlock(SshFileMagicV2, 0, SshFileMagicV2.Length, null, 0);
        var keyLengthBytes = BitConverter.GetBytes(encryptedKey.Length);
        hmac.TransformBlock(keyLengthBytes, 0, keyLengthBytes.Length, null, 0);
        hmac.TransformBlock(encryptedKey, 0, encryptedKey.Length, null, 0);
        hmac.TransformBlock(iv, 0, iv.Length, null, 0);

        var buffer = new byte[81920];
        var remaining = cipherLength;
        while (remaining > 0)
        {
            var read = await fsIn.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellationToken).ConfigureAwait(false);
            if (read <= 0)
                throw new ExecutionException("Invalid SSH encrypted file format.");
            hmac.TransformBlock(buffer, 0, read, null, 0);
            remaining -= read;
        }
        hmac.TransformFinalBlock(Array.Empty<byte>(), 0, 0);

        var expectedTag = new byte[FileTagSize];
        await fsIn.ReadExactlyAsync(expectedTag.AsMemory(0, expectedTag.Length), cancellationToken).ConfigureAwait(false);
        if (!CryptographicOperations.FixedTimeEquals(hmac.Hash!, expectedTag))
            throw new CryptographicException("SSH encrypted file authentication failed.");
    }

    private static async Task WriteAndHashAsync(Stream stream, HMAC hmac, byte[] bytes, CancellationToken cancellationToken)
    {
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        hmac.TransformBlock(bytes, 0, bytes.Length, null, 0);
    }

    private static async Task<bool> TryReadMagicAsync(FileStream fs, byte[] magic, CancellationToken cancellationToken)
    {
        if (fs.Length < magic.Length) return false;
        byte[] candidate = new byte[magic.Length];
        await fs.ReadExactlyAsync(candidate, cancellationToken).ConfigureAwait(false);
        return candidate.AsSpan().SequenceEqual(magic);
    }

    /// <summary>
    /// Highly secure, zero-password encryption bound to the current OS user and machine (DPAPI).
    /// Used primarily for session state and metadata hardening.
    /// </summary>
    public static string Protect(string plainText, string? optionalEntropy = null)
    {
        if (string.IsNullOrEmpty(plainText)) return plainText;
        byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "DPAPI:" + Convert.ToBase64String(ProtectWindows(plainBytes, optionalEntropy));
        }

        return "MACHINE:" + Convert.ToBase64String(ProtectGeneric(plainBytes, optionalEntropy));
    }

    /// <summary>
    /// Decrypts data that was protected using <see cref="Protect"/>.
    /// Throws if attempted on a different machine or by a different OS user.
    /// </summary>
    public static string Unprotect(string cipherText, string? optionalEntropy = null)
    {
        if (string.IsNullOrEmpty(cipherText)) return cipherText;

        if (cipherText.StartsWith("DPAPI:"))
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                throw new ExecutionException("DPAPI encrypted data can only be decrypted on Windows.");

            byte[] cipherBytes = Convert.FromBase64String(cipherText.Substring(6));
            return Encoding.UTF8.GetString(UnprotectWindows(cipherBytes, optionalEntropy));
        }

        if (cipherText.StartsWith("MACHINE:"))
        {
            byte[] cipherBytes = Convert.FromBase64String(cipherText.Substring(8));
            return Encoding.UTF8.GetString(UnprotectGeneric(cipherBytes, optionalEntropy));
        }

        return cipherText;
    }

    [SupportedOSPlatform("windows")]
    private static byte[] ProtectWindows(byte[] plainBytes, string? entropy)
    {
        byte[]? entropyBytes = entropy != null ? Encoding.UTF8.GetBytes(entropy) : null;
        return ProtectedData.Protect(plainBytes, entropyBytes, DataProtectionScope.CurrentUser);
    }

    [SupportedOSPlatform("windows")]
    private static byte[] UnprotectWindows(byte[] cipherBytes, string? entropy)
    {
        byte[]? entropyBytes = entropy != null ? Encoding.UTF8.GetBytes(entropy) : null;
        return ProtectedData.Unprotect(cipherBytes, entropyBytes, DataProtectionScope.CurrentUser);
    }

    // Authenticated (encrypt-then-MAC) machine-bound payload format:
    //   [MachineMagicG2 (8)] [IV (16)] [AES-CBC ciphertext] [HMAC-SHA256 tag (32)]
    // The HMAC covers magic||IV||ciphertext. Pre-magic payloads are legacy CBC-only (no MAC) and are
    // still read for backward compatibility — see UnprotectGeneric.
    private static readonly byte[] MachineMagicG2 = Encoding.ASCII.GetBytes("ETLSQLG2");

    internal static byte[] ProtectGeneric(byte[] plainBytes, string? entropy)
    {
        var (encKey, macKey) = DeriveMachineSubKeys(GetMachineKey(entropy));
        using var aes = Aes.Create();
        aes.Key = encKey;
        aes.GenerateIV();
        byte[] iv = aes.IV;

        byte[] ciphertext;
        using (var encryptor = aes.CreateEncryptor())
        using (var msCipher = new MemoryStream())
        {
            using (var cs = new CryptoStream(msCipher, encryptor, CryptoStreamMode.Write))
            {
                cs.Write(plainBytes, 0, plainBytes.Length);
            }
            ciphertext = msCipher.ToArray();
        }

        using var ms = new MemoryStream(MachineMagicG2.Length + iv.Length + ciphertext.Length + 32);
        ms.Write(MachineMagicG2);
        ms.Write(iv);
        ms.Write(ciphertext);
        using (var hmac = new HMACSHA256(macKey))
        {
            byte[] tag = hmac.ComputeHash(ms.GetBuffer(), 0, (int)ms.Length);
            ms.Write(tag);
        }
        return ms.ToArray();
    }

    internal static byte[] UnprotectGeneric(byte[] cipherBytes, string? entropy)
    {
        byte[] material = GetMachineKey(entropy);

        if (cipherBytes.Length >= MachineMagicG2.Length
            && cipherBytes.AsSpan(0, MachineMagicG2.Length).SequenceEqual(MachineMagicG2))
        {
            var (encKey, macKey) = DeriveMachineSubKeys(material);
            const int ivLength = 16;
            int tagOffset = cipherBytes.Length - 32;
            if (tagOffset < MachineMagicG2.Length + ivLength)
                throw new ExecutionException("Machine-protected payload is too short.");

            using (var hmac = new HMACSHA256(macKey))
            {
                byte[] expected = hmac.ComputeHash(cipherBytes, 0, tagOffset);
                if (!CryptographicOperations.FixedTimeEquals(expected, cipherBytes.AsSpan(tagOffset, 32)))
                    throw new ExecutionException("Machine-protected payload authentication failed.");
            }

            byte[] iv = new byte[ivLength];
            Buffer.BlockCopy(cipherBytes, MachineMagicG2.Length, iv, 0, ivLength);
            int cipherStart = MachineMagicG2.Length + ivLength;

            using var aes = Aes.Create();
            aes.Key = encKey;
            aes.IV = iv;
            using var decryptor = aes.CreateDecryptor();
            using var msOut = new MemoryStream();
            using (var msIn = new MemoryStream(cipherBytes, cipherStart, tagOffset - cipherStart))
            using (var cs = new CryptoStream(msIn, decryptor, CryptoStreamMode.Read))
            {
                cs.CopyTo(msOut);
            }
            return msOut.ToArray();
        }

        // Legacy CBC-only payloads (IV prepended, no MAC) used the raw machine key directly. Kept for
        // read compatibility with data protected before authenticated machine encryption was added.
        if (cipherBytes.Length < 16)
            throw new ExecutionException("Machine-protected payload is too short.");
        using var legacyAes = Aes.Create();
        legacyAes.Key = material;
        byte[] legacyIv = new byte[16];
        Buffer.BlockCopy(cipherBytes, 0, legacyIv, 0, legacyIv.Length);
        legacyAes.IV = legacyIv;
        using var legacyDecryptor = legacyAes.CreateDecryptor();
        using var legacyOut = new MemoryStream();
        using (var msIn = new MemoryStream(cipherBytes, legacyIv.Length, cipherBytes.Length - legacyIv.Length))
        using (var cs = new CryptoStream(msIn, legacyDecryptor, CryptoStreamMode.Read))
        {
            cs.CopyTo(legacyOut);
        }
        return legacyOut.ToArray();
    }

    private static (byte[] EncKey, byte[] MacKey) DeriveMachineSubKeys(byte[] material)
    {
        byte[] encKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, material, 32, info: "etl-sql-machine-generic-aes"u8.ToArray());
        byte[] macKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, material, 32, info: "etl-sql-machine-generic-hmac"u8.ToArray());
        return (encKey, macKey);
    }

    internal static byte[] GetMachineKey(string? entropy)
    {
        bool isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string etlSqlDir = Path.Combine(appData, "etl-sql");
        if (!Directory.Exists(etlSqlDir))
        {
            // Owner-only directory on Unix (no-op flag on Windows, where the per-user profile already scopes it).
            if (isWindows)
                Directory.CreateDirectory(etlSqlDir);
            else
                Directory.CreateDirectory(etlSqlDir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        string keyPath = Path.Combine(etlSqlDir, "machine.key");
        byte[] baseKey;

        if (File.Exists(keyPath))
        {
            baseKey = File.ReadAllBytes(keyPath);
        }
        else
        {
            baseKey = RandomNumberGenerator.GetBytes(32);
            if (isWindows)
            {
                File.WriteAllBytes(keyPath, baseKey);
            }
            else
            {
                // Create owner read/write only (0600) atomically so the key is never briefly world-readable.
                var options = new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite
                };
                using var fs = new FileStream(keyPath, options);
                fs.Write(baseKey);
            }
        }

        if (entropy == null) return baseKey;

        // Mix entropy into the key to make it user-bound if entropy is provided
        return Rfc2898DeriveBytes.Pbkdf2(baseKey, Encoding.UTF8.GetBytes(entropy), 1000, HashAlgorithmName.SHA256, 32);
    }
    /// <summary>
    /// Encrypts a file using a PGP public key.
    /// </summary>
    public static async Task EncryptFileWithPgp(string inputFile, string outputFile, string keyFile, bool overwrite)
    {
        if (File.Exists(outputFile) && !overwrite)
            throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {outputFile}");

        var keys = new EncryptionKeys(new FileInfo(keyFile));
        using var pgp = new PGP(keys);
        await pgp.EncryptFileAsync(new FileInfo(inputFile), new FileInfo(outputFile));
    }

    /// <summary>
    /// Decrypts a file using a PGP private key.
    /// </summary>
    public static async Task DecryptFileWithPgp(string inputFile, string outputFile, string keyFile, string? passphrase, bool overwrite)
    {
        if (File.Exists(outputFile) && !overwrite)
            throw new ExecutionException($"Destination file already exists and OVERWRITE is OFF: {outputFile}");

        EncryptionKeys keys = new EncryptionKeys(new FileInfo(keyFile), passphrase ?? string.Empty);
        using var pgp = new PGP(keys);
        await pgp.DecryptFileAsync(new FileInfo(inputFile), new FileInfo(outputFile));
    }

    /// <summary>
    /// Validates a file's lock status (waiting for lock to clear) and expected hash integrity.
    /// </summary>
    public static void ValidateFileAccess(string filePath, Dictionary<string, string>? options, ETL_SQL.Core.IExecutionContext context)
    {
        if (options == null) return;
        if (!File.Exists(filePath)) return;

        // 1. Wait for lock
        if (options.TryGetValue("WAIT_FOR_LOCK", out var wfl) &&
            (wfl.Equals("ON", StringComparison.OrdinalIgnoreCase) || wfl.Equals("TRUE", StringComparison.OrdinalIgnoreCase)))
        {
            int timeoutSec = 30;
            if (options.TryGetValue("LOCK_TIMEOUT_SEC", out var lts) && int.TryParse(lts, out var ltsv))
            {
                timeoutSec = ltsv;
            }

            var start = DateTime.UtcNow;
            var timeout = TimeSpan.FromSeconds(timeoutSec);
            bool success = false;
            while (DateTime.UtcNow - start < timeout)
            {
                if (File.Exists(filePath))
                {
                    try
                    {
                        using (var fs = File.Open(filePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                        {
                            success = true;
                            break;
                        }
                    }
                    catch (IOException)
                    {
                        // locked
                    }
                }
                System.Threading.Thread.Sleep(200);
            }

            if (!success && !File.Exists(filePath))
            {
                throw new ExecutionException($"Timeout waiting for file to arrive and unlock: {filePath}");
            }
        }

        // 2. Hash validation
        if (options.TryGetValue("EXPECTED_HASH", out var eh) && !string.IsNullOrEmpty(eh))
        {
            string expectedHash = eh.Trim('\'', '\"').ToLowerInvariant();
            string algo = "SHA256";
            if (options.TryGetValue("ALGORITHM", out var a))
            {
                algo = a.ToUpperInvariant();
            }

            using var stream = File.OpenRead(filePath);
            byte[] hashBytes;
            if (algo == "MD5")
            {
                using var hasher = MD5.Create();
                hashBytes = hasher.ComputeHash(stream);
            }
            else if (algo == "SHA1" || algo == "SHA-1")
            {
                using var hasher = SHA1.Create();
                hashBytes = hasher.ComputeHash(stream);
            }
            else if (algo == "SHA256" || algo == "SHA-256" || algo == "SHA2_256")
            {
                using var hasher = SHA256.Create();
                hashBytes = hasher.ComputeHash(stream);
            }
            else if (algo == "SHA512" || algo == "SHA-512" || algo == "SHA2_512")
            {
                using var hasher = SHA512.Create();
                hashBytes = hasher.ComputeHash(stream);
            }
            else
            {
                throw new ExecutionException($"Unsupported hash algorithm: {algo}. Supported: MD5, SHA1, SHA256, SHA512.");
            }

            string actualHash = Convert.ToHexString(hashBytes).ToLowerInvariant();
            if (actualHash != expectedHash)
            {
                throw new ExecutionException($"File integrity check failed: Expected hash '{expectedHash}' but got '{actualHash}' for file '{filePath}'.");
            }
        }
    }
}
