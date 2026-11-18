# Framed Spill Encryption Format (`ETLSPL03`) and Compression Level Defaults

**Status:** Accepted  
**Applies to:** v0.23.0  
**Supersedes:** `ETLSPL02` whole-file AES-CBC + trailing HMAC-SHA256 verification for newly written spill chunks (`ETLSPL02` remains readable for saved sessions).

## Context

`ETLSPL02` encrypts each spill chunk as a single `AES-256-CBC` stream followed by a 32-byte trailing `HMAC-SHA256` tag over the 58-byte header and full ciphertext. To avoid exposing unverified plaintext, `AuthenticatedSpillEncryption.CreateReaderAsync` pre-scans the entire file, reads the final 32 bytes, computes HMAC across all ciphertext, seeks back to offset 58, and then opens the decrypting reader. On multi-GB spill chunks this doubles sequential I/O latency on reader open and requires a seekable stream. Furthermore, `ETLSPL02` does not bind chunk identity (`chunkName`) or frame boundaries, and `SpillStore` currently hardcodes `CompressionLevel.Optimal` (zlib level 6) for every spill chunk.

## Decision

### 1. Wire Format: `ETLSPL03` Single-Pass Framed `AES-256-GCM`

All newly written encrypted spill chunks use format `ETLSPL03`, built exclusively on .NET BCL cryptographic primitives (`AesGcm`, `HKDF`, `RandomNumberGenerator`, `CryptographicOperations`). No third-party crypto dependency is introduced.

#### Stream Header (`64` bytes fixed, big-endian integers)

| Offset | Length | Field | Description |
| :--- | :--- | :--- | :--- |
| `0` | `8` | `Magic` | ASCII bytes `"ETLSPL03"` |
| `8` | `1` | `Codec` | `1` = JSON lines, `2` = Apache Arrow IPC |
| `9` | `1` | `Compression` | `0` = None, `1` = GZip (compress-before-encrypt; applied inside frame plaintext) |
| `10` | `4` | `MaxFramePlaintextBytes` | Big-endian `uint32`. Writer default: `131072` (`128 KiB`). Reader allowed range: `[4096, 1048576]` (`4 KiB`–`1 MiB`). |
| `14` | `32` | `Salt` | CSPRNG per-chunk HKDF salt (`RandomNumberGenerator.Fill`) |
| `46` | `16` | `ChunkId` | CSPRNG per-chunk identifier; bytes `46..49` form the 4-byte `NoncePrefix` |
| `62` | `2` | `Flags` | Big-endian `uint16` reserved header flags; must be `0x0000` (reader fails closed otherwise) |

#### Key Derivation and Chunk Identity Binding

- **Encryption key:** `HKDF.DeriveKey(HashAlgorithmName.SHA256, sessionKey, 32, Header[14..46], "ETL-SQL spill v3"u8)`.
- **Zero-wipe:** Derived key bytes and scratch buffers are wiped with `CryptographicOperations.ZeroMemory` on writer/reader disposal and error paths.
- **Chunk Identity (`ChunkIdentity`):** `Header[0..64] || BE32(utf8ChunkName.Length) || utf8ChunkName`. Binding `utf8ChunkName` into every frame's AAD prevents renaming or splicing valid frames across different spill chunks under the same session key.

#### Frame Format

Following the 64-byte header is a contiguous sequence of `AES-256-GCM` frames indexed sequentially `0, 1, 2, ...`:

| Offset | Length | Field | Description |
| :--- | :--- | :--- | :--- |
| `0` | `8` | `FrameIndex` | Big-endian `uint64`, starting at `0`, strictly incrementing by `+1` |
| `8` | `1` | `FrameFlags` | Bit `0x01` = `IS_FINAL`. All other bits (`0xFE`) reserved and must be `0`. |
| `9` | `4` | `PayloadLength` | Big-endian `uint32` length of ciphertext (equal to plaintext length) |
| `13` | `16` | `Tag` | 128-bit `AES-GCM` authentication tag |
| `29` | `PayloadLength` | `Ciphertext` | Encrypted payload bytes |

Frame payload rules:
- Non-final frames (`(FrameFlags & 0x01) == 0`): `1 <= PayloadLength <= MaxFramePlaintextBytes`.
- Final frame (`(FrameFlags & 0x01) == 0x01`): `0 <= PayloadLength <= MaxFramePlaintextBytes`. An empty chunk writes a single final frame (`FrameIndex = 0`, `FrameFlags = 0x01`, `PayloadLength = 0`).

Per-frame `AesGcm` inputs for frame `i`:
- **Nonce (`12` bytes):** `NoncePrefix (Header[46..50]) || BE64(FrameIndex)` — guaranteed unique for a given derived key because `FrameIndex` is strictly monotonic (limit `2^64` frames, i.e., far beyond realistic spill sizes) and every chunk draws a fresh CSPRNG salt + `ChunkId`.
- **Associated Data (`AAD`):** `ChunkIdentity || FrameHeader_i (13 bytes)` (`FrameIndex || FrameFlags || PayloadLength`).
- **Plaintext / Ciphertext:** Compression (`GZipStream`) wraps *inside* encryption; ciphertext is authenticated by `Tag` before any plaintext byte of frame `i` is returned to the caller.

