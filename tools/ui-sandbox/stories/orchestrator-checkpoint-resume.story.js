const rows = {
  mixed: `
    <tr><td><span class="status-badge failure">FAILURE</span></td><td>Aug 9, 2:14 AM</td><td>8m 12s</td><td>84,220</td><td>412 MB</td><td class="history-error-cell">Warehouse timeout after load.</td><td><button class="btn btn-xs btn-outline history-resume-btn" type="button">Resume · load_complete</button></td></tr>
    <tr><td><span class="status-badge failure">FAILURE</span></td><td>Aug 8, 2:00 AM</td><td>44.1s</td><td>0</td><td>96 MB</td><td class="history-error-cell">Source unavailable.</td><td><button class="btn btn-xs btn-outline history-resume-btn" type="button" disabled>Unavailable</button><span class="history-resume-reason">No resumable checkpoint: this run was not persistent or never reached a top-level label.</span></td></tr>
    <tr><td><span class="status-badge success">SUCCESS</span></td><td>Aug 7, 2:00 AM</td><td>6m 49s</td><td>83,910</td><td>387 MB</td><td></td><td><button class="btn btn-xs btn-outline history-resume-btn" type="button" disabled>Unavailable</button><span class="history-resume-reason">Only failed or cancelled runs can resume.</span></td></tr>`,
  resumable: `
    <tr><td><span class="status-badge failure">FAILURE</span></td><td>Aug 9, 2:14 AM</td><td>8m 12s</td><td>84,220</td><td>412 MB</td><td class="history-error-cell">Warehouse timeout after load.</td><td><button class="btn btn-xs btn-outline history-resume-btn" type="button">Resume · load_complete</button></td></tr>`
};

export default {
  id: 'orchestrator-checkpoint-resume',
  title: 'Orchestrator — Checkpoint recovery',
  description: 'History-table recovery states for author-declared persistent checkpoints.',
  fixtures: [
    { id: 'mixed', label: 'Mixed eligibility' },
    { id: 'resumable', label: 'Resumable failure' },
  ],
  async mount(stage, fixtureId) {
    stage.innerHTML = `<section class="card" style="margin:24px;max-width:1040px">
      <div class="card-header"><div><span class="section-kicker">Run history</span><h3>nightly_sales</h3></div></div>
      <div class="history-table-scroll">
        <table class="data-table history-table orchestrator-run-history-table">
          <thead><tr><th>Status</th><th>Start</th><th>Duration</th><th>Rows</th><th>Peak RAM</th><th>Error</th><th>Recovery</th></tr></thead>
          <tbody>${rows[fixtureId] ?? rows.mixed}</tbody>
        </table>
      </div>
    </section>`;
    return { dispose() {} };
  }
};
