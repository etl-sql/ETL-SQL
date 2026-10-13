// Unit tests for the Admin dataset permissions table (src/ETL-SQL.Portal/wwwroot/js/dataset-acl-ui.js).
//
// The property under test: a dataset's creator holds a real, revocable Owner grant rather than
// standing authorship, so the table must name that user grant and offer the user-revoke route for
// it. A listing that showed group rows only would hide the grant that actually lets the author in.
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const mod = await import(
    pathToFileURL(path.resolve('src/ETL-SQL.Portal/wwwroot/js/dataset-acl-ui.js')).href);
const { isUserGrant, principalName, aclRowsHtml, aclTableHtml } = mod;

function assert(cond, msg) { if (!cond) throw new Error('FAIL: ' + msg); }
const esc = s => String(s ?? '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]));
const escAttr = s => esc(s).replace(/'/g, '&#39;');

// GET /api/datasets/{id}/acl returns both kinds of grant in one array.
const entries = [
    { groupId: 7, groupName: 'Finance', permission: 'Viewer', principalKind: 'Group', userId: null, userName: null },
    { groupId: 0, groupName: '', permission: 'Owner', principalKind: 'User', userId: 42, userName: 'jsmith' },
];

// 1. The two kinds are distinguished by principalKind, not by which id happens to be set.
assert(!isUserGrant(entries[0]), 'a group grant is not a user grant');
assert(isUserGrant(entries[1]), 'a user grant is recognized');
assert(principalName(entries[0]) === 'Finance', 'group grants name the group');
assert(principalName(entries[1]) === 'jsmith', 'user grants name the user');

// 2. A user grant with no resolvable username still identifies the principal, so a grant is never
//    displayed as blank and therefore un-revokable-looking.
assert(principalName({ principalKind: 'User', userId: 9, userName: null }) === 'user 9',
    'a user grant falls back to the id');

// 3. Revoke buttons carry the id for their own route: data-uid drives /acl/user/{id}, data-gid
//    drives /acl/{groupId}. Confusing them would revoke the wrong principal.
const rows = aclRowsHtml(entries, esc, escAttr);
assert(rows.includes('data-gid="7"'), 'group rows carry data-gid');
assert(rows.includes('data-uid="42"'), 'user rows carry data-uid');
assert(!rows.includes('data-uid="7"') && !rows.includes('data-gid="42"'),
    'the two revoke ids must not cross over');

// 4. The creator's Owner grant is visible as such.
assert(rows.includes('jsmith') && rows.includes('Owner'), 'the creator grant is listed with its permission');
assert((rows.match(/<td><span class="chip">User<\/span><\/td>/g) || []).length === 1, 'exactly one user row');
assert((rows.match(/<td><span class="chip">Group<\/span><\/td>/g) || []).length === 1, 'exactly one group row');

// 5. Principal names are escaped — group and user names are operator-supplied.
const hostile = aclRowsHtml(
    [{ principalKind: 'User', userId: 1, userName: '<img src=x onerror=alert(1)>', permission: 'Owner' }],
    esc, escAttr);
assert(!hostile.includes('<img'), 'principal names must be escaped');
assert(hostile.includes('&lt;img'), 'the escaped form is rendered');

// 6. An empty ACL says so rather than rendering an empty table body.
assert(aclTableHtml([], esc, escAttr).includes('No permissions granted.'), 'empty state present');
assert(aclTableHtml(null, esc, escAttr).includes('No permissions granted.'), 'null tolerated');

// 7. The header names the principal column, since rows are no longer all groups.
const table = aclTableHtml(entries, esc, escAttr);
assert(table.includes('<th>Principal</th>') && table.includes('<th>Type</th>'),
    'the table distinguishes principal and type');

console.log('dataset-acl-ui tests passed');
