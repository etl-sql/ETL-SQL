// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/rt-layout.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Pages, containers, tabs, accordions, and physical layouts.
 */
import { actionsFor, evaluateExpressionAgainstParameters } from './rt-actions.js';
import { _uiStates, getBaselineManifest, getRefreshTimers, parameters, vscode } from './rt-state.js';
import { postParameters, postRefreshVisuals } from './rt-transport.js';
import { renderManifest } from './report-runtime.js';
import { escHtml, getOption, getParam, getStyle, isOn, renderInlineMarkdown, safeUrl, toCssLength, toPixels } from './rt-util.js';
import { applyDesignTokens } from './rt-theme.js';
import { renderVisual, resizeChartsIn } from './rt-visual.js';
import { renderButton } from './rt-controls-input.js';



export function isPageVisible(page, params) {
    if (!page) return false;
    if (page.isHidden) return false;
    if (page.visibleExpression) {
        return evaluateExpressionAgainstParameters(page.visibleExpression, params || parameters);
    }
    return true;
}

export function executePageOnLoad(page) {
    if (!page || !page.actions) return;
    page.actions.forEach(action => {
        const trigger = (action.trigger || 'ON_LOAD').toUpperCase();
        if (trigger === 'ON_LOAD') {
            if (action.type === 'SET_PARAMETER' && action.parameterName) {
                if (action.value !== undefined && action.value !== null) {
                    // `setParameter` does not exist. An ON_LOAD SET_PARAMETER action threw
                    // here instead of applying, so a page that sets its own parameter on load
                    // rendered with the old value. This is the same call the drill-down and
                    // SET_PARAMETER action branches make.
                    postParameters({ [action.parameterName]: String(action.value ?? '') })
                        .then(m => { if (m) renderManifest(m); });
                }
            }
        }
    });
}

