// Unit tests for the Orchestrator Access panel (src/ETL-SQL.Portal/wwwroot/js/orchestrator-acl-ui.js).
//
// The property under test: this panel says who may reach an object, so every state it can be in has
// to read as itself. An object with no owner is administrators-only and not "open"; a refusal is a
// refusal and not an empty table; and the editing controls appear only when the server has already
// answered that this caller may administer the object. Each of those collapses into "an empty grants
// table" if it is got wrong, which is the failure this module was written to prevent — and the one a
// screenshot of the sandbox story would not catch.
import path from 'node:path';
import { pathToFileURL } from 'node:url';

const mod = await import(
    pathToFileURL(path.resolve('src/ETL-SQL.Portal/wwwroot/js/orchestrator-acl-ui.js')).href);
const {
    PERMISSIONS, PRINCIPAL_KINDS, OWNER_KINDS, splitPrincipal, ownerLabel, canAdminister,
    canReassignOwner, ownerFormHtml, unownedListHtml, grantRowsHtml, accessPanelHtml,
} = mod;

function assert(cond, msg) { if (!cond) throw new Error('FAIL: ' + msg); }
const esc = s => String(s ?? '').replace(/[&<>"']/g, c =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
const escAttr = esc;

const OWNER_KEY = '9b1c77d2f4e84a1e8c0d6b3a5f27e410';
const job = { name: 'nightly-load', createdBy: `user:${OWNER_KEY}` };
const grants = [
    { principalKind: 'GROUP', principalId: '3f2a9c1d84be47a0b6c25e7f9d031a48', permission: 'EXECUTE' },
    { principalKind: 'USER', principalId: 'c47ee0b18a9d4f3ea1b25c60d9f83a77', permission: 'READ' },
];

// 1. Attribution is stored as `kind:key`, and the halves answer different questions: the kind selects
//    how a grant matches, the key is what it matches on. A bare value is a key, not a kind.
assert(splitPrincipal(`user:${OWNER_KEY}`).kind === 'USER', 'the kind is read from the prefix');
assert(splitPrincipal(`user:${OWNER_KEY}`).key === OWNER_KEY, 'the key is what follows it');
assert(splitPrincipal('GROUP:finance').kind === 'GROUP', 'group attribution keeps its kind');
assert(splitPrincipal(OWNER_KEY).kind === 'USER' && splitPrincipal(OWNER_KEY).key === OWNER_KEY,
    'an unprefixed value is a user key, never a kind');
assert(splitPrincipal('') === null && splitPrincipal(null) === null, 'absent attribution is null, not a blank principal');
assert(splitPrincipal('service:sa:nightly').key === 'sa:nightly', 'only the first separator splits the kind off');

// 2. An unowned object is Admin-only until adopted. Rendering it as blank — or as "none" — invites the
//    reading that nobody owns it and therefore anyone may have it, which is the opposite of the rule.
const unowned = ownerLabel(null);
assert(/administrator/i.test(unowned), 'no owner reads as administrators-only');
assert(!/^\s*$/.test(unowned) && !/^none$/i.test(unowned), 'no owner is never blank or "none"');
assert(ownerLabel(`user:${OWNER_KEY}`) === `User ${OWNER_KEY}`, 'an owner is named by kind and key');

// 3. The client asks the server whether the caller may administer; it does not decide. A successful
//    read of the grants IS that answer, so an error state must never be editable — even when a stale
//    grants array is still in hand, which is exactly the "reachable but not administrable" case.
assert(canAdminister({ job, grants }), 'a successful read is the server saying yes');
assert(!canAdminister({ job, grants, error: 'You can reach this job but cannot administer its grants.' }),
    'a refusal is not editable even with grants already loaded');
assert(!canAdminister({ job, grants: null }), 'grants not yet loaded is not editable');
assert(!canAdminister(null) && !canAdminister(undefined), 'no state is not editable');

// 4. A refusal is rendered as itself. 403 ("you may reach this but not administer it") and 404 ("no
//    such object in your tenant") mean different things and both look like an empty table otherwise.
const refused = accessPanelHtml(
    { job, grants, error: 'This job no longer exists in your tenant.' }, esc, escAttr);
assert(refused.includes('This job no longer exists in your tenant.'), 'the refusal is shown');
assert(!refused.includes('aclGrantBtn'), 'a refusal offers no add form');
assert(!refused.includes('data-revoke-key'), 'a refusal offers no revoke buttons');

// 5. Revoke buttons carry the kind and key of their own row: revoking is routed on both, so crossing
//    them over would revoke a different principal than the one whose row was clicked.
const rows = grantRowsHtml(grants, esc, escAttr, true);
assert(rows.includes(`data-revoke-kind="GROUP" data-revoke-key="${grants[0].principalId}"`),
    'the group row carries its own kind and key');
assert(rows.includes(`data-revoke-kind="USER" data-revoke-key="${grants[1].principalId}"`),
    'the user row carries its own kind and key');
assert(!rows.includes(`data-revoke-kind="USER" data-revoke-key="${grants[0].principalId}"`),
    'kind and key must not cross rows');
assert(grantRowsHtml(grants, esc, escAttr, false).includes('data-revoke-key') === false,
    'a read-only panel renders the grants without revoke controls');

// 6. Principal keys reach the panel from the Orchestrator's store and are operator-supplied, so they
//    are escaped in both text and attribute position.
const hostile = grantRowsHtml(
    [{ principalKind: 'USER', principalId: '"><img src=x onerror=alert(1)>', permission: 'READ' }],
    esc, escAttr, true);
assert(!hostile.includes('<img'), 'a principal key must not escape into markup');
assert(hostile.includes('&lt;img'), 'the escaped form is rendered instead');
assert(!/data-revoke-key="">/.test(hostile), 'the attribute is not terminated early by a quote in the key');

// 7. An object with no grants says what that means to someone who may act on it: the owner and
//    administrators still reach it. "No grants" alone reads as "no access", which is never true.
const empty = accessPanelHtml({ job, grants: [] }, esc, escAttr);
assert(/Only the owner and administrators/.test(empty), 'the empty state names who still has access');
assert(empty.includes('aclGrantBtn'), 'an administrator can still add the first grant');

// 8. Owner and grants are shown together. A grants table without the owner claims to list everyone
//    with access while omitting the one principal who always has it.
const panel = accessPanelHtml({ job, grants }, esc, escAttr);
assert(panel.includes(ownerLabel(job.createdBy)), 'the owner is shown alongside the grants');
assert(panel.includes(grants[0].principalId) && panel.includes(grants[1].principalId), 'every grant is listed');

// 9. The offered vocabularies are the ones the Orchestrator accepts. A value the API rejects would be
//    a form that produces a 400 on submit rather than a choice that cannot be made.
assert(PERMISSIONS.join(',') === 'READ,EXECUTE,OVERRIDE,MANAGE', 'the permission ladder is widest last');
assert(PRINCIPAL_KINDS.join(',') === 'USER,GROUP,SERVICE', 'the principal kinds match the API');
for (const permission of PERMISSIONS) assert(panel.includes(`value="${permission}"`), `${permission} is offerable`);
for (const kind of PRINCIPAL_KINDS) assert(panel.includes(`value="${kind}"`), `${kind} is offerable`);

// 10. Ownership is administrator-only, so the reassignment control follows the viewer's role rather
//     than the grants read. It still decides only what is drawn: the Orchestrator refuses anyone else.
assert(canReassignOwner({ job, grants, isAdmin: true }), 'an administrator may reassign');
assert(!canReassignOwner({ job, grants }), 'a non-administrator is not offered reassignment');
assert(!canReassignOwner({ job, grants, isAdmin: true, error: 'Not found.' }),
    'a refused panel offers no reassignment');
assert(ownerFormHtml({ job, grants }) === '', 'no control for a non-administrator');
assert(ownerFormHtml({ job, grants, isAdmin: true }).includes('aclOwnerBtn'), 'the control is rendered');

// 11. A group can be granted but cannot own — the decision compares ownership against one caller's
//     key, so a group owner would read as owned and behave as unowned. Offering it would be a form
//     whose only outcome is a 400.
assert(OWNER_KINDS.join(',') === 'USER,SERVICE', 'only users and services may own');
assert(!ownerFormHtml({ job, grants, isAdmin: true }).includes('value="GROUP"'),
    'GROUP is never offered as an owner');
assert(PRINCIPAL_KINDS.includes('GROUP'), 'a group is still grantable');

// 12. An unowned object still offers reassignment — that is the adoption path, and the same act as a
//     transfer. Both appear on the panel of an administrator.
const orphan = { job: { name: 'orphaned', createdBy: null }, grants: [], isAdmin: true };
const orphanPanel = accessPanelHtml(orphan, esc, escAttr);
assert(/administrator/i.test(orphanPanel), 'the unowned state is still stated');
assert(orphanPanel.includes('aclOwnerBtn'), 'an unowned object can be adopted from the panel');

// 13. The unowned list names what it found, and says so plainly when it found nothing — "no unowned
//     objects" is the good news here, so rendering blank would read as reassurance not yet earned.
const unownedList = unownedListHtml(
    [{ kind: 'JOB', name: 'nightly-load' }, { kind: 'SCHEDULE', name: 'hourly' }], esc);
assert(unownedList.includes('nightly-load') && unownedList.includes('hourly'), 'every unowned object is listed');
assert(unownedList.includes('JOB') && unownedList.includes('SCHEDULE'), 'each is named by kind');
assert(/has a recorded owner/.test(unownedListHtml([], esc)), 'the empty case is stated');
assert(/has a recorded owner/.test(unownedListHtml(null, esc)), 'null is tolerated');
assert(!unownedListHtml([{ kind: 'JOB', name: '<img src=x>' }], esc).includes('<img'),
    'object names are escaped');

console.log('orchestrator-acl-ui tests passed');
