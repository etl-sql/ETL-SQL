/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Zero-trust client-side save checks shared by every Studio host.
 */

import { encryptClientPassword } from './connection-wizard.js';

export function detectPlaintextSecrets(scriptText) {
    if (!scriptText || typeof scriptText !== 'string') return [];
    const findings = [];
    const patterns = [
        { label: 'Plaintext Password', regex: /\b(PASSWORD|PWD)\s*=\s*(['"])(?!ENC:|SECRET:|SHARED:)(.+?)\2/gi },
        { label: 'Plaintext Secret / API Key', regex: /\b(API_KEY|APIKEY|SECRET_KEY|SECRETKEY|TOKEN|ACCESS_TOKEN)\s*=\s*(['"])(?!ENC:|SECRET:|SHARED:)(.+?)\2/gi }
    ];
    for (const { label, regex } of patterns) {
        let match;
        while ((match = regex.exec(scriptText)) !== null) {
            const value = match[3] || match[0];
            const valueOffset = match[0].indexOf(value);
            findings.push({ label, start: match.index + valueOffset, end: match.index + valueOffset + value.length, value });
        }
    }
    return findings;
}

export async function secureStudioScriptForSave(scriptText, passphrase, encrypt = encryptClientPassword) {
    if (!passphrase?.trim()) throw new Error('A passphrase is required to encrypt credentials.');
    const findings = detectPlaintextSecrets(scriptText);
    let secured = scriptText;
    for (const finding of findings.sort((left, right) => right.start - left.start)) {
        const encrypted = await encrypt(finding.value, passphrase);
        if (!encrypted?.startsWith('ENC:')) {
            throw new Error('Credential encryption is unavailable. The script was not changed or saved.');
        }
        secured = secured.slice(0, finding.start) + encrypted + secured.slice(finding.end);
    }
    return secured;
}