// PAGE OPTIONS reached the manifest but nothing ever read them, so every option below was
// parsed, serialized, asserted in a test, and then silently dropped before it reached a pixel.
function applyPageOptions(pageDiv, contentDiv, page) {
    const opts = page.options;
    if (!opts) return;

    const backgroundImage = getOption(opts, 'BACKGROUND_IMAGE');
    if (backgroundImage) {
        const raw = String(backgroundImage).trim();
        // Authors write either a bare path or the CSS url(...) form. Quotes and parentheses are
        // stripped from the bare form so a value cannot close url() and start another token.
        pageDiv.style.backgroundImage = /^url\(/i.test(raw)
            ? raw
            : `url("${raw.replace(/["'();\\\s]/g, '')}")`;
        pageDiv.style.backgroundRepeat = 'no-repeat';
        pageDiv.style.backgroundPosition = 'center';
        pageDiv.style.backgroundSize = (getOption(opts, 'BACKGROUND_SIZE') || 'cover').toLowerCase();
    }

    const overflow = getOption(opts, 'OVERFLOW');
    if (overflow) pageDiv.style.overflow = String(overflow).toLowerCase();

    const maxWidth = toCssLength(getOption(opts, 'MAX_WIDTH'));
    if (maxWidth) contentDiv.style.maxWidth = maxWidth;

    if ((getOption(opts, 'ALIGN_CONTENT') || '').toUpperCase() === 'CENTER') {
        contentDiv.style.marginLeft = 'auto';
        contentDiv.style.marginRight = 'auto';
    }
}

export function renderPage(manifest, page, pageSections, pageTheme) {
    console.debug(`[Layout] Rendering Page: ${page.name}`);
    const div = document.createElement('div');
    div.className = 'page';
    if (page.name) div.id = 'page-' + page.name.toLowerCase();
    div.dataset.pageName = page.name || '';
    div.dataset.pageMode = (page.mode || 'DASHBOARD').toUpperCase();
    applyDesignTokens(div, page, true, manifest);

    const content = document.createElement('div');
    content.className = 'page-grid';
    div.appendChild(content);
    applyPageOptions(div, content, page);

    pageSections[page.name] = div;

    if (page.mode === 'PAGINATED' && page.physicalPages && page.physicalPages.length > 0) {
        renderPhysicalPages(content, page, manifest, pageTheme);
    } else {
        renderResponsiveLayout(content, page, manifest, pageTheme);
    }

    return div;
}

// MOBILE_LAYOUT is an alternate structure, not a style: below the breakpoint the page is laid
// out from the mobile structure and slot map instead of the desktop pair. Crossing the
// breakpoint re-renders, because the two layouts place different visuals in different slots.
function renderResponsiveLayout(content, page, manifest, pageTheme) {
    const mobile = page.mobileLayout;
    const breakpoint = mobile ? toPixels(mobile.breakpoint) : 0;
    if (!mobile || !mobile.structure || breakpoint <= 0) {
        renderLayout(content, page, manifest, pageTheme);
        return;
    }

    const query = window.matchMedia(`(max-width: ${breakpoint}px)`);
    const draw = () => {
        const layoutDef = query.matches
            ? { structure: mobile.structure, slotMap: mobile.slotMap || page.slotMap }
            : page;
        content.dataset.activeLayout = query.matches ? 'MOBILE' : 'DEFAULT';
        content.replaceChildren();
        content.removeAttribute('style');
        content.className = 'page-grid';
        applyPageOptions(content.parentElement, content, page);
        renderLayout(content, layoutDef, manifest, pageTheme);
    };

    draw();
    const onChange = () => draw();
    if (typeof query.addEventListener === 'function') query.addEventListener('change', onChange);
    else if (typeof query.addListener === 'function') query.addListener(onChange);
}

function renderPhysicalPages(container, pageDef, manifest, pageTheme) {
    container.style.display = 'flex';
    container.style.flexDirection = 'column';
    container.style.alignItems = 'center';
    container.style.gap = '20px';
    container.style.padding = '20px';
    container.style.backgroundColor = '#f0f0f0';

    pageDef.physicalPages.forEach(pPage => {
        const sheet = document.createElement('div');
        sheet.className = 'physical-page-sheet';
        applyDesignTokens(sheet, pageDef, true, manifest);
        
        const layout = pPage.layout || {};
        const width = layout.customWidth || (layout.orientation === 'Landscape' ? 11.0 : 8.5);
        const height = layout.customHeight || (layout.orientation === 'Landscape' ? 8.5 : 11.0);
        const unit = layout.units || 'in';

        sheet.style.width = width + unit;
        sheet.style.height = height + unit;
        sheet.style.backgroundColor = 'white';
        sheet.style.boxShadow = '0 4px 8px rgba(0,0,0,0.1)';
        sheet.style.position = 'relative';
        sheet.style.overflow = 'hidden';

        const marginT = (layout.marginTop ?? 1.0) + unit;
        const marginR = (layout.marginRight ?? 1.0) + unit;
        const marginB = (layout.marginBottom ?? 1.0) + unit;
        const marginL = (layout.marginLeft ?? 1.0) + unit;

        const printArea = document.createElement('div');
        printArea.style.position = 'absolute';
        printArea.style.top = marginT;
        printArea.style.right = marginR;
        printArea.style.bottom = marginB;
        printArea.style.left = marginL;

        (pPage.visuals || []).forEach(pv => {
            const wrapper = document.createElement('div');
            wrapper.style.position = 'absolute';
            wrapper.style.top = pv.topOffset + unit;
            wrapper.style.left = '0';
            wrapper.style.right = '0';
            wrapper.style.height = pv.height + unit;
            
            if (pv.visual) {
                let visToRender = pv.visual;
                if (pv.startRowIndex !== undefined && pv.endRowIndex !== undefined && visToRender.visualType === 'TABLE') {
                    visToRender = JSON.parse(JSON.stringify(pv.visual));
                    if (visToRender.rows) {
                        visToRender.rows = visToRender.rows.slice(pv.startRowIndex, pv.endRowIndex + 1);
                    }
                }
                renderVisual(wrapper, visToRender, pageTheme, manifest);
            }
            printArea.appendChild(wrapper);
        });

        sheet.appendChild(printArea);
        container.appendChild(sheet);
    });
}

export function renderContainer(container, containerDef, manifest, pageTheme) {
    const containerTypeName = (containerDef.containerType || '').toUpperCase();
    if (containerTypeName === 'MODAL') {
        return; // Modal dialogs are rendered on-demand via SHOW_MODAL
    }
    if (containerTypeName === 'TABS') {
        renderTabsContainer(container, containerDef, manifest, pageTheme);
        return;
    }
    if (containerTypeName === 'ACCORDION') {
        renderAccordionContainer(container, containerDef, manifest, pageTheme);
        return;
    }

    const div = document.createElement('div');
    const isScroll = containerTypeName === 'SCROLL';
    const isLayer  = containerTypeName === 'LAYER';
    div.className = isScroll ? 'container-scroll' : isLayer ? 'container-layer' : 'container-box';

    // LAYER: stack children as absolutely-positioned overlapping panels
    if (isLayer) {
        div.setAttribute('data-name', containerDef.name);
        applyDesignTokens(div, containerDef, false, manifest);
        const height = (containerDef.styles || {})['HEIGHT'] || (containerDef.styles || {})['height'];
        if (height) div.style.height = height;
        const slotMap = containerDef.slotMap || {};
        const uniqueItems = [...new Set(Object.values(slotMap))];
        uniqueItems.forEach((item, i) => {
            const wrapper = document.createElement('div');
            wrapper.className = 'layer-slot';
            wrapper.style.zIndex = String(i + 1);
            const visual = (manifest.visuals || []).find(v => v.name.toLowerCase() === item.toLowerCase());
            if (visual) {
                renderVisual(wrapper, visual, pageTheme, manifest);
            } else {
                const nested = (manifest.containers || []).find(c => c.name.toLowerCase() === item.toLowerCase());
                if (nested) renderContainer(wrapper, nested, manifest, pageTheme);
            }
            div.appendChild(wrapper);
        });
        container.appendChild(div);
        setTimeout(() => resizeChartsIn(div), 50);
        return;
    }
    div.setAttribute('data-name', containerDef.name);
    applyDesignTokens(div, containerDef, false, manifest);

    const tag = getOption(containerDef.options, 'TAG') || getStyle(containerDef.styles, 'TAG');
    if (tag) div.setAttribute('data-tag', tag);
    const styles = containerDef.styles || {};
    const opts = containerDef.options || {};
    const containerTheme = getStyle(styles, 'THEME') || pageTheme;

    const collapsibleOpt = getOption(opts, 'COLLAPSIBLE');
    const isCollapsible = containerDef.isCollapsible || isOn(collapsibleOpt);
    const defaultState = (getOption(opts, 'DEFAULT') || 'OPEN').toUpperCase();

    if (containerDef.refresh && containerDef.refresh > 0) {
        const rId = setInterval(() => {
            const visualNames = [];
            const slotMap = containerDef.slotMap || {};
            Object.values(slotMap).forEach(target => {
                const v = (manifest?.visuals || []).find(x => x.name.toLowerCase() === target.toLowerCase());
                if (v) visualNames.push(v.name);
            });
            if (visualNames.length > 0) {
                if (vscode) vscode.postMessage({ type: 'refreshVisuals', visuals: visualNames });
                else postRefreshVisuals(visualNames).then(m => { if (m) renderManifest(m); });
            }
        }, containerDef.refresh * 1000);
        getRefreshTimers().push(rId);
    }

    if (isScroll) {
        const height = getStyle(styles, 'HEIGHT') || '400px';
        div.style.maxHeight = height;
    }

    if (isCollapsible) {
        div.classList.add('collapsible-inline');
        const header = document.createElement('div');
        header.className = 'container-header';

        const title = document.createElement('span');
        title.className = 'container-title';
        const cTitleText = containerDef.title || containerDef.name;
        if (containerDef.titleIsMarkdown && containerDef.title) {
            title.innerHTML = renderInlineMarkdown(cTitleText);
        } else {
            title.textContent = cTitleText;
        }
        const cStyles = containerDef.styles || {};
        const ctColor = getStyle(cStyles, 'TITLE_COLOR');
        const ctSize = getStyle(cStyles, 'TITLE_SIZE');
        const ctWeight = getStyle(cStyles, 'TITLE_WEIGHT');
        const ctFont = getStyle(cStyles, 'TITLE_FONT');
        const ctAlign = getStyle(cStyles, 'TITLE_ALIGN');
        if (ctColor) title.style.color = ctColor;
        if (ctSize) title.style.fontSize = ctSize.includes('px') || ctSize.includes('rem') || ctSize.includes('em') || ctSize.includes('%') ? ctSize : (ctSize + 'px');
        if (ctWeight) title.style.fontWeight = ctWeight;
        if (ctFont) title.style.fontFamily = ctFont;
        if (ctAlign) title.style.textAlign = ctAlign.toLowerCase();
        header.appendChild(title);

        const showActiveCount = isOn(getOption(opts, 'SHOW_ACTIVE_COUNT'));
        if (showActiveCount) {
            const count = calculateContainerActiveCount(containerDef, manifest);
            if (count > 0) {
                const countBadge = document.createElement('span');
                countBadge.className = 'container-active-count-badge';
                countBadge.textContent = `${count} active`;
                header.appendChild(countBadge);
            }
        }

        const chevron = document.createElement('span');
        chevron.className = 'container-chevron';
        chevron.innerHTML = '&#x25B2;'; // UP
        header.appendChild(chevron);

        const name = containerDef.name;
        const persisted = _uiStates[name];
        const startCollapsed = (persisted && persisted.collapsed) || (!persisted && defaultState === 'CLOSED');
        if (startCollapsed) {
            div.classList.add('collapsed');
            chevron.innerHTML = '&#x25BC;'; // DOWN
        }

        header.onclick = () => {
            const isCollapsed = div.classList.toggle('collapsed');
            chevron.innerHTML = isCollapsed ? '&#x25BC;' : '&#x25B2;'; // DOWN : UP
            setTimeout(() => {
                resizeChartsIn(div);
                const grid = getPageContainer(div)?.querySelector('.page-grid');
                if (grid) resizeChartsIn(grid);
            }, 350);
        };

        div.appendChild(header);

        const content = document.createElement('div');
        content.className = 'container-content';
        renderLayout(content, containerDef, manifest, containerTheme);
        div.appendChild(content);
    } else {
        renderLayout(div, containerDef, manifest, containerTheme);
    }

    container.appendChild(div);
}

function getPageContainer(el) {
    while (el && el !== document.body && !el.classList.contains('page')) el = el.parentElement;
    return el;
}

function renderCollapsibleContainer(gridContainer, containerDef, manifest, pageTheme, slotWrapper) {
    const page = getPageContainer(gridContainer);
    if (!page) {
        // Fallback: if no page found, render normally
        renderContainer(slotWrapper, containerDef, manifest, pageTheme);
        return;
    }

    // 1. Create Rail if not exists
    let rail = page.querySelector('.drawer-rail-left');
    if (!rail) {
        rail = document.createElement('div');
        rail.className = 'drawer-rail-left';
        page.appendChild(rail);
    }

    // 2. Create Trigger
    const trigger = document.createElement('div');
    trigger.className = 'drawer-trigger';
    trigger.title = containerDef.title || containerDef.name;

    let iconHtml = '&#x2699;'; // Default GEAR
    if (containerDef.icon) {
        const icon = containerDef.icon.toUpperCase();
        if (icon === 'GEAR') iconHtml = '&#x2699;';
        else if (icon === 'FILTER') iconHtml = '&#x1F50D;';
        else if (icon === 'INFO') iconHtml = '&#x2139;';
        else if (containerDef.icon.includes('.') || containerDef.icon.includes('/')) {
            iconHtml = `<img src="${escHtml(safeUrl(containerDef.icon))}" style="width:24px;height:24px;">`;
        } else {
            iconHtml = escHtml(containerDef.icon);
        }
    }
    trigger.innerHTML = iconHtml;
    rail.appendChild(trigger);

    // 3. Create Drawer
    const drawer = document.createElement('div');
    drawer.className = 'collapsible-drawer';
    drawer.setAttribute('data-name', containerDef.name);
    applyDesignTokens(drawer, containerDef, false, manifest);
    const tag = getOption(containerDef.options, 'TAG') || getStyle(containerDef.styles, 'TAG');
    if (tag) drawer.setAttribute('data-tag', tag);

    const styles = containerDef.styles || {};
    const containerTheme = getStyle(styles, 'THEME') || pageTheme;

    const header = document.createElement('div');
    header.className = 'drawer-header';

    const title = document.createElement('div');
    title.className = 'drawer-title';
    const dTitleText = containerDef.title || containerDef.name;
    if (containerDef.titleIsMarkdown && containerDef.title) {
        title.innerHTML = renderInlineMarkdown(dTitleText);
    } else {
        title.textContent = dTitleText;
    }
    const dtColor = getStyle(styles, 'TITLE_COLOR');
    const dtSize = getStyle(styles, 'TITLE_SIZE');
    const dtWeight = getStyle(styles, 'TITLE_WEIGHT');
    const dtFont = getStyle(styles, 'TITLE_FONT');
    const dtAlign = getStyle(styles, 'TITLE_ALIGN');
    if (dtColor) title.style.color = dtColor;
    if (dtSize) title.style.fontSize = dtSize.includes('px') || dtSize.includes('rem') || dtSize.includes('em') || dtSize.includes('%') ? dtSize : (dtSize + 'px');
    if (dtWeight) title.style.fontWeight = dtWeight;
    if (dtFont) title.style.fontFamily = dtFont;
    if (dtAlign) title.style.textAlign = dtAlign.toLowerCase();
    header.appendChild(title);

    const actions = document.createElement('div');
    actions.className = 'drawer-actions';

    if (containerDef.isPinnable !== false) {
        const pinBtn = document.createElement('span');
        pinBtn.className = 'drawer-action-btn';
        pinBtn.innerHTML = '&#x1F4CC;'; // Pin
        pinBtn.title = 'Pin Panel';
        pinBtn.onclick = (e) => {
            e.stopPropagation();
            const isPinned = drawer.classList.toggle('pinned');
            pinBtn.classList.toggle('active');
            gridContainer.classList.toggle('has-pinned-left');
            if (isPinned) drawer.classList.add('open');
            setTimeout(() => resizeChartsIn(gridContainer), 350);
        };
        actions.appendChild(pinBtn);
    }

    const closeBtn = document.createElement('span');
    closeBtn.className = 'drawer-action-btn';
    closeBtn.innerHTML = '&times;';
    closeBtn.onclick = () => {
        drawer.classList.remove('open');
        if (drawer.classList.contains('pinned')) {
            drawer.classList.remove('pinned');
            const pinBtn = actions.querySelector('.drawer-action-btn');
            if (pinBtn) pinBtn.classList.remove('active');
            gridContainer.classList.remove('has-pinned-left');
            setTimeout(() => resizeChartsIn(gridContainer), 350);
        }
    };
    actions.appendChild(closeBtn);

    header.appendChild(actions);
    drawer.appendChild(header);

    const content = document.createElement('div');
    content.className = 'drawer-content';
    renderLayout(content, containerDef, manifest, containerTheme);
    drawer.appendChild(content);

    page.appendChild(drawer);

    const drawerDefault = (getOption(containerDef.options, 'DEFAULT') || 'CLOSED').toUpperCase();
    if (drawerDefault === 'OPEN') {
        drawer.classList.add('open');
    }

    trigger.onclick = () => {
        drawer.classList.toggle('open');
        if (!drawer.classList.contains('open') && drawer.classList.contains('pinned')) {
             // If closing while pinned, unpin
             closeBtn.click();
        }
    };

    if (slotWrapper) slotWrapper.classList.add('grid-slot-collapsed');
}


export function renderLayout(container, layoutDef, manifest, pageTheme) {
    if (layoutDef.structure) {
        container.style.display = 'grid';
        // CSS grid-template-areas needs each row quoted: "A A" "B C"
        const rows = layoutDef.structure.split('/')
            .map(r => r.trim().split(/\s+/).filter(s => s))
            .filter(r => r.length > 0);

        const maxCols = Math.max(...rows.map(r => r.length));
        const normalizedRows = rows.map(r => {
            while (r.length < maxCols) r.push('.');
            return r.join(' ');
        });

        container.style.gridTemplateAreas = normalizedRows.map(r => `"${r}"`).join(' ');

        if (rows.length > 0) {
            container.style.gridTemplateRows = `repeat(${rows.length}, auto)`;
            container.style.gridTemplateColumns = `repeat(${maxCols}, 1fr)`;
        }

        const slotMap = layoutDef.slotMap || {};
        Object.keys(slotMap).forEach(slotLetter => {
            const item = slotMap[slotLetter];
            if (!item) return;

            const wrapper = document.createElement('div');
            wrapper.style.gridArea = slotLetter;

            // Item could be a visual or another container
            const visual = (manifest.visuals || []).find(v => v.name.toLowerCase() === item.toLowerCase());
            if (visual) {
                renderVisual(wrapper, visual, pageTheme, manifest);
            } else {
                const nested = (manifest.containers || []).find(c => c.name.toLowerCase() === item.toLowerCase());
                if (nested) {
                    const mode = (getStyle(nested.styles, 'COLLAPSE_MODE') || 'DRAWER').toUpperCase();
                    if (nested.isCollapsible && mode === 'DRAWER') {
                        renderCollapsibleContainer(container, nested, manifest, pageTheme, wrapper);
                    } else {
                        renderContainer(wrapper, nested, manifest, pageTheme);
                    }
                } else {

                    const btn = (manifest.buttons || []).find(b => b.name.toLowerCase() === item.toLowerCase());
                    if (btn) renderButton(wrapper, btn);
                }
            }
            container.appendChild(wrapper);
        });
    } else {
        const slotMap = layoutDef.slotMap || {};
        const uniqueItems = [...new Set(Object.values(slotMap))];
        uniqueItems.forEach(item => {
            const visual = (manifest.visuals || []).find(v => v.name.toLowerCase() === item.toLowerCase());
            if (visual) {
                renderVisual(container, visual, pageTheme, manifest);
            } else {
                const nested = (manifest.containers || []).find(c => c.name.toLowerCase() === item.toLowerCase());
                if (nested) {
                    const mode = (getStyle(nested.styles, 'COLLAPSE_MODE') || 'DRAWER').toUpperCase();
                    if (nested.isCollapsible && mode === 'DRAWER') {
                        renderCollapsibleContainer(container, nested, manifest, pageTheme, null);
                    } else {
                        renderContainer(container, nested, manifest, pageTheme);
                    }
                } else {

                    const btn = (manifest.buttons || []).find(b => b.name.toLowerCase() === item.toLowerCase());
                    if (btn) renderButton(container, btn);
                }
            }
        });
    }
}

function calculateContainerActiveCount(containerDef, manifest) {
    if (!containerDef || !getBaselineManifest() || !getBaselineManifest().parameters) return 0;
    const slotMap = containerDef.slotMap || {};
    const items = Object.values(slotMap);
    let activeCount = 0;
    const countedParams = new Set();

    items.forEach(itemName => {
        const v = (manifest?.visuals || []).find(vis => vis.name.toLowerCase() === itemName.toLowerCase());
        if (v) {
            const changeActions = actionsFor(v, 'ON_CHANGE').concat(actionsFor(v, 'ON_SUBMIT')).filter(a => a.type === 'SET_PARAMETER');
            changeActions.forEach(a => {
                const p = a.parameterName;
                if (p && !countedParams.has(p.toLowerCase())) {
                    countedParams.add(p.toLowerCase());
                    const curVal = String(getParam(parameters, p) ?? '');
                    const baseVal = String(getParam(getBaselineManifest().parameters, p) ?? '');
                    if (curVal !== baseVal) {
                        activeCount++;
                    }
                }
            });
        }
    });
    return activeCount;
}

function renderTabsContainer(container, containerDef, manifest, pageTheme) {
    const div = document.createElement('div');
    div.setAttribute('data-name', containerDef.name);
    applyDesignTokens(div, containerDef, false, manifest);
    const styles = containerDef.styles || {};
    const opts = containerDef.options || {};
    const containerTheme = getStyle(styles, 'THEME') || pageTheme;

    const tabPosition = (getOption(opts, 'TAB_POSITION') || getStyle(styles, 'TAB_POSITION') || 'TOP').toUpperCase();
    div.className = `report-container container-tabs tabs-position-${tabPosition.toLowerCase()}`;

    const slotMap = containerDef.slotMap || {};
    const slotDetails = containerDef.slotDetails || {};
    const slotKeys = Object.keys(slotMap);
    if (slotKeys.length === 0) {
        container.appendChild(div);
        return;
    }

    const nav = document.createElement('div');
    nav.className = 'tabs-nav';

    const content = document.createElement('div');
    content.className = 'tabs-content';

    slotKeys.forEach((key, idx) => {
        const itemName = slotMap[key];
        const detail = slotDetails[key] || {};
        const tabBtn = document.createElement('div');
        tabBtn.className = 'tabs-tab' + (idx === 0 ? ' active' : '');
        tabBtn.setAttribute('data-slot', key);

        const iconVal = detail.icon;
        if (iconVal) {
            const iconEl = document.createElement('span');
            iconEl.className = 'tab-icon';
            if (iconVal.includes('.') || iconVal.includes('/')) {
                iconEl.innerHTML = `<img src="${escHtml(safeUrl(iconVal))}" style="width:16px;height:16px;vertical-align:middle;">`;
            } else {
                iconEl.textContent = iconVal;
            }
            tabBtn.appendChild(iconEl);
            tabBtn.appendChild(document.createTextNode(' '));
        }

        const labelSpan = document.createElement('span');
        labelSpan.className = 'tab-label';
        const targetVisual = (manifest?.visuals || []).find(v => v.name.toLowerCase() === itemName.toLowerCase());
        labelSpan.textContent = (targetVisual && targetVisual.title) ? targetVisual.title : itemName;
        tabBtn.appendChild(labelSpan);

        const badgeVal = detail.badge;
        if (badgeVal != null && badgeVal !== '') {
            const badgeEl = document.createElement('span');
            badgeEl.className = 'tab-badge';
            badgeEl.textContent = badgeVal;
            tabBtn.appendChild(badgeEl);
        }

        const panel = document.createElement('div');
        panel.className = 'tabs-panel' + (idx === 0 ? ' active' : '');
        panel.setAttribute('data-slot', key);

        if (targetVisual) {
            renderVisual(panel, targetVisual, containerTheme, manifest);
        } else {
            const nested = (manifest?.containers || []).find(c => c.name.toLowerCase() === itemName.toLowerCase());
            if (nested) renderContainer(panel, nested, manifest, containerTheme);
        }

        tabBtn.addEventListener('click', () => {
            nav.querySelectorAll('.tabs-tab').forEach(t => t.classList.remove('active'));
            content.querySelectorAll('.tabs-panel').forEach(p => p.classList.remove('active'));
            tabBtn.classList.add('active');
            panel.classList.add('active');
            setTimeout(() => resizeChartsIn(panel), 50);
        });

        nav.appendChild(tabBtn);
        content.appendChild(panel);
    });

    div.appendChild(nav);
    div.appendChild(content);
    container.appendChild(div);
    setTimeout(() => resizeChartsIn(content), 50);
}

function renderAccordionContainer(container, containerDef, manifest, pageTheme) {
    const div = document.createElement('div');
    div.setAttribute('data-name', containerDef.name);
    applyDesignTokens(div, containerDef, false, manifest);
    div.className = 'report-container container-accordion';

    const styles = containerDef.styles || {};
    const opts = containerDef.options || {};
    const containerTheme = getStyle(styles, 'THEME') || pageTheme;
    const defaultOpen = (getOption(opts, 'DEFAULT_OPEN') || '').toLowerCase();

    const slotMap = containerDef.slotMap || {};
    const slotKeys = Object.keys(slotMap);

    slotKeys.forEach(key => {
        const itemName = slotMap[key];
        const targetVisual = (manifest?.visuals || []).find(v => v.name.toLowerCase() === itemName.toLowerCase());
        const sectionTitle = (targetVisual && targetVisual.title) ? targetVisual.title : itemName;

        const itemEl = document.createElement('div');
        itemEl.className = 'accordion-item';
        const isOpen = defaultOpen && (key.toLowerCase() === defaultOpen || itemName.toLowerCase() === defaultOpen);
        if (isOpen) itemEl.classList.add('open');

        const headerEl = document.createElement('div');
        headerEl.className = 'accordion-header';
        headerEl.innerHTML = `<span>${escHtml(sectionTitle)}</span><span class="accordion-chevron">&#x25BC;</span>`;

        const contentEl = document.createElement('div');
        contentEl.className = 'accordion-content';

        if (targetVisual) {
            renderVisual(contentEl, targetVisual, containerTheme, manifest);
        } else {
            const nested = (manifest?.containers || []).find(c => c.name.toLowerCase() === itemName.toLowerCase());
            if (nested) renderContainer(contentEl, nested, manifest, containerTheme);
        }

        headerEl.addEventListener('click', () => {
            const opened = itemEl.classList.toggle('open');
            if (opened) setTimeout(() => resizeChartsIn(contentEl), 50);
        });

        itemEl.appendChild(headerEl);
        itemEl.appendChild(contentEl);
        div.appendChild(itemEl);
    });

    container.appendChild(div);
    setTimeout(() => resizeChartsIn(div), 50);
}
