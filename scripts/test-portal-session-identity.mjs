import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const scriptRoot = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptRoot, '..');
const modulePath = path.join(repoRoot, 'src', 'ETL-SQL.Portal', 'wwwroot', 'js', 'session-identity.js');
const { getSessionIdentity, hasRole, renderSessionIdentity } = await import(pathToFileURL(modulePath));

function token(payload) {
  const encoded = Buffer.from(JSON.stringify(payload)).toString('base64url');
  return `header.${encoded}.signature`;
}

const identity = getSessionIdentity(token({ sub: '41', unique_name: 'Ada Lovelace', email: 'ada@example.test', role: ['Admin', 'Publisher'] }));
assert.equal(identity.displayName, 'Ada Lovelace');
assert.equal(identity.subject, '41');
assert.deepEqual(identity.roles, ['Admin', 'Publisher']);
assert.equal(hasRole(identity, 'admin'), true);
assert.equal(hasRole(identity, 'Viewer'), false);

const claimIdentity = getSessionIdentity(token({
  sub: '99',
  'http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name': 'Grace Hopper',
  'http://schemas.microsoft.com/ws/2008/06/identity/claims/role': 'OrchestratorManager'
}));
assert.equal(claimIdentity.displayName, 'Grace Hopper');
assert.equal(hasRole(claimIdentity, 'OrchestratorManager'), true);

const element = { textContent: '', title: '', dataset: {} };
renderSessionIdentity(identity, element);
assert.equal(element.textContent, 'Ada Lovelace');
assert.equal(element.title, 'Ada Lovelace — ada@example.test');
assert.equal(element.dataset.subject, '41');

for (const page of ['index.html', 'admin.html', 'docs.html', 'orchestrator.html']) {
  const source = fs.readFileSync(path.join(repoRoot, 'src', 'ETL-SQL.Portal', 'wwwroot', page), 'utf8');
  assert.match(source, /session-identity\.js/);
  assert.doesNotMatch(source, /payload\.sub\s*\|\|\s*payload\.unique_name/);
}
const admin = fs.readFileSync(path.join(repoRoot, 'src', 'ETL-SQL.Portal', 'wwwroot', 'admin.html'), 'utf8');
assert.match(admin, /e\.username/);
assert.doesNotMatch(admin, /e\.userName/);
console.log('Portal shared session identity and audit identity rendering passed.');
