/**
 * report-runtime.js — Phase 9C
 *
 * Dual-mode bootstrap:
 *   - VS Code WebviewPanel: reads `window.__MANIFEST__` (injected by reportPreviewPanel.ts)
 *   - Web (Phase 9D): calls /api/manifest
 */
(function () {
    'use strict';

    /**
     * Entry point: obtain manifest and render all visuals + pages.
     */
    async function boot() {
        let manifest;
        if (window.__MANIFEST__) {
            manifest = window.__MANIFEST__;
        } else {
            try {
                const res = await fetch('/api/manifest');
                manifest  = await res.json();
            } catch (e) {
                document.getElementById('root').innerHTML =
                    '<p class="error">Failed to load manifest: ' + e.message + '</p>';
                return;
            }
        }
        renderManifest(manifest);
    }

    function renderManifest(manifest) {
        const root = document.getElementById('root');
        if (!root) return;
        root.innerHTML = '';

        if (manifest.pages && manifest.pages.length > 0) {
            manifest.pages.forEach(page => renderPage(root, page, manifest));
        } else {
            (manifest.visuals || []).forEach(v => renderVisual(root, v));
        }

        renderFooter(root, manifest);
    }

    function renderPage(container, page, manifest) {
        const section = document.createElement('section');
        section.className = 'page';

        const heading = document.createElement('h2');
        heading.textContent = page.name;
        section.appendChild(heading);

        // Render visuals in slot order
        const sortedSlots = Object.keys(page.slotMap).sort();
        sortedSlots.forEach(slot => {
            const visualName = page.slotMap[slot];
            const visual     = (manifest.visuals || []).find(
                v => v.name.toLowerCase() === visualName.toLowerCase()
            );
            if (visual) renderVisual(section, visual);
        });

        container.appendChild(section);
    }

    function renderVisual(container, visual) {
        const card = document.createElement('div');
        card.className = 'visual-card';

        const title = document.createElement('h3');
        title.textContent = visual.name;
        card.appendChild(title);

        const type = (visual.visualType || '').toUpperCase();

        switch (type) {
            case 'TABLE':  renderTable(card, visual);  break;
            case 'CARD':   renderCard(card, visual);   break;
            case 'SLICER': renderSlicer(card, visual); break;
            default:       renderChart(card, visual);  break;
        }

        container.appendChild(card);
    }

    // ── Chart (BAR / LINE / SCATTER / PIE) ─────────────────────────────────

    function renderChart(container, visual) {
        if (!visual.chartConfig) {
            container.appendChild(noDataEl('No chart config available'));
            return;
        }

        let config;
        try {
            config = typeof visual.chartConfig === 'string'
                ? JSON.parse(visual.chartConfig)
                : visual.chartConfig;
        } catch (e) {
            container.appendChild(noDataEl('Invalid chart config: ' + e.message));
            return;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'chart-wrapper';

        const canvas = document.createElement('canvas');
        wrapper.appendChild(canvas);
        container.appendChild(wrapper);

        if (typeof Chart === 'undefined') {
            container.appendChild(noDataEl('Chart.js not loaded'));
            return;
        }
        new Chart(canvas, config);
    }

    // ── Table ───────────────────────────────────────────────────────────────

    function renderTable(container, visual) {
        if (!visual.columns || visual.columns.length === 0) {
            container.appendChild(noDataEl('No data'));
            return;
        }

        const wrapper = document.createElement('div');
        wrapper.className = 'table-wrapper';

        const table = document.createElement('table');
        const thead = document.createElement('thead');
        const headerRow = document.createElement('tr');
        visual.columns.forEach(col => {
            const th = document.createElement('th');
            th.textContent = col;
            headerRow.appendChild(th);
        });
        thead.appendChild(headerRow);
        table.appendChild(thead);

        const tbody = document.createElement('tbody');
        (visual.rows || []).forEach(row => {
            const tr = document.createElement('tr');
            visual.columns.forEach((_, ci) => {
                const td = document.createElement('td');
                td.textContent = row[ci] != null ? String(row[ci]) : '';
                tr.appendChild(td);
            });
            tbody.appendChild(tr);
        });
        table.appendChild(tbody);
        wrapper.appendChild(table);
        container.appendChild(wrapper);
    }

    // ── Card ────────────────────────────────────────────────────────────────

    function renderCard(container, visual) {
        const cardEl = document.createElement('div');
        cardEl.className = 'card-value';
        const label = visual.columns && visual.columns[0] ? visual.columns[0] : visual.name;
        const value = visual.rows && visual.rows[0] && visual.rows[0][0] != null
            ? String(visual.rows[0][0])
            : 'No data';
        cardEl.innerHTML = '<span class="card-label">' + escHtml(label) + '</span>' +
                           '<span class="card-number">' + escHtml(value) + '</span>';
        container.appendChild(cardEl);
    }

    // ── Slicer (read-only in VS Code preview) ───────────────────────────────

    function renderSlicer(container, visual) {
        const note = document.createElement('p');
        note.className = 'slicer-note';
        note.textContent = '[Slicer — interactive in ReportPlayer only]';
        container.appendChild(note);
    }

    // ── Footer ──────────────────────────────────────────────────────────────

    function renderFooter(container, manifest) {
        const footer = document.createElement('footer');
        const built  = manifest.builtAt ? new Date(manifest.builtAt).toLocaleString() : '';
        footer.innerHTML = '<small>Built: ' + escHtml(built) + '</small>';
        container.appendChild(footer);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    function noDataEl(msg) {
        const p = document.createElement('p');
        p.className = 'no-data';
        p.textContent = msg;
        return p;
    }

    function escHtml(s) {
        return String(s)
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;');
    }

    // Boot on DOMContentLoaded
    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }
})();
