import { importFresh } from '../util.js';

export default {
  id: 'feedback',
  title: 'Feedback and dialogs',
  subtitle: 'Shared accessible notifications, confirmations, and validated prompts',
  fixtures: [
    { id: 'controls', label: 'Interactive controls' },
  ],
  async mount(stage, _fixtureId, ctx) {
    await importFresh('/src/ETL-SQL.ReportRuntime/Resources/Shared/feedback.js');
    stage.innerHTML = `<section class="card" style="max-width:760px;margin:24px auto;padding:24px">
      <span class="section-kicker">Shared interaction system</span>
      <h2>Feedback and dialogs</h2>
      <p>Exercise keyboard focus, Escape/cancel behavior, validation, impact copy, and live-region announcements.</p>
      <div style="display:flex;flex-wrap:wrap;gap:10px">
        <button class="btn btn-primary" data-demo="success">Success toast</button>
        <button class="btn btn-outline" data-demo="error">Error toast</button>
        <button class="btn btn-outline" data-demo="confirm">Destructive confirmation</button>
        <button class="btn btn-outline" data-demo="prompt">Validated prompt</button>
      </div>
      <p data-result role="status" aria-live="polite" style="margin-top:18px"></p>
    </section>`;
    const feedback = window.ETLSQLFeedback;
    const result = stage.querySelector('[data-result]');
    stage.querySelector('[data-demo="success"]').addEventListener('click', () => feedback.notify('The governed change completed.', { title: 'Saved', tone: 'success', auditAction: 'sandbox.success' }));
    stage.querySelector('[data-demo="error"]').addEventListener('click', () => feedback.notify('The service rejected this request.', { title: 'Request failed', tone: 'error', duration: 0 }));
    stage.querySelector('[data-demo="confirm"]').addEventListener('click', async () => {
      const accepted = await feedback.confirm('Delete the selected catalog object?', { title: 'Delete catalog object', impact: 'This removes the object and stops its future schedules.', confirmLabel: 'Delete object', danger: true, auditAction: 'sandbox.delete' });
      result.textContent = accepted ? 'Deletion confirmed.' : 'Deletion cancelled.';
    });
    stage.querySelector('[data-demo="prompt"]').addEventListener('click', async () => {
      const value = await feedback.prompt('Explain why this governed exception is required.', { title: 'Exception justification', label: 'Justification', multiline: true, required: true, minLength: 12, confirmLabel: 'Submit justification', auditAction: 'sandbox.justification' });
      result.textContent = value ? `Submitted ${value.length} characters.` : 'Submission cancelled.';
    });
    ctx.stat('Canonical feedback.js — focus-trapped dialogs and live-region toasts');
    return { dispose() { document.querySelectorAll('.etlsql-feedback-backdrop,.etlsql-feedback-toasts').forEach(element => element.remove()); }, resize() {} };
  },
};
