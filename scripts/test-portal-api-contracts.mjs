import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const scriptRoot = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(scriptRoot, '..');
const generatedPath = path.join(repoRoot, 'src', 'ETL-SQL.Portal', 'wwwroot', 'js', 'api-contracts.generated.js');
// The admin page's behaviour used to live in an inline <script type="module"> block in
// admin.html and now lives in a file of its own, so the casing assertions below read the file.
const adminPath = path.join(repoRoot, 'src', 'ETL-SQL.Portal', 'wwwroot', 'js', 'pages', 'admin.js');
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

const admin = fs.readFileSync(adminPath, 'utf8');
assert.doesNotMatch(admin, /\bu\.userName\b|\bbody\.userName\b/);
assert.match(admin, /\bu\.username\b/);
assert.match(admin, /\bbody\.username\b/);
console.log('Portal critical API contracts and Admin Users casing passed.');
