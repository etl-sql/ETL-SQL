/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Report header, navigation, footer, dialogs, and staged controls.
 */
import { cssClassToken, escHtml, getParam, getStyle, inputTypeForParameter, isOn, safeUrl } from './rt-util.js';
import { feedback, getLastActivePage, isOfflineHost, parameters, pendingParameters, safeRequestAnimationFrame, setLastActivePage, vscode } from './rt-state.js';
import { postParameters } from './rt-transport.js';
import { renderManifest } from './report-runtime.js';
import { buildViewsPicker } from './rt-views.js';
import { executePageOnLoad, isPageVisible, renderLayout } from './rt-layout.js';
import { resizeChartsIn } from './rt-visual.js';
import { updateBodyTheme } from './rt-theme.js';
import type { ActionVisual, ReportAction } from './rt-actions.js';

interface ParameterMeta {
    name: string;
    type?: unknown;
    defaultValue?: string;
    isRequired?: boolean;
}

interface ChromeVisual extends ActionVisual {
    name: string;
    visualType?: string;
    options?: Record<string, string>;
    actions?: ReportAction[];
}

interface NavItem {
    pageName?: string;
    label?: string;
    icon?: string;
    badge?: string;
    isExternalLink?: boolean;
    externalUrl?: string;
    target?: string;
}

interface NavGroup { title?: string; items?: NavItem[] }
interface NavDefinition {
    styles?: Record<string, unknown>;
    activeStyles?: Record<string, unknown>;
    defaultPage?: string;
    navType?: string;
    options?: Record<string, string>;
    items?: NavItem[];
    groups?: NavGroup[];
    pages?: string[];
}

interface ChromePage {
    name: string;
    options?: Record<string, string>;
    [key: string]: unknown;
}

interface ChromeContainer {
    name: string;
    structure?: string;
    slotMap?: Record<string, string>;
    title?: string;
    styles?: Record<string, unknown>;
    [key: string]: unknown;
}

interface ExecutionNode {
    name?: string;
    status?: string;
    durationMs?: number;
    rowsProcessed?: number;
    children?: ExecutionNode[];
}

interface PipelineMessage {
    timestamp: string | number | Date;
    color?: string;
    message: string;
}

interface ChromeManifest {
    title?: string;
    description?: string;
    owner?: string;
    steward?: string;
    certification?: string;
    freshnessStatus?: string;
    lastRefreshed?: string;
    builtAt?: string;
    tags?: string | string[];
    id?: string;
    parameterMetadata?: Record<string, ParameterMeta>;
    parameters?: Record<string, unknown>;
    visuals?: ChromeVisual[];
    nav?: NavDefinition;
    pages?: ChromePage[];
    containers?: ChromeContainer[];
    messages?: PipelineMessage[];
    executionTree?: ExecutionNode | ExecutionNode[];
    error?: string;
    [key: string]: unknown;
}

