import { createScheduledJobActivityView } from '/src/ETL-SQL.Portal/wwwroot/js/orchestrator-activity-view.js';
import { renderScheduledJobsTable } from '/src/ETL-SQL.Portal/wwwroot/js/orchestrator-job-table.js';

const job = (name, index) => ({ id: `job-${index}`, name, script: 'SELECT 1;', interval: 0, unit: 'HOUR',
    isEnabled: true, maxRetries: 0, retryDelaySeconds: 30, hashPolicy: 'Warn', version: 1, jobType: 0, createdBy: 'user:operator' });
const entries = ['active_load', 'queued_load', 'completed_load', 'failed_load', 'retry_succeeded', 'idle_load'].map((name, index) => ({
    job: job(name, index), runState: index === 0 ? 'Running' : index === 1 ? 'Queued' : 'Idle',
    completedToday: index === 2 || index === 4, failedToday: index === 3 || index === 4
}));

export default {
    id: 'orchestrator-activity',
    title: 'Orchestrator — Metric filters',
    description: 'Native job chips, table, search, and pagination use the same typed controller as the Portal.',
    fixtures: [{ id: 'ready', label: 'Readable jobs' }, { id: 'paged', label: 'Multiple pages' },
        { id: 'denied', label: 'Access denied' }, { id: 'invalid', label: 'Invalid evidence' }],
    async mount(stage, fixtureId) {
        const html = await (await fetch('/src/ETL-SQL.Portal/wwwroot/orchestrator.html')).text();
        const source = new DOMParser().parseFromString(html, 'text/html');
        stage.replaceChildren();
        const styles = document.createElement('style');
        styles.textContent = source.querySelector('style').textContent;
        stage.append(styles);
        const host = document.createElement('section');
        host.style.cssText = 'padding:20px;width:100%;box-sizing:border-box';
        host.append(source.querySelector('.orch-stats-bar').cloneNode(true));
        host.append(source.querySelector('#jobsTableWrap').closest('.card').cloneNode(true));
        host.querySelector('#newJobBtn').remove();
        stage.append(host);
        const dataset = fixtureId === 'paged' ? Array.from({ length: 30 }, (_, index) => ({
            job: job(`paged_${String(index).padStart(2, '0')}`, index), runState: 'Running', completedToday: false, failedToday: false
        })) : entries;
        let changed = false;
        const view = createScheduledJobActivityView({
            root: host,
            async fetchPage(request) {
                if (fixtureId === 'denied') throw new Error('Access denied.');
                if (fixtureId === 'invalid') return { counts: { active: 99 } };
                const current = changed ? dataset.filter(entry => entry.job.name !== 'active_load') : dataset;
                const matching = current.filter(entry =>
                    (request.filter === 'all' || (request.filter === 'active' ? entry.runState === 'Running'
                        : request.filter === 'queued' ? entry.runState === 'Queued' : entry[request.filter]))
                    && (request.status === 'all' || entry.job.isEnabled === (request.status === 'enabled'))
                    && entry.job.name.includes(request.search.toLowerCase()));
                return { asOfUtc: '2026-10-07T12:00:00Z', dayStartUtc: '2026-10-07T00:00:00Z',
                    counts: { active: current.filter(entry => entry.runState === 'Running').length,
                        queued: current.filter(entry => entry.runState === 'Queued').length,
                        completedToday: current.filter(entry => entry.completedToday).length,
                        failedToday: current.filter(entry => entry.failedToday).length },
                    total: matching.length, offset: request.offset, limit: request.limit,
                    items: matching.slice(request.offset, request.offset + request.limit) };
            },
            renderPage: page => { host.querySelector('#jobsTableWrap').innerHTML = renderScheduledJobsTable(page.items.map(entry => entry.job)); },
            clearPage() {}
        });
        const refresh = host.querySelector('#refreshJobsBtn');
        const clickRefresh = () => { changed = true; void view.refresh(); };
        refresh.addEventListener('click', clickRefresh);
        await view.refresh();
        return { dispose() { refresh.removeEventListener('click', clickRefresh); view.dispose(); } };
    }
};
