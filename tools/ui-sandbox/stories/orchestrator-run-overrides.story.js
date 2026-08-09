function overrideRow(name = '', value = '') {
  return `<div class="run-override-row">
    <div class="form-group"><label>Variable<input class="run-override-name" type="text" placeholder="@start_date" value="${name}"></label></div>
    <div class="form-group"><label>Value<input class="run-override-value" type="text" placeholder="2026-08-01" value="${value}"></label></div>
    <button class="btn-icon run-override-remove" type="button" title="Remove variable" aria-label="Remove variable">✕</button>
  </div>`;
}

export default {
  id: 'orchestrator-run-overrides',
  title: 'Orchestrator — One-run overrides',
  description: 'Backfill form that applies input variables to one run without editing the saved job.',
  fixtures: [
    { id: 'blank', label: 'Ordinary run' },
    { id: 'backfill', label: 'Backfill variables' },
  ],
  async mount(stage, fixtureId) {
    const seeded = fixtureId === 'backfill'
      ? overrideRow('@start_date', '2026-08-01') + overrideRow('@region', 'North America')
      : '';
    stage.innerHTML = `<div class="modal-card modal-md" style="margin:24px auto">
      <div class="modal-header">
        <div>
          <span class="library-kicker">One-run execution</span>
          <h3 class="modal-title">Run nightly_sales</h3>
          <p class="modal-subtitle">Optionally override input variables for this run only. The saved job is not edited.</p>
        </div>
        <button class="btn-icon" title="Close" type="button">✕</button>
      </div>
      <div class="modal-body">
        <div class="run-override-head">
          <div><strong>Variable overrides</strong><p>Use the script variable name and the same value text accepted by <code>--var</code>.</p></div>
          <button class="btn btn-sm btn-outline" id="storyAddOverride" type="button">+ Add variable</button>
        </div>
        <div id="storyOverrideRows">${seeded}</div>
        <p class="run-override-security">Override names—not values—are written to the audit trail. Secret references are redacted before operational logging.</p>
      </div>
      <div class="modal-actions"><button class="btn btn-primary" type="button">Run now</button><button class="btn btn-outline" type="button">Cancel</button></div>
    </div>`;

    const rows = stage.querySelector('#storyOverrideRows');
    const bindRemove = () => rows.querySelectorAll('.run-override-remove').forEach(button => {
      button.onclick = () => button.closest('.run-override-row').remove();
    });
    const add = stage.querySelector('#storyAddOverride');
    add.onclick = () => { rows.insertAdjacentHTML('beforeend', overrideRow()); bindRemove(); };
    bindRemove();
    return { dispose() { add.onclick = null; } };
  },
};
