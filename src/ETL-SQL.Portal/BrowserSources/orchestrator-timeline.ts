import { escHtml, fmtDt, renderCalendarTimeline } from './orchestrator-admin-ui.js';

/**
 * Today's schedule as a 24-hour strip, or the next seven days as a calendar, over the loaded job
 * catalog. The strip is plain markup: one row per job due today, with a button placed along the day
 * at its next run. It is keyboard reachable, and it does not depend on the chart renderer, which
 * draws value bars and node graphs but has no time axis.
 */
type TimelineJob = Pick<JobDefinition, 'name' | 'interval' | 'unit' | 'atTime' | 'nextRun' | 'isEnabled'>;

interface TimelineOptions<T extends TimelineJob> {
    root: Document;
    open(job: T): void;
}

const DAY_MS = 24 * 60 * 60 * 1000;

function scheduleText(job: TimelineJob): string {
    if (!job.interval) return 'Manual';
    const unit = (job.unit || 'HOUR').toLowerCase();
    const plural = job.interval === 1 ? unit.replace(/s$/, '') : unit.endsWith('s') ? unit : unit + 's';
    return `Every ${job.interval} ${plural}${job.atTime ? ` at ${job.atTime}` : ''}`;
}

/** The rows of today's strip: each job whose next run falls on the local calendar day of `now`. */
export function todaysRuns<T extends TimelineJob>(jobs: T[], now: Date): Array<{ job: T; start: number; offset: number }> {
    const dayStart = new Date(now.getFullYear(), now.getMonth(), now.getDate()).getTime();
    return jobs.flatMap(job => {
        const start = job.nextRun ? Date.parse(job.nextRun) : NaN;
        return start >= dayStart && start < dayStart + DAY_MS ? [{ job, start, offset: (start - dayStart) / DAY_MS }] : [];
    }).sort((a, b) => a.start - b.start);
}

export function renderDayStrip(jobs: TimelineJob[], now: Date): string {
    const runs = todaysRuns(jobs, now);
    if (!runs.length) return '<div class="orch-day-empty">No runs are scheduled for the rest of today.</div>';
    const time = (value: number) => new Date(value).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    return `<div class="orch-day-strip" role="list" aria-label="Jobs scheduled today">
        <div class="orch-day-axis" aria-hidden="true"><span>00:00</span><span>06:00</span><span>12:00</span><span>18:00</span><span>24:00</span></div>
        ${runs.map(run => `<div class="orch-day-row" role="listitem">
            <span class="orch-day-name">${escHtml(run.job.name)}</span>
            <div class="orch-day-track"><button type="button" class="orch-day-run${run.job.isEnabled ? '' : ' disabled-job'}"
              style="left:${(Math.min(run.offset, 0.94) * 100).toFixed(2)}%" data-job="${escHtml(run.job.name)}"
              title="${escHtml(`${run.job.name} — ${scheduleText(run.job)} — Next: ${fmtDt(run.job.nextRun)}${run.job.isEnabled ? '' : ' (disabled)'}`)}">${escHtml(time(run.start))}</button></div>
          </div>`).join('')}
      </div>`;
}

export function createJobTimeline<T extends TimelineJob>(options: TimelineOptions<T>) {
    const { root } = options;
    const lifetime = new AbortController();
    const box = (id: string): HTMLElement => {
        const found = root.getElementById(id);
        if (!found) throw new Error(`Missing timeline element: ${id}`);
        return found;
    };
    const ganttButton = box('timelineModeGanttBtn'), calendarButton = box('timelineModeCalendarBtn');
    const ganttWrap = box('timelineGanttWrap'), calendarWrap = box('timelineCalendarWrap'), strip = box('ganttChart');
    let mode: 'gantt' | 'calendar' = 'gantt';
    let jobs: T[] = [];
    function draw() {
        if (mode === 'gantt') strip.innerHTML = renderDayStrip(jobs, new Date());
        else calendarWrap.innerHTML = renderCalendarTimeline(jobs, 7);
    }

    function show(next: 'gantt' | 'calendar') {
        mode = next;
        ganttButton.classList.toggle('active', next === 'gantt');
        calendarButton.classList.toggle('active', next === 'calendar');
        ganttWrap.style.display = next === 'gantt' ? '' : 'none';
        calendarWrap.style.display = next === 'calendar' ? '' : 'none';
        box('timelineKicker').textContent = next === 'gantt' ? "Today's schedule" : 'Multi-Day Schedule';
        box('timelineTitle').textContent = next === 'gantt' ? 'Job Timeline (24h)' : '7-Day Run Calendar';
        draw();
    }

    ganttButton.addEventListener('click', () => show('gantt'), { signal: lifetime.signal });
    calendarButton.addEventListener('click', () => show('calendar'), { signal: lifetime.signal });
    for (const host of [strip, calendarWrap])
        host.addEventListener('click', event => {
            const item = event.target instanceof Element ? event.target.closest<HTMLElement>('[data-job]') : null;
            const job = jobs.find(candidate => candidate.name === item?.dataset.job);
            if (job) options.open(job);
        }, { signal: lifetime.signal });

    return {
        render(loaded: T[]) { if (!lifetime.signal.aborted) { jobs = loaded; draw(); } },
        /** The catalog is unavailable: drop the bars and calendar rather than leave the last ones up. */
        clear() { jobs = []; strip.replaceChildren(); calendarWrap.replaceChildren(); },
        dispose() { lifetime.abort(); jobs = []; }
    };
}