export function showRequiredParametersModal(requiredList: ParameterMeta[], manifest: ChromeManifest): void {
    const modal = document.createElement('div');
    modal.className = 'required-params-modal';

    const content = document.createElement('div');
    content.className = 'modal-content';

    const title = document.createElement('h2');
    title.textContent = 'Required Parameters';
    content.appendChild(title);

    const desc = document.createElement('p');
    desc.textContent = 'Please provide values for the following mandatory fields to run this report:';
    content.appendChild(desc);

    const grid = document.createElement('div');
    grid.className = 'params-grid';

    const inputs: Record<string, HTMLInputElement> = {};
    requiredList.forEach((meta: ParameterMeta) => {
        const label = document.createElement('label');
        label.textContent = meta.name.startsWith('@') ? meta.name.substring(1) : meta.name;

        const input = document.createElement('input');
        input.setAttribute('aria-label', label.textContent);
        input.type = inputTypeForParameter(meta);
        const currentValue = getParam(manifest.parameters, meta.name) as string || '';
        if (input.type === 'checkbox') input.checked = isOn(currentValue);
        else input.value = currentValue;
        input.placeholder = meta.defaultValue || '';
        input.className = 'modal-input';

        grid.appendChild(label);
        grid.appendChild(input);
        inputs[meta.name] = input;
    });
    content.appendChild(grid);

    const footer = document.createElement('div');
    footer.className = 'modal-footer';

    const runBtn = document.createElement('button');
    runBtn.className = 'header-btn primary';
    runBtn.textContent = 'Run Report';
    runBtn.onclick = () => {
        const updates = { ...parameters }; // Start with current global state
        let allOk = true;
        for (const name in inputs) {
            const input = inputs[name];
            const val = input.type === 'checkbox'
                ? (input.checked ? 'TRUE' : 'FALSE')
                : input.value;
            const meta = (manifest.parameterMetadata as Record<string, ParameterMeta>)[name];

            if (meta.isRequired && !val) {
                input.classList.add('error');
                allOk = false;
            } else {
                input.classList.remove('error');
                updates[name] = val;
            }
        }

        if (allOk) {
            modal.remove();
            postParameters(updates, false).then(m => {
                if (m) renderManifest(m);
            });
        }
    };

    footer.appendChild(runBtn);
    content.appendChild(footer);
    modal.appendChild(content);
    document.body.appendChild(modal);
}

export function renderAutoPanel(container: HTMLElement, manifest: ChromeManifest): void {
    if (!manifest.parameterMetadata) return;

    // Identify parameters that are marked as INPUT but don't have a corresponding visual SLICER
    const visuals = manifest.visuals || [];
    const visualParams = new Set();
    visuals.forEach((v: ChromeVisual) => {
        const type = (v.visualType || '').toUpperCase();
        if ([
            'SLICER', 'MULTISELECT', 'DATEPICKER', 'RELDATEPICKER', 'SLIDER',
            'SEARCH', 'CHECKBOX', 'TEXTBOX', 'NUMBERBOX'
        ].includes(type)) {
            const p = v.options && (v.options['data-parameter'] || v.options['PARAMETER'] || v.options['parameter']);
            if (p) visualParams.add(p.toLowerCase());

            // Also check ACTIONS for SET_PARAMETER
            (v.actions || []).forEach((a: ReportAction) => {
                if (a.type === 'SET_PARAMETER' && a.parameterName) {
                    visualParams.add(a.parameterName.toLowerCase());
                }
            });
        }
    });

    const autoParams: ParameterMeta[] = [];
    for (const name in manifest.parameterMetadata) {
        if (!visualParams.has(name.toLowerCase())) {
            autoParams.push(manifest.parameterMetadata[name]);
        }
    }

    if (autoParams.length === 0) return;

    const panel = document.createElement('div');
    panel.className = 'auto-parameter-panel collapsed';

    const toggle = document.createElement('div');
    toggle.className = 'panel-toggle';
    toggle.innerHTML = '<span>&#x2699;</span>';
    toggle.onclick = () => panel.classList.toggle('collapsed');
    panel.appendChild(toggle);

    const content = document.createElement('div');
    content.className = 'panel-content';

    const title = document.createElement('h4');
    title.textContent = 'Report Parameters';
    content.appendChild(title);

    const list = document.createElement('div');
    list.className = 'panel-list';

    autoParams.forEach((meta: ParameterMeta) => {
        const item = document.createElement('div');
        item.className = 'panel-item';

        const label = document.createElement('label');
        label.textContent = meta.name.startsWith('@') ? meta.name.substring(1) : meta.name;
        item.appendChild(label);

        const inputGroup = document.createElement('div');
        inputGroup.className = 'input-group';

        const input = document.createElement('input');
        input.setAttribute('aria-label', label.textContent);
        input.type = inputTypeForParameter(meta);
        const currentValue = getParam(manifest.parameters, meta.name) || '';
        if (input.type === 'checkbox') input.checked = isOn(currentValue);
        else input.value = currentValue as string;
        input.placeholder = meta.defaultValue || '';
        inputGroup.appendChild(input);

        const applyBtn = document.createElement('button');
        applyBtn.innerHTML = '&#x2713;';
        applyBtn.onclick = () => {
            const updates = { ...parameters }; // Batch everything
            updates[meta.name] = input.type === 'checkbox'
                ? (input.checked ? 'TRUE' : 'FALSE')
                : input.value;
            postParameters(updates, false).then(m => {
                if (m) renderManifest(m);
            });
        };
        inputGroup.appendChild(applyBtn);

        item.appendChild(inputGroup);
        list.appendChild(item);
    });

    content.appendChild(list);
    panel.appendChild(content);
    container.appendChild(panel);
}