#### Single-Pass Reader Fail-Closed Contract

1. Dispatch on the 8-byte magic header: `"ETLSPL03"` selects the single-pass framed reader; `"ETLSPL02"` selects the legacy CBC+HMAC reader for backwards compatibility with existing saved sessions. Any other magic fails closed with `ExecutionException`.
2. Verify header bounds (`Flags == 0`, `MaxFramePlaintextBytes` in `[4096, 1048576]`, `Codec` / `Compression` match expected values).
3. On demand per frame (zero full-file pre-scan, zero backward seek):
   - Expect `FrameIndex == expectedIndex` (reject reordered, duplicated, or skipped frames).
   - Validate `FrameFlags` (only `0x00` or `0x01`) and `PayloadLength` (`<= MaxFramePlaintextBytes`, non-zero unless final).
   - Read exact `Tag` (16 bytes) and `Ciphertext` (`PayloadLength` bytes); EOF mid-frame fails closed.
   - Decrypt and verify via `AesGcm.Decrypt(nonce, ciphertext, tag, plaintext, aad)`. Any authentication failure throws `ExecutionException` without yielding unverified bytes from that frame.
   - Upon observing `IS_FINAL` (`FrameFlags == 0x01`), probe the underlying stream for EOF; any trailing byte after the final frame fails closed. Reaching EOF without observing an authenticated `IS_FINAL` frame also fails closed (prefix truncation protection).

### 2. Compression Level Tradeoff & Default Selection

Measured on 100,000 rows (`SpillCompressionAssessmentTests`, Apache Arrow IPC `ColumnBatch` chunks, Windows x64 .NET 10):

**Encrypted (`SpillEncryptionEnabled = true`, `ETLSPL02` baseline encryption active):**

| Dataset (100k rows) | Level | Physical Bytes | Ratio vs None | Write Wall (ms) | Write CPU (ms) | Read Wall (ms) | Read CPU (ms) |
| :--- | :--- | ---: | ---: | ---: | ---: | ---: | ---: |
| Compressible | Uncompressed | 8,525,498 | 1.0000 | 244 | 250.0 | 90 | 78.1 |
| Compressible | `CompressionLevel.Fastest` | 1,566,570 | 0.1838 | 179 | 171.9 | 68 | 62.5 |
| Compressible | `CompressionLevel.Optimal` | 1,010,714 | 0.1186 | 247 | 234.4 | 65 | 78.1 |
| High-entropy | Uncompressed | 9,525,178 | 1.0000 | 168 | 171.9 | 37 | 31.2 |
| High-entropy | `CompressionLevel.Fastest` | 6,599,178 | 0.6928 | 271 | 328.1 | 61 | 93.8 |
| High-entropy | `CompressionLevel.Optimal` | 4,164,298 | 0.4372 | 291 | 328.1 | 62 | 62.5 |

**Unencrypted baseline (`SpillEncryptionEnabled = false`):**

| Dataset (100k rows) | Mode | Physical Bytes | Ratio | Write Wall (ms) | Read Wall (ms) |
| :--- | :--- | ---: | ---: | ---: | ---: |
| Compressible | Uncompressed -> GZip (`Optimal`) | 8,525,392 -> 1,010,609 | 0.1185 | 147 -> 187 | 34 -> 35 |
| High-entropy | Uncompressed -> GZip (`Optimal`) | 9,525,072 -> 4,164,202 | 0.4372 | 148 -> 308 | 36 -> 57 |

**Defaults decision (`SP05`):**
- Keep spill encryption **on by default** and compression **on by default** (`compress-before-encrypt`).
- Replace hardcoded `CompressionLevel.Optimal` in `SpillStore` with configurable compression level selection defaulting to `CompressionLevel.Fastest` under normal/high CPU pressure (`0.7247x` write wall time and `0.7334x` write CPU time vs `Optimal` on compressible encrypted payloads, while still achieving `81.6%` byte reduction and faster encrypted writes than uncompressed due to smaller encrypted I/O), with an adaptive/explicit `Optimal` option when spill free-space headroom tightens or when explicitly configured.

## Consequences

- **Positive:**
  - Single-pass streaming verify-before-use decryption eliminates full-file pre-scan and backward seek on spill reads and works cleanly on non-seekable streams.
  - Truncation, frame reordering, duplication, cross-chunk frame splicing, chunk renaming, wrong session key, and header/flag/payload tampering fail closed immediately on the corrupted frame.
  - Bounded `128 KiB` frame plaintext buffers keep crypto working memory small and predictable.
- **Tradeoff:**
  - Framing adds `29` bytes per frame (`13` B frame header + `16` B GCM tag): `~0.022%` overhead at `128 KiB` frames (`~6` bytes extra in the stream header vs `ETLSPL02`).
  - Existing saved-session `ETLSPL02` chunks remain readable during the compatibility window while new writes adopt `ETLSPL03`.
