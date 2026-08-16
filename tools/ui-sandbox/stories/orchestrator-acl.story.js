// Story for the Orchestrator detail panel's Access section.
//
// The four fixtures are the states an operator actually lands in, and the ones worth eyeballing
// because each looks similar and means something different: an owner who can administer, a viewer who
// can only look, an object nobody owns, and a refusal. The last two are the ones a table alone would
// render as "empty" — which is exactly the misreading the panel is written to prevent.

import {
    accessPanelHtml,
    ownerLabel,
    canAdminister,
} from '../../../src/ETL-SQL.Portal/wwwroot/js/orchestrator-acl-ui.js';

const esc = value => String(value ?? '').replace(/[&<>"']/g, character => ({
    '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
}[character]));
const escAttr = esc;

const OWNER_KEY = '9b1c77d2f4e84a1e8c0d6b3a5f27e410';
const ownedJob = { name: 'nightly-load', createdBy: `user:${OWNER_KEY}` };

const grants = [
    { principalKind: 'GROUP', principalId: '3f2a9c1d84be47a0b6c25e7f9d031a48', permission: 'EXECUTE' },
    { principalKind: 'USER', principalId: 'c47ee0b18a9d4f3ea1b25c60d9f83a77', permission: 'READ' },
    { principalKind: 'SERVICE', principalId: 'sa_nightly_runner', permission: 'MANAGE' },
];

const FIXTURES = {
    owner: {
        label: 'Owner — can administer',
        state: {
            job: ownedJob,
            grants,
        },
        note: 'Add form and Revoke buttons present.',
    },
    viewer: {
        label: 'Reachable but not administrable',
        state: {
            job: ownedJob,
            grants,
            error: 'You can reach this job but cannot administer its grants.',
        },
        note: 'Reaching an object is not administering it: the refusal is shown, not an empty table.',
    },
    unowned: {
        label: 'No recorded owner',
        state: {
            job: { name: 'orphaned-load', createdBy: null },
            grants: [],
        },
        note: 'Unowned reads as administrators-only, never as "open".',
    },
    refused: {
        label: 'Not found in your tenant',
        state: {
            job: ownedJob,
            grants: [],
            error: 'This job no longer exists in your tenant.',
        },
        note: 'A tenant-scoped miss reads as "not found", never as "forbidden".',
    },
};

export default {
    id: 'orchestrator-acl',
    title: 'Orchestrator Access',
    subtitle: 'per-object grants + owner',
    fixtures: Object.entries(FIXTURES).map(([id, fixture]) => ({ id, label: fixture.label })),
    async mount(stage, fixtureId, ctx) {
        stage.classList.add('portal-page');
        const fixture = FIXTURES[fixtureId] || FIXTURES.owner;

        stage.innerHTML = `
          <aside class="orch-detail-panel open" style="position:static;width:auto;max-width:640px">
            <div class="orch-detail-header"><h3>${esc(fixture.state.job?.name || 'Job')}</h3></div>
            <div class="orch-detail-body">
              <div class="orch-detail-section">
                <label>Access</label>
                ${accessPanelHtml(fixture.state, esc, escAttr)}
              </div>
            </div>
          </aside>`;

        ctx.stat(`${fixture.note} · editable=${canAdminister(fixture.state)} · owner="${ownerLabel(fixture.state.job?.createdBy)}"`);
        return { dispose() {}, resize() {} };
    },
};