// ── Header & Actions ──────────────────────────────────────────────────

export function renderHeader(container: HTMLElement | DocumentFragment, manifest: ChromeManifest): void {
    const header = document.createElement('header');
    header.className = 'report-header';

    const left = document.createElement('div');
    left.className = 'header-left';
    left.innerHTML = `
            <div class="header-title">${escHtml(manifest.title || 'ETL-SQL Report')}</div>
            <div class="header-subtitle">${escHtml(manifest.description || 'Interactive Data Insight')}</div>
        `;

    const badges = [];
    badges.push(`<span class="header-badge owner" title="Owner">👤 ${escHtml(manifest.owner || 'Owner unknown')}</span>`);
    if (manifest.steward) badges.push(`<span class="header-badge steward" title="Steward">🛡️ ${escHtml(manifest.steward)}</span>`);
    if (manifest.certification) badges.push(`<span class="header-badge cert" title="Certification">⭐ ${escHtml(manifest.certification)}</span>`);
    const rawFreshnessStatus = String(manifest.freshnessStatus || (manifest.lastRefreshed || manifest.builtAt ? 'fresh' : 'unknown')).toLowerCase();
    const freshnessStatus = ['fresh', 'stale', 'unknown'].includes(rawFreshnessStatus) ? rawFreshnessStatus : 'unknown';
    const freshnessText = manifest.lastRefreshed || manifest.builtAt || 'Freshness unknown';
    badges.push(`<span class="header-badge fresh ${freshnessStatus}" title="Freshness: ${escHtml(freshnessStatus)}">🕒 ${escHtml(freshnessText)}</span>`);
    if (manifest.tags) {
        const tagList = Array.isArray(manifest.tags) ? manifest.tags : String(manifest.tags).split(',');
        tagList.forEach((t: string) => {
            const tagStr = t.trim();
            if (tagStr) badges.push(`<a class="header-badge tag" title="Search tag '${escHtml(tagStr)}'" href="#" data-tag="${escHtml(tagStr)}">🏷️ ${escHtml(tagStr)}</a>`);
        });
    }

    if (badges.length > 0) {
        const badgeContainer = document.createElement('div');
        badgeContainer.className = 'header-badges';
        badgeContainer.innerHTML = badges.join('');
        badgeContainer.querySelectorAll('a.header-badge.tag').forEach(el => {
            el.addEventListener('click', (ev) => {
                ev.preventDefault();
                const tagVal = el.getAttribute('data-tag');
                if (!tagVal) return;
                if (window.parent && window.parent !== window) {
                    window.parent.postMessage({ type: 'etl-catalog-search', tag: tagVal, query: tagVal }, window.location.origin);
                    return;
                }
                try {
                    window.top!.location.href = '/?search=' + encodeURIComponent(tagVal);
                } catch {
                    window.location.href = '/?search=' + encodeURIComponent(tagVal);
                }
            });
        });
        left.appendChild(badgeContainer);
    }

    const actions = document.createElement('div');
    actions.className = 'header-actions';

    // Offline: a snapshot can be stale, and saying so is useful, but the button that asks the
    // server to refresh it has nothing to ask.
    if (freshnessStatus === 'stale' && !isOfflineHost) {
        const refreshBtn = document.createElement('button');
        refreshBtn.className = 'header-btn warning';
        refreshBtn.title = 'Request Data Refresh for Stale Report';
        refreshBtn.textContent = '🔄 Request Refresh';
        refreshBtn.addEventListener('click', async () => {
            const reportId = manifest.id || window.__REPORT_ID__;
            if (!reportId) return;
            try {
                const base = window.__API_BASE__ || '';
                const res = await fetch(`${base}/api/reports/${reportId}/request-refresh`, { method: 'POST', headers: { 'Content-Type': 'application/json' } });
                const data = await res.json().catch(() => ({}));
                feedback!.notify(data.message || 'Data refresh requested.', { title: 'Refresh requested', tone: 'success', auditAction: 'report.refresh.request' });
            } catch (e) {
                feedback!.notify('Request failed: ' + (e as Error).message, { title: 'Refresh request failed', tone: 'error' });
            }
        });
        actions.appendChild(refreshBtn);
    }

    // Views picker: author bookmarks (shared, source-controlled) plus the caller's private
    // "My saved views" when the Portal saved-view API is reachable. Both live in one menu so a
    // reader picks a view without having to know which of the two kinds it is.
    const viewsPicker = buildViewsPicker(manifest);
    if (viewsPicker) actions.appendChild(viewsPicker);

    if (vscode) {
        const openBtn = document.createElement('button');
        openBtn.className = 'header-btn primary';
        openBtn.title = 'Open interactive report in browser';
        openBtn.textContent = 'Open';
        openBtn.addEventListener('click', () => {
            vscode!.postMessage({ type: 'serve' });
        });
        actions.appendChild(openBtn);

        const pdfBtn = document.createElement('button');
        pdfBtn.className = 'header-btn';
        pdfBtn.title = 'Export to PDF';
        pdfBtn.textContent = 'PDF';
        pdfBtn.addEventListener('click', () => {
            vscode!.postMessage({ type: 'exportReport', format: 'pdf' });
        });
        actions.appendChild(pdfBtn);

        const mdBtn = document.createElement('button');
        mdBtn.className = 'header-btn';
        mdBtn.title = 'Export to Markdown';
        mdBtn.textContent = 'MD';
        mdBtn.addEventListener('click', () => {
            vscode!.postMessage({ type: 'exportReport', format: 'markdown' });
        });
        actions.appendChild(mdBtn);

        const publishBtn = document.createElement('button');
        publishBtn.className = 'header-btn';
        publishBtn.title = 'Publish to Portal';
        publishBtn.textContent = 'Publish';
        publishBtn.addEventListener('click', () => vscode!.postMessage({ type: 'publish' }));
        actions.appendChild(publishBtn);
    }

    header.appendChild(left);
    header.appendChild(actions);
    container.appendChild(header);
}

