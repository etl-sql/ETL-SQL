import fs from 'node:fs/promises';

// ZIP central-directory fields: https://pkware.cachefly.net/webdocs/casestudies/APPNOTE.TXT
// VSCE inherits host file modes. Cross-packaging on Windows needs explicit Unix executable bits.
// Change only the metadata of our new, unsigned temporary archive; payload bytes stay untouched.
export async function setVsixExecutableModes(file, executables, { signal } = {}) {
    signal?.throwIfAborted();
    if (!executables.length) return;
    const requested = new Set(executables.map(name => `extension/${name}`));
    if (requested.size !== executables.length || executables.some(name => !/^bin\/ETL-SQL(?:-LSP|-Report|-Player)?(?:\.exe)?$/.test(name)))
        throw new Error('Unexpected VSIX executable path.');
    const archive = await fs.open(file, 'r+');
    try {
        const { size } = await archive.stat();
        const read = async (offset, length) => {
            signal?.throwIfAborted();
            if (!Number.isSafeInteger(offset) || offset < 0 || offset + length > size) throw new Error('Invalid ZIP directory range.');
            const bytes = Buffer.alloc(length);
            let count = 0;
            while (count < length) {
                const result = await archive.read(bytes, count, length - count, offset + count);
                if (!result.bytesRead) throw new Error('Truncated ZIP directory.');
                count += result.bytesRead;
            }
            return bytes;
        };
        const tailOffset = Math.max(0, size - 65557);
        const tail = await read(tailOffset, size - tailOffset);
        let end = -1;
        for (let index = tail.length - 22; index >= 0; index--) {
            if (tail.readUInt32LE(index) === 0x06054b50 && index + 22 + tail.readUInt16LE(index + 20) === tail.length) { end = index; break; }
        }
        if (end < 0 || tail.readUInt32LE(end + 4) !== 0) throw new Error('Unsupported ZIP end record.');
        let count = tail.readUInt16LE(end + 10);
        let length = tail.readUInt32LE(end + 12);
        let offset = tail.readUInt32LE(end + 16);
        let directoryEnd = tailOffset + end;
        if (count === 0xffff || length === 0xffffffff || offset === 0xffffffff) {
            const locator = await read(directoryEnd - 20, 20);
            if (locator.readUInt32LE(0) !== 0x07064b50 || locator.readUInt32LE(4) !== 0 || locator.readUInt32LE(16) !== 1)
                throw new Error('Unsupported ZIP64 locator.');
            directoryEnd = Number(locator.readBigUInt64LE(8));
            const record = await read(directoryEnd, 56);
            if (record.readUInt32LE(0) !== 0x06064b50 || record.readBigUInt64LE(4) < 44n || record.readUInt32LE(16) !== 0 ||
                record.readUInt32LE(20) !== 0 || record.readBigUInt64LE(24) !== record.readBigUInt64LE(32))
                throw new Error('Unsupported ZIP64 directory.');
            count = Number(record.readBigUInt64LE(32));
            length = Number(record.readBigUInt64LE(40));
            offset = Number(record.readBigUInt64LE(48));
        } else if (tail.readUInt16LE(end + 8) !== count) throw new Error('Split ZIP archives are unsupported.');
        if (!Number.isSafeInteger(count) || count > 100000 || !Number.isSafeInteger(length) || offset + length !== directoryEnd)
            throw new Error('Invalid ZIP central directory.');
        const changes = [];
        const seen = new Set();
        const stop = offset + length;
        for (let index = 0; index < count; index++) {
            const header = await read(offset, 46);
            if (header.readUInt32LE(0) !== 0x02014b50) throw new Error('Invalid ZIP entry header.');
            const nameLength = header.readUInt16LE(28);
            const entryLength = 46 + nameLength + header.readUInt16LE(30) + header.readUInt16LE(32);
            if (offset + entryLength > stop) throw new Error('ZIP entry exceeds its directory.');
            const name = (await read(offset + 46, nameLength)).toString('utf8');
            if (requested.has(name)) {
                if (seen.has(name) || ((header.readUInt32LE(38) >>> 16) & 0xf000) === 0xa000)
                    throw new Error(`Unsafe executable entry: ${name}.`);
                seen.add(name);
                header[5] = 3; // Unix "version made by" platform.
                header.writeUInt32LE(((0o100755 << 16) | 0x20) >>> 0, 38);
                changes.push({ offset, header });
            }
            offset += entryLength;
        }
        if (offset !== stop || seen.size !== requested.size) throw new Error('Missing executable or invalid ZIP directory size.');
        for (const change of changes) {
            signal?.throwIfAborted();
            const { bytesWritten } = await archive.write(change.header, 0, change.header.length, change.offset);
            if (bytesWritten !== change.header.length) throw new Error('Could not set VSIX executable modes.');
        }
        await archive.sync();
    } finally { await archive.close(); }
}
