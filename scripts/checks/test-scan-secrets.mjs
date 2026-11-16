// scripts/scan-secrets.js against synthetic tokens. Every token is assembled at runtime so this file
// itself never contains a literal the scanner would report.
import assert from 'node:assert/strict';
import { createRequire } from 'node:module';

const require = createRequire(import.meta.url);
const { secretFindings } = require('../scan-secrets.js');

const kinds = text => secretFindings(text).map(finding => finding.kind);
const repeat = (chars, count) => chars.repeat(Math.ceil(count / chars.length)).slice(0, count);
const classic = ['gh', 'p_', repeat('A1b2', 36)].join('');
const fineGrained = ['github', '_pat_', repeat('11AB_cd', 82)].join('');
const header = kind => ['-----BEGIN ', kind, 'PRIVATE KEY-----'].join('');
const aws = ['AK', 'IA', repeat('QZ7', 16)].join('');

assert.deepEqual(kinds(`token = "${classic}"`), ['GitHub token']);
assert.deepEqual(kinds(`token = "${fineGrained}"`), ['GitHub fine-grained token']);
for (const kind of ['', 'RSA ', 'OPENSSH ', 'ENCRYPTED '])
    assert.deepEqual(kinds(header(kind)), ['Private key block'], `${kind || 'PKCS#8 '}private key header`);

// A vendor placeholder is skipped, but only when the matched token is the placeholder.
assert.deepEqual(kinds(['AK', 'IAIOSFODNN7', 'EXAMPLE'].join('')), []);
assert.deepEqual(kinds(`// EXAMPLE configuration: ${aws}`), ['AWS access key id'],
    'A real key on a line that mentions EXAMPLE must still be reported.');

assert.deepEqual(kinds(`${classic} // pragma: allowlist secret`), []);
assert.deepEqual(secretFindings(`first\nsecond ${classic}`).map(finding => finding.line), [2]);
console.log('secret scan pattern checks passed');