/**
 * @param {HTMLElement | DocumentFragment} container The nav is prepended or inserted before
 *   the first page, so anything that can take a child works. `renderManifest` builds into a
 *   DocumentFragment, which is not an HTMLElement.
 * @param {*} navDef
 * @param {Record<string, HTMLElement>} pageSections
 * @param {Array<*>} pages
 * @param {*} manifest The manifest being rendered. A nav click re-themes the body from it, and
 *   without it in scope that call threw into the surrounding catch on every page change — the
 *   theme silently stayed on the previous page's.
 */
export function renderNavBar(container: HTMLElement | DocumentFragment, navDef: NavDefinition, pageSections: Record<string, HTMLElement>, pages: ChromePage[], manifest: ChromeManifest): void {
    const nav = document.createElement('nav');
    nav.className = 'nav-bar';

    // Apply nav styling
    if (navDef.styles) {
        for (const [k, v] of Object.entries(navDef.styles)) {
            const key = k.toLowerCase().replace(/_/g, '-');
            (nav.style as unknown as Record<string, string>)[key] = v as string;
        }
    }

    // Insert nav before the first page section
    const firstPage = pages.length > 0 ? pageSections[pages[0].name] : null;
    if (firstPage) {
        container.insertBefore(nav, firstPage);
    } else {
        container.prepend(nav);
    }

    const defaultPageName = navDef.defaultPage || (pages.length > 0 ? pages[0].name : null);
    const requestedPage  = (getLastActivePage() || window.__INITIAL_PAGE__ || '').trim();
    const pageToShow     = (requestedPage && pageSections[requestedPage]) ? requestedPage : defaultPageName;
    const itemClass = navDef.navType === 'TAB' ? 'nav-tab' :
                      navDef.navType === 'BUTTON' ? 'nav-btn' : 'nav-link';
    const isLink = navDef.navType === 'LINK';
    const hideInvisible = isOn(navDef.options?.['HIDE_INVISIBLE'] || navDef.options?.['hide_invisible']);

    function applyActiveStyles(element: HTMLElement, isActive: boolean): void {
        if (!navDef.activeStyles) return;
        for (const [k, v] of Object.entries(navDef.activeStyles)) {
            const key = k.toLowerCase().replace(/_/g, '-');
            if (isActive) (element.style as unknown as Record<string, string>)[key] = v as string;
            else element.style.removeProperty(key);
        }
    }

    function createNavItem(item: NavItem, idx: number, parent: HTMLElement): void {
        const pageName = (item.pageName || item) as string;
        const targetPage = pages.find(p => p.name === pageName);
        if (hideInvisible && targetPage && !isPageVisible(targetPage)) {
            return;
        }

        if (isLink && idx > 0) {
            const sep = document.createElement('span');
            sep.className = 'nav-sep';
            sep.textContent = ' | ';
            parent.appendChild(sep);
        }

        if (item.isExternalLink || item.externalUrl) {
            const el = document.createElement('a');
            el.className = itemClass + ' nav-link-external';
            el.href = safeUrl(item.externalUrl);
            el.target = item.target || '_blank';
            el.rel = 'noopener noreferrer';
            if (item.icon) {
                const iconSpan = document.createElement('span');
                iconSpan.className = 'nav-icon';
                iconSpan.textContent = item.icon;
                el.appendChild(iconSpan);
            }
            const textSpan = document.createElement('span');
            textSpan.className = 'nav-text';
            textSpan.textContent = item.label || 'Link';
            el.appendChild(textSpan);
            if (item.badge) {
                const badgeSpan = document.createElement('span');
                badgeSpan.className = 'nav-badge';
                badgeSpan.textContent = item.badge;
                el.appendChild(badgeSpan);
            }
            parent.appendChild(el);
            return;
        }

        const el = document.createElement('span');
        el.className = itemClass;
        if (item.icon) {
            const iconSpan = document.createElement('span');
            iconSpan.className = 'nav-icon';
            iconSpan.textContent = item.icon;
            el.appendChild(iconSpan);
        }
        const textSpan = document.createElement('span');
        textSpan.className = 'nav-text';
        textSpan.textContent = item.label || pageName;
        el.appendChild(textSpan);
        if (item.badge) {
            const badgeSpan = document.createElement('span');
            badgeSpan.className = 'nav-badge';
            badgeSpan.textContent = item.badge;
            el.appendChild(badgeSpan);
        }

        const isActive = (pageName === pageToShow);
        if (isActive) {
            el.classList.add('active');
            applyActiveStyles(el, true);
        }

        el.dataset.page = pageName;
        el.addEventListener('click', () => {
            try {
                pages.forEach(p => {
                    const n = p.name;
                    const s = pageSections[n];
                    if (s) {
                        if (n === pageName) {
                            s.style.display = 'block';
                            s.classList.add('active');
                            const pgDef = pages.find(item => item.name === n);
                            const trans = (pgDef?.options?.TRANSITION || navDef.options?.TRANSITION || '').toUpperCase();
                            if (trans === 'FADE') {
                                s.style.opacity = '0';
                                safeRequestAnimationFrame(() => { s.style.opacity = '1'; });
                            } else if (trans === 'SLIDE') {
                                s.style.transform = 'translateX(20px)';
                                s.style.opacity = '0';
                                safeRequestAnimationFrame(() => { s.style.transform = 'translateX(0)'; s.style.opacity = '1'; });
                            }
                        } else {
                            s.style.display = 'none';
                            s.classList.remove('active');
                        }
                    }
                });
            } catch (e) {
                console.error('Error switching page visibility:', e);
            }

            try {
                /** @type {NodeListOf<HTMLElement>} */ (document.querySelectorAll<HTMLElement>('[data-page]')).forEach(it => {
                    const match = it.dataset.page === pageName;
                    if (match) {
                        it.classList.add('active');
                        applyActiveStyles(it, true);
                    } else {
                        it.classList.remove('active');
                        applyActiveStyles(it, false);
                    }
                });
            } catch (e) {
                console.error('Error updating active page classes:', e);
            }

            setLastActivePage(pageName);

            try {
                const target = pageSections[pageName];
                if (target) safeRequestAnimationFrame(() => resizeChartsIn(target));
            } catch (e) {
                console.error('Error resizing charts:', e);
            }

            try {
                updateBodyTheme(manifest, pageName);
            } catch (e) {
                console.error('Error updating body theme:', e);
            }

            try {
                const pDef = pages.find(p => p.name === pageName);
                executePageOnLoad(pDef);
            } catch (e) {
                console.error('Error executing page actions:', e);
            }

            try {
                if (window.parent && window.parent !== window) {
                    window.parent.postMessage({ type: 'etl-page-changed', page: pageName, userTriggered: true }, '*');
                }
            } catch (e) {
                console.error('Error posting message to parent window:', e);
            }
        });

        parent.appendChild(el);
    }

    if (navDef.groups && navDef.groups.length > 0) {
        navDef.groups.forEach(group => {
            const grpDiv = document.createElement('div');
            grpDiv.className = 'nav-group';
            if (group.title) {
                const t = document.createElement('span');
                t.className = 'nav-group-title';
                t.textContent = group.title;
                grpDiv.appendChild(t);
            }
            const grpItems = document.createElement('div');
            grpItems.className = 'nav-group-items';
            (group.items || []).forEach((item, idx) => createNavItem(item, idx, grpItems));
            grpDiv.appendChild(grpItems);
            nav.appendChild(grpDiv);
        });
    } else if (navDef.items && navDef.items.length > 0) {
        navDef.items.forEach((item, idx) => createNavItem(item, idx, nav));
    } else {
        navDef.pages!.forEach((pageName, idx) => createNavItem({ pageName: pageName }, idx, nav));
    }

    // Execute initial page onLoad
    const initPg = pages.find(p => p.name === pageToShow);
    executePageOnLoad(initPg);
}

