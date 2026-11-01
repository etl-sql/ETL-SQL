import { readFileSync } from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

// Both independently certified packages must retain the same published Windows file inventory.
export function checkWindowsPackageOrigin(msi, native, sha, version) {
    if (!/^[a-f0-9]{40}$/.test(sha) || !/^\d+\.\d+\.\d+$/.test(version) ||
        msi.sourceSha !== sha || native.sourceSha !== sha || native.version !== version ||
        msi.windowsPayload?.sourceSha !== sha || msi.windowsPayload?.version !== version ||
        !/^[a-f0-9]{64}$/.test(msi.windowsPayload?.manifestSha256 ?? '') ||
        native.windowsPayloadSha256 !== msi.windowsPayload.manifestSha256) {
        throw new Error('MSI and native Windows packages do not identify the same shared payload.');
    }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    try {
        if (process.argv.length !== 6) throw new Error('Usage: check-windows-package-origin.mjs <msi-inputs.json> <native-selection.json> <sha> <version>');
        const [msi, native, sha, version] = process.argv.slice(2);
        const json = file => JSON.parse(readFileSync(file, 'utf8').replace(/^\uFEFF/, ''));
        checkWindowsPackageOrigin(json(msi), json(native), sha, version);
        console.log('MSI and native Windows packages retain the same shared payload hash.');
    } catch (error) { console.error(error.message); process.exitCode = 1; }
}
