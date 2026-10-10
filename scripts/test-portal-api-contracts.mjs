import assert from 'node:assert/strict';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const scriptRoot = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptRoot, '..');
const generatedPath = path.join(repoRoot, 'src', 'ETL-SQL.Portal', 'wwwroot', 'js', 'api-contracts.generated.js');
const { assertApiContract } = await import(pathToFileURL(generatedPath));

const user = {
  id: 1,
  username: 'admin',
  isActive: true,
  roles: ['Admin'],
  groups: [],
  version: 1
};
assert.equal(assertApiContract('user', user), user);
assert.equal(assertApiContract('userCatalog', { items: [user], total: 1, page: 1, pageSize: 25 }).total, 1);
assert.throws(
  () => assertApiContract('user', { ...user, username: undefined, userName: 'admin' }),
  /API contract user failed at \$\.username/
);
assert.throws(
  () => assertApiContract('jobStatus', { jobId: 'job-1', status: 'Pending' }),
  /createdAt/
);
assert.equal(assertApiContract('jobAccepted', { jobId: 'job-1' }).jobId, 'job-1');

// Exercise the request crossing the actual typed API boundary. Variable names and module
// placement do not prove that the server receives its canonical username field.
let sentBody;
globalThis.sessionStorage = { getItem: () => null };
globalThis.window = { location: { origin: 'https://portal.test', href: 'https://portal.test/admin.html' },
  fetch: async (_url, options) => {
    sentBody = JSON.parse(options.body);
    return Response.json({ ...user, email: '', mustChangePassword: true, createdAt: '2026-10-08T12:00:00Z' }, { status: 201 });
  } };
const { adminUsersApi } = await import('../src/ETL-SQL.Portal/wwwroot/js/admin-users-api.js');
assert.equal((await adminUsersApi.create({ username: 'admin', email: '', password: 'temporary-test-password', role: 'Admin' })).username, 'admin');
assert.equal(sentBody.username, 'admin');
assert.equal(Object.hasOwn(sentBody, 'userName'), false);
console.log('Portal critical API contracts and Admin Users request casing passed.');