export function showModalDialog(modalName: string | null | undefined, manifest: ChromeManifest | null | undefined): void {
    if (!modalName) return;
    hideModalDialog(modalName);

    const containerDef = (manifest?.containers || []).find(c => c.name.toLowerCase() === modalName.toLowerCase());
    if (!containerDef) {
        console.warn('Modal container not found:', modalName);
        return;
    }

    const overlay = document.createElement('div');
    overlay.className = 'report-modal-overlay';
    overlay.id = 'modal-overlay-' + modalName.toLowerCase();

    const dialog = document.createElement('div');
    dialog.className = 'report-modal-dialog';
    dialog.setAttribute('role', 'dialog');
    dialog.setAttribute('aria-modal', 'true');

    const header = document.createElement('div');
    header.className = 'report-modal-header';

    const title = document.createElement('span');
    title.textContent = containerDef.title || containerDef.name;
    header.appendChild(title);

    const closeBtn = document.createElement('button');
    closeBtn.className = 'report-modal-close';
    closeBtn.innerHTML = '&times;';
    closeBtn.setAttribute('aria-label', 'Close dialog');
    closeBtn.onclick = () => hideModalDialog(modalName);
    header.appendChild(closeBtn);

    dialog.appendChild(header);

    const body = document.createElement('div');
    body.className = 'report-modal-body';
    const styles = containerDef.styles || {};
    const containerTheme = getStyle(styles, 'THEME') as string | null;
    renderLayout(body, containerDef, manifest!, containerTheme);
    dialog.appendChild(body);

    overlay.appendChild(dialog);
    overlay.addEventListener('click', e => {
        if (e.target === overlay) hideModalDialog(modalName);
    });

    const onKeyDown = (e: KeyboardEvent): void => {
        if (e.key === 'Escape') {
            hideModalDialog(modalName);
            document.removeEventListener('keydown', onKeyDown);
        }
    };
    document.addEventListener('keydown', onKeyDown);

    document.body.appendChild(overlay);
    setTimeout(() => resizeChartsIn(dialog), 50);
}

