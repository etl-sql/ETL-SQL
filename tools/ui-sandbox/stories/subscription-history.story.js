import { importFresh } from '../util.js';

const HISTORY_UI_JS = '/src/ETL-SQL.ReportPortal/wwwroot/js/subscription-history-ui.js';

const fixtures = {
  mixed: [
    { status: 'FAILURE', startTime: '2026-06-04T13:04:00Z', endTime: '2026-06-04T13:04:03Z', rowsProcessed: 0, errorMessage: 'SMTP connection refused.' },
    { status: 'SUCCESS', startTime: '2026-06-03T13:04:00Z', endTime: '2026-06-03T13:04:08Z', rowsProcessed: 1842, errorMessage: null },
    { status: 'CANCELLED', startTime: '2026-06-02T13:04:00Z', endTime: '2026-06-02T13:04:01Z', rowsProcessed: 0, errorMessage: 'Cancelled by operator.' },
  ],
  empty: [],
};

const esc = value => String(value ?? '').replace(/[&<>"']/g, c => ({
  '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
}[c]));

export default {
  id: 'subscription-history',
  title: 'Subscription history',
  subtitle: 'Portal delivery diagnostics',
  fixtures: [
    { id: 'mixed', label: 'Success and failures' },
    { id: 'empty', label: 'No delivery history' },
  ],
  async mount(stage, fixtureId, ctx) {
    stage.classList.add('portal-page');
    const mod = await importFresh(HISTORY_UI_JS);
    const card = document.createElement('div');
    card.className = 'modal-card modal-lg history-modal';
    card.innerHTML = `<div class="modal-header">
      <div><span class="library-kicker">Subscription</span><h3 class="modal-title">Daily Sales Delivery History</h3></div>
    </div>
    <div class="modal-body">${mod.renderSubscriptionHistory(fixtures[fixtureId], {
      esc,
      formatDate: value => value ? new Date(value).toLocaleString() : '—',
    })}</div>`;
    stage.replaceChildren(card);
    ctx.stat(`${fixtures[fixtureId].length} delivery attempts`);
    return { resize() {} };
  },
};