export function hideModalDialog(modalName: string | null | undefined): void {
    if (!modalName) return;
    const overlay = document.getElementById('modal-overlay-' + modalName.toLowerCase());
    if (overlay) overlay.remove();
}


// ── Footer ──────────────────────────────────────────────────────────────

export function renderFooter(container: HTMLElement, manifest: ChromeManifest): void {
    const footer = document.createElement('footer');
    const built  = manifest.builtAt ? new Date(manifest.builtAt).toLocaleString() : '';
    footer.innerHTML = '<small>Built: ' + escHtml(built) + '</small>';
    container.appendChild(footer);
}

export function renderPipelineConsole(root: HTMLElement, manifest: ChromeManifest): void {
    if (window.__IS_PREVIEW__ || vscode) return;
    if (!manifest.messages?.length && !(manifest.executionTree as { length?: number } | null | undefined)?.length && !manifest.error) return;

    const consoleWrapper = document.createElement('div');
    consoleWrapper.className = 'pipeline-console collapsed';

    const header = document.createElement('div');
    header.className = 'pipeline-header';

    let statusColor = 'gray';
    let statusText = 'Completed';
    if (manifest.error) {
        statusColor = 'red';
        statusText = 'Failed';
    }

    header.innerHTML = `
            <span>Pipeline Console</span>
            <span style="color: ${statusColor}; font-weight: normal;">
                ${statusText}
                <span class="toggle-icon" style="margin-left: 8px;">&#x25B2;</span>
            </span>
        `;

    const body = document.createElement('div');
    body.className = 'pipeline-body';

    const leftPane = document.createElement('div');
    leftPane.className = 'pipeline-pane left-pane';
    leftPane.innerHTML = '<div class="pane-title">Execution Tree</div>';

    const rightPane = document.createElement('div');
    rightPane.className = 'pipeline-pane';
    rightPane.innerHTML = '<div class="pane-title">Messages</div>';

    body.appendChild(leftPane);
    body.appendChild(rightPane);

    consoleWrapper.appendChild(header);
    consoleWrapper.appendChild(body);

    let isCollapsed = true;
    header.addEventListener('click', () => {
        isCollapsed = !isCollapsed;
        consoleWrapper.classList.toggle('collapsed', isCollapsed);
        const icon = /** @type {HTMLElement | null} */ (header.querySelector<HTMLElement>('.toggle-icon'));
        icon!.innerHTML = isCollapsed ? '&#x25B2;' : '&#x25BC;';
    });

    // Render Execution Tree
    if (manifest.executionTree) {
        const treeRoot = document.createElement('div');

        function renderNode(node: ExecutionNode, container: HTMLElement): void {
            const el = document.createElement('div');
            el.className = 'tree-node';

            const content = document.createElement('div');
            content.className = 'tree-node-content';

            const hasChildren = node.children && node.children.length > 0;
            const iconStr = hasChildren ? '&#x25BC;' : '&nbsp;';

            let timeStr = '';
            if (node.durationMs != null) timeStr = `[${escHtml(node.durationMs)}ms]`;

            let rowsStr = '';
            if (node.rowsProcessed != null) rowsStr = `(${escHtml(node.rowsProcessed)} rows)`;

            const status = node.status || 'Completed';
            const statusClass = cssClassToken(status, 'completed');
            content.innerHTML = `
                    <span class="tree-icon" style="color:#888">${iconStr}</span>
                    <span class="node-name">${escHtml(node.name || 'Unnamed')}</span>
                    <span class="node-meta">
                        <span class="status-${statusClass}">${escHtml(status)}</span>
                        ${timeStr} ${rowsStr}
                    </span>
                `;

            el.appendChild(content);

            if (hasChildren) {
                const childrenContainer = document.createElement('div');
                childrenContainer.className = 'tree-children';
                node.children!.forEach((child: ExecutionNode) => renderNode(child, childrenContainer));
                el.appendChild(childrenContainer);

                /** @type {HTMLElement | null} */ (content.querySelector<HTMLElement>('.tree-icon'))!.addEventListener('click', (e) => {
                    e.stopPropagation();
                    const isHidden = childrenContainer.style.display === 'none';
                    childrenContainer.style.display = isHidden ? 'block' : 'none';
                    /** @type {Element} */ ((e.target) as Element).innerHTML = isHidden ? '&#x25BC;' : '&#x25B6;';
                });
            }
            container.appendChild(el);
        }

        if (Array.isArray(manifest.executionTree)) {
            manifest.executionTree.forEach((rootNode: ExecutionNode) => renderNode(rootNode, treeRoot));
        } else {
            renderNode(manifest.executionTree, treeRoot);
        }

        leftPane.appendChild(treeRoot);
    } else {
        leftPane.innerHTML += '<div class="no-data">No execution tree available.</div>';
    }

    // Render Messages
    if (manifest.messages && manifest.messages.length > 0) {
        manifest.messages.forEach(msg => {
            const entry = document.createElement('div');
            entry.className = 'log-entry';

            const time = new Date(msg.timestamp).toLocaleTimeString();
            const colorClass = `log-${cssClassToken(msg.color, 'white')}`;

            entry.innerHTML = `
                    <span class="log-time">[${time}]</span>
                    <span class="${colorClass}">${escHtml(msg.message)}</span>
                `;
            rightPane.appendChild(entry);
        });
    } else {
        rightPane.innerHTML += '<div class="no-data">No messages recorded.</div>';
    }

    if (manifest.error) {
        const errEntry = document.createElement('div');
        errEntry.className = 'log-entry log-red';
        errEntry.innerHTML = `<br/><b>Fatal Error:</b><br/><pre>${escHtml(manifest.error)}</pre>`;
        rightPane.appendChild(errEntry);

        // Auto-expand if there's an error
        isCollapsed = false;
        consoleWrapper.classList.remove('collapsed');
        /** @type {HTMLElement | null} */ (header.querySelector<HTMLElement>('.toggle-icon'))!.innerHTML = '&#x25BC;';
    }

    root.appendChild(consoleWrapper);
}

export function updateStagedUI(): void {
    const hasPending = Object.keys(pendingParameters).length > 0;

    // 1. Update only RUN buttons (tagged with data-is-run-btn during renderButton)
    /** @type {NodeListOf<HTMLElement>} */ (document.querySelectorAll<HTMLElement>('[data-is-run-btn]')).forEach(btn => {
        if (hasPending) {
            btn.classList.add('pending-changes');
        } else {
            btn.classList.remove('pending-changes');
        }
    });

    // 2. Add/Update a "Pending" badge in the header if it exists
    const header = /** @type {HTMLElement | null} */ (document.querySelector<HTMLElement>('.report-header'));
    if (header) {
        let badge = /** @type {HTMLElement | null} */ (header.querySelector<HTMLElement>('.pending-badge'));
        if (hasPending) {
            if (!badge) {
                badge = document.createElement('div');
                badge.className = 'pending-badge';
                badge.innerHTML = '&#x26A0; Changes Pending';
                /** @type {HTMLElement} */ (badge).style.background = '#fff3cd';
                /** @type {HTMLElement} */ (badge).style.color = '#856404';
                /** @type {HTMLElement} */ (badge).style.padding = '4px 8px';
                /** @type {HTMLElement} */ (badge).style.borderRadius = '4px';
                /** @type {HTMLElement} */ (badge).style.fontSize = '0.8em';
                /** @type {HTMLElement} */ (badge).style.fontWeight = 'bold';
                header.appendChild(badge);
            }
        } else if (badge) {
            badge.remove();
        }
    }
}
