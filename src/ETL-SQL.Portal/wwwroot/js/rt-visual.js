// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/rt-visual.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Visual dispatch, cards, text, images, HTML, and maximization.
 */
// Rendering, layout, actions, chrome, and the entry module deliberately form a cycle.
// Cross-cycle functions use hoisted declarations; imported state is read only when
// those functions run. Boot starts after the graph has evaluated. Preserve that
// ordering when adding top-level work or changing functions to const declarations.
import { abbreviateNumber, errorEl, escHtml, formatValue, getOption, getStyle, isOff, isOn, renderInlineMarkdown, safeUrl, simpleMarkdown } from './rt-util.js';
import { ALLOWED_TOKEN_NAMES, applyDesignTokens, isAllowedTokenName } from './rt-theme.js';
import { actionsFor, evaluateExpressionAgainstParameters, executeAction, matchesCondition } from './rt-actions.js';
import { parameters } from './rt-state.js';
import { hasDeferredRows, loadVisualRows, publishExportState } from './rt-data.js';
import { renderTable } from './rt-table.js';
import { renderCheckbox, renderNumberbox, renderSearch, renderSlicer, renderSlider, renderTextbox } from './rt-controls-input.js';
import { renderDatePicker, renderRelDatePicker } from './rt-controls-date.js';
import { renderMatrix } from './rt-matrix.js';
import { renderMissingChartPayload, renderNativeSvg } from './rt-charts.js';
import { postDrillUp } from './rt-transport.js';

let _maximizedVisualCard = null;

export function resizeChartsIn(section) {
    section.querySelectorAll('.chart-wrapper').forEach(() => {
    });
}

const NON_MAXIMIZABLE_CONTROL_TYPES = new Set([
    'SLICER', 'MULTISELECT', 'DATEPICKER', 'RELDATEPICKER', 'SLIDER',
    'SEARCH', 'CHECKBOX', 'TEXTBOX', 'NUMBERBOX'
]);

function shouldShowVisualToolbar(type, styles) {
    const allowMaximize = getStyle(styles, 'ALLOW_MAXIMIZE');
    if (isOn(allowMaximize)) return true;
    if (isOff(allowMaximize)) return false;
    return !NON_MAXIMIZABLE_CONTROL_TYPES.has(type);
}

function addVisualToolbar(card) {
    card.classList.add('has-visual-toolbar');
    const toolbar = document.createElement('div');
    toolbar.className = 'visual-toolbar';

    const maxBtn = document.createElement('button');
    maxBtn.type = 'button';
    maxBtn.className = 'visual-tool-btn';
    maxBtn.textContent = '[]';
    maxBtn.title = 'Maximize visual';
    maxBtn.setAttribute('aria-label', 'Maximize visual');
    maxBtn.addEventListener('click', e => {
        e.stopPropagation();
        toggleVisualMaximize(card, maxBtn);
    });

    toolbar.appendChild(maxBtn);
    card.appendChild(toolbar);
}

function toggleVisualMaximize(card, button) {
    if (_maximizedVisualCard && _maximizedVisualCard !== card) {
        closeMaximizedVisual();
    }

    const isOpening = !card.classList.contains('visual-maximized');
    if (!isOpening) {
        closeMaximizedVisual();
        return;
    }

    _maximizedVisualCard = card;
    // Teleport card to <body> so position:fixed anchors to viewport regardless of
    // any CSS transform/contain on ancestor elements in the portal layout.
    card._maxOriginalParent = card.parentElement;
    card._maxNextSibling    = card.nextSibling;

    // Capture inherited design tokens before teleporting outside container/page DOM hierarchy
    const computed = typeof getComputedStyle === 'function' ? getComputedStyle(card) : null;
    card._maxOrigTokens = {};
    const tokenProps = new Set(ALLOWED_TOKEN_NAMES);
    for (let i = 0; i < card.style.length; i++) {
        const p = card.style[i];
        if (isAllowedTokenName(p)) tokenProps.add(p);
    }
    if (computed) {
        for (let i = 0; i < computed.length; i++) {
            const p = computed[i];
            if (isAllowedTokenName(p)) tokenProps.add(p);
        }
    }
    for (const token of tokenProps) {
        const inlineVal = card.style.getPropertyValue(token);
        if (inlineVal) {
            card._maxOrigTokens[token] = inlineVal;
        }
        const effVal = inlineVal || (computed ? computed.getPropertyValue(token) : '');
        if (effVal) {
            card.style.setProperty(token, effVal.trim());
        }
    }

    document.body.appendChild(card);
    card.classList.add('visual-maximized');
    // Override any transparent/glass inline background so maximized card is fully opaque.
    card._maxOrigBg      = card.style.backgroundColor;
    card._maxOrigBgImage = card.style.backgroundImage;
    card.style.backgroundColor = card.classList.contains('theme-dark') ? '#1e1e1e' : '#fff';
    card.style.backgroundImage = 'none';
    document.body.classList.add('visual-maximize-active');
    if (button) {
        button.textContent = 'x';
        button.title = 'Restore visual';
        button.setAttribute('aria-label', 'Restore visual');
    }
    setTimeout(() => resizeChartsIn(card), 50);
}

export function closeMaximizedVisual() {
    if (!_maximizedVisualCard) return;

    const card = _maximizedVisualCard;
    card.classList.remove('visual-maximized');
    document.body.classList.remove('visual-maximize-active');

    // Restore card to its original position in the layout
    if (card._maxOriginalParent) {
        card._maxOriginalParent.insertBefore(card, card._maxNextSibling || null);
        card._maxOriginalParent = null;
        card._maxNextSibling    = null;
    }

    // Restore original design tokens
    const currentTokenProps = [];
    for (let i = 0; i < card.style.length; i++) {
        const p = card.style[i];
        if (isAllowedTokenName(p)) currentTokenProps.push(p);
    }
    for (const p of currentTokenProps) {
        if (card._maxOrigTokens && card._maxOrigTokens[p] !== undefined) {
            card.style.setProperty(p, card._maxOrigTokens[p]);
        } else {
            card.style.removeProperty(p);
        }
    }
    if (card._maxOrigTokens) {
        for (const [token, val] of Object.entries(card._maxOrigTokens)) {
            card.style.setProperty(token, val);
        }
    }
    card._maxOrigTokens = null;

    // Reset inline dimensions on chart container divs to let layout reflow correctly
    card.querySelectorAll('.chart-wrapper > div').forEach(el => {
        el.style.width = '100%';
        el.style.height = '100%';
    });

    // Restore original background
    card.style.backgroundColor = card._maxOrigBg      || '';
    card.style.backgroundImage = card._maxOrigBgImage || '';
    card._maxOrigBg      = null;
    card._maxOrigBgImage = null;

    const button = card.querySelector('.visual-tool-btn');
    if (button) {
        button.textContent = '[]';
        button.title = 'Maximize visual';
        button.setAttribute('aria-label', 'Maximize visual');
    }

    _maximizedVisualCard = null;
    setTimeout(() => resizeChartsIn(card), 50);
}

// Filter types that render without requiring rows
const FILTER_TYPES = new Set(['SLICER', 'TABLE', 'CARD', 'TEXT', 'HTML', 'DATEPICKER', 'RELDATEPICKER', 'SLIDER', 'MULTISELECT', 'SEARCH', 'CHECKBOX', 'TEXTBOX', 'NUMBERBOX', 'IMAGE']);

export function renderVisual(container, visual, pageTheme, manifest, embedDepth = 0) {
    const card = document.createElement('div');
    card.className = 'visual-card';
    card.setAttribute('data-name', visual.name);
    applyDesignTokens(card, visual, false, manifest);
    card.setAttribute('data-visual-name', visual.name); // Compatibility

    const tag = getOption(visual.options, 'TAG');
    if (tag) card.setAttribute('data-tag', tag);

    const vopts = visual.options || {};
    const visibleExpr = vopts['VISIBLE'] || vopts['visible'];
    if (visibleExpr != null && !evaluateExpressionAgainstParameters(visibleExpr, parameters)) {
        card.style.display = 'none';
        card.setAttribute('aria-hidden', 'true');
    }

    const dependsOn = vopts['DEPENDS_ON'] || vopts['depends_on'];
    if (dependsOn) card.setAttribute('data-depends-on', dependsOn);

    /** @type {EtlSqlVisualHost} */ (card)._visualData = visual;

    // Apply WIDTH / HEIGHT / TOOLTIP from styles
    const vstyles = visual.styles || {};
    const width   = getStyle(vstyles, 'WIDTH');
    const height  = getStyle(vstyles, 'HEIGHT');
    const styleTooltip = getStyle(vstyles, 'TOOLTIP');
    const tooltip = visual.tooltip;

    const opacity = getStyle(vstyles, 'OPACITY');
    const bgColor = getStyle(vstyles, 'BACKGROUND-COLOR') || getStyle(vstyles, 'BACKGROUND');
    const border = getStyle(vstyles, 'BORDER');
    const borderRadius = getStyle(vstyles, 'BORDER-RADIUS') || getStyle(vstyles, 'BORDER_RADIUS');
    const shadow = getStyle(vstyles, 'SHADOW');

    if (width)   card.style.width   = width;
    if (height)  card.style.height  = height;
    if (opacity) card.style.opacity = opacity;
    if (border) card.style.border = border;
    if (borderRadius) card.style.borderRadius = borderRadius;
    if (isOn(shadow)) card.style.boxShadow = '0 6px 18px rgba(15, 23, 42, 0.16)';
    else if (shadow && !isOff(shadow)) card.style.boxShadow = shadow;
    if (bgColor) {
        const normalized = bgColor.trim().toLowerCase();
        const isTransparent = normalized === 'transparent' || normalized === 'rgba(0,0,0,0)' || normalized === 'rgba(0, 0, 0, 0)';
        if (isTransparent) {
            card.style.backgroundColor = 'transparent';
            card.style.backgroundImage = 'none';
        } else {
            // Layer over the CSS theme base color; keeps #1e1e1e dark base intact for dark-themed cards.
            card.style.backgroundImage = `linear-gradient(${bgColor}, ${bgColor})`;
        }
    }
    const tooltipText = styleTooltip || (tooltip && tooltip.type === 'text' ? tooltip.text : null);
    if (tooltipText) card.title = tooltipText;
    if (isOff(getOption(visual.options, 'VISIBLE'))) {
        card.style.display = 'none';
    }

    const title = document.createElement('h3');
    const customTitle = getOption(visual.options, 'TITLE') || getOption(visual.options, 'title');
    if (customTitle) {
        if (visual.titleIsMarkdown) {
            title.innerHTML = renderInlineMarkdown(customTitle);
        } else {
            title.textContent = customTitle;
        }
    } else {
        title.textContent = visual.name;
    }

    const tColor = getStyle(vstyles, 'TITLE_COLOR');
    const tSize = getStyle(vstyles, 'TITLE_SIZE');
    const tWeight = getStyle(vstyles, 'TITLE_WEIGHT');
    const tFont = getStyle(vstyles, 'TITLE_FONT');
    const tAlign = getStyle(vstyles, 'TITLE_ALIGN');

    if (tColor) title.style.color = tColor;
    if (tSize) title.style.fontSize = tSize.includes('px') || tSize.includes('rem') || tSize.includes('em') || tSize.includes('%') ? tSize : (tSize + 'px');
    if (tWeight) title.style.fontWeight = tWeight;
    if (tFont) title.style.fontFamily = tFont;
    if (tAlign) title.style.textAlign = tAlign.toLowerCase();

    const customSubtitle = getOption(visual.options, 'SUBTITLE') || getOption(visual.options, 'subtitle');
    let subtitleEl = null;
    if (customSubtitle) {
        subtitleEl = document.createElement('div');
        subtitleEl.className = 'card-subtitle visual-subtitle';
        if (visual.subtitleIsMarkdown) {
            subtitleEl.innerHTML = renderInlineMarkdown(customSubtitle);
        } else {
            subtitleEl.textContent = customSubtitle;
        }
        const sColor = getStyle(vstyles, 'SUBTITLE_COLOR');
        const sSize = getStyle(vstyles, 'SUBTITLE_SIZE');
        const sWeight = getStyle(vstyles, 'SUBTITLE_WEIGHT');
        const sFont = getStyle(vstyles, 'SUBTITLE_FONT');
        const sAlign = getStyle(vstyles, 'SUBTITLE_ALIGN') || tAlign;

        if (sColor) subtitleEl.style.color = sColor;
        if (sSize) subtitleEl.style.fontSize = sSize.includes('px') || sSize.includes('rem') || sSize.includes('em') || sSize.includes('%') ? sSize : (sSize + 'px');
        if (sWeight) subtitleEl.style.fontWeight = sWeight;
        if (sFont) subtitleEl.style.fontFamily = sFont;
        if (sAlign) subtitleEl.style.textAlign = sAlign.toLowerCase();
    }

    const type = (visual.visualType || '').toUpperCase();
    // Hide outer redundant header if CARD has its own internal card-label or mapping:label
    const isCardType = type === 'CARD' || type === 'KPI' || Boolean(getOption(visual.options, 'mapping:label'));
    if (isCardType) title.style.display = 'none';

    card.appendChild(title);
    if (subtitleEl && !isCardType) card.appendChild(subtitleEl);
    if (type === 'HTML') {
        card.id = htmlVisualContainerId(visual.name);
    }
    if (shouldShowVisualToolbar(type, vstyles)) {
        addVisualToolbar(card);
    }

    if (visual.error) {
        card.appendChild(errorEl(visual.error));
        container.appendChild(card);
        return;
    }

    // Deferred ON_RUN visuals show a placeholder until the paginated page is run.
    if (visual.isHidden) {
        card.classList.add('deferred-visual');
        const ph = document.createElement('div');
        ph.className = 'deferred-placeholder';
        ph.textContent = 'Configure parameters above and click Run to load data.';
        card.appendChild(ph);
        container.appendChild(card);
        return;
    }

    if (hasDeferredRows(visual)) {
        const loading = document.createElement('div');
        loading.className = 'empty-state';
        const count = visual.rowsSource && visual.rowsSource.rowCount ? Number(visual.rowsSource.rowCount).toLocaleString() : '';
        loading.innerHTML = '<div class="empty-icon">...</div>' +
                            '<p>Loading' + (count ? ' ' + escHtml(count) : '') + ' rows.</p>';
        card.appendChild(loading);
        container.appendChild(card);

        const nextSibling = card.nextSibling;
        loadVisualRows(visual)
            .then(() => {
                const parent = card.parentElement;
                if (!parent) return;
                card.remove();
                renderVisual(parent, visual, pageTheme, manifest, embedDepth);
                const rendered = parent.lastElementChild;
                if (nextSibling && rendered) parent.insertBefore(rendered, nextSibling);
            })
            .catch(e => {
                loading.replaceChildren(errorEl(e.message || 'Failed to load visual rows.'));
                publishExportState('error', { reason: 'lazy-rows-failed', message: e.message });
            });
        return;
    }

    // Empty state handling: If not a filter/text type and no data rows, show "No Data" icon + message.
    if (!FILTER_TYPES.has(type) && (!visual.rows || visual.rows.length === 0)) {
        const empty = document.createElement('div');
        empty.className = 'empty-state';
        empty.innerHTML = '<div class="empty-icon">\u2205</div>' +
                          '<p>No data matches the current filters.</p>';
        card.appendChild(empty);
        container.appendChild(card);
        return;
    }

    // Resolve effective theme: visual-level overrides page-level
    const effectiveTheme = getStyle(vstyles, 'THEME') || pageTheme || null;
    if (effectiveTheme) card.classList.add('theme-' + effectiveTheme.toLowerCase());

    switch (type) {
        case 'TABLE':       renderTable(card, visual, manifest);              break;
        case 'CARD':        renderCard(card, visual);                         break;
        case 'SLICER':
        case 'MULTISELECT': renderSlicer(card, visual, manifest);             break;
        case 'TEXT':        renderText(card, visual);                         break;
        case 'HTML':        renderHtmlVisual(card, visual, manifest, embedDepth); break;
        case 'DATEPICKER':    renderDatePicker(card, visual, manifest);        break;
        case 'RELDATEPICKER': renderRelDatePicker(card, visual, manifest);     break;
        case 'SLIDER':      renderSlider(card, visual, manifest);             break;
        case 'SEARCH':      renderSearch(card, visual, manifest);             break;
        case 'CHECKBOX':    renderCheckbox(card, visual, manifest);           break;
        case 'TEXTBOX':     renderTextbox(card, visual, manifest);            break;
        case 'NUMBERBOX':   renderNumberbox(card, visual, manifest);          break;
        case 'IMAGE':       renderImage(card, visual);                        break;
        // MATRIX is an interactive pivot table, not a chart. Its HTML renderer preserves
        // nested headers, subtotals, scrolling, and expand/collapse behavior.
        case 'MATRIX':      renderMatrix(card, visual);                       break;
        default:            visual.nativeSvg
                                ? renderNativeSvg(card, visual, manifest, effectiveTheme)
                                : renderMissingChartPayload(card, visual); break;
    }

    // DRILL_IN breadcrumb: shown when visual has an active drill state
    if (visual.drillState?.hierarchy?.length > 0) {
        const bc = document.createElement('div');
        bc.className = 'drill-breadcrumb';
        // Segments: root label (hierarchy[0]) + each path segment
        const segs = [{ label: visual.drillState.hierarchy[0], depth: 0 }]
            .concat((visual.drillState.path || []).map((s, i) => ({ label: s.value, depth: i + 1 })));
        segs.forEach((seg, i) => {
            const sp = document.createElement('span');
            const isActive = i === segs.length - 1;
            sp.className = 'bc-seg' + (isActive ? ' bc-seg-active' : ' bc-seg-link');
            sp.textContent = seg.label;
            if (!isActive) {
                sp.addEventListener('click', () => postDrillUp(visual.name, seg.depth));
            }
            bc.appendChild(sp);
            if (!isActive) {
                const sep = document.createElement('span');
                sep.className = 'bc-sep';
                sep.textContent = ' › ';
                bc.appendChild(sep);
            }
        });
        card.insertBefore(bc, card.firstChild);
    }

    // Drill-through affordance: cursor + badge when visual has DRILL_DOWN actions
    if ((visual.actions || []).some(a => a.type === 'DRILL_DOWN')) {
        card.classList.add('has-drill-down');
        const badge = document.createElement('span');
        badge.className = 'drill-badge';
        badge.title = 'Right-click to drill through';
        badge.textContent = '⬇';
        card.appendChild(badge);
    }

    // Drill-in affordance: cursor + badge when visual has DRILL_IN actions
    if ((visual.actions || []).some(a => a.type === 'DRILL_IN')) {
        card.classList.add('has-drill-in');
        const badge = document.createElement('span');
        badge.className = 'drill-badge';
        badge.title = 'Click to drill in';
        badge.textContent = '↧';
        card.appendChild(badge);
    }

    container.appendChild(card);
}

const HTML_VISUAL_ELEMENTS = new Set([
    'DIV', 'SPAN', 'SECTION', 'ARTICLE', 'ASIDE', 'HEADER', 'FOOTER', 'NAV', 'MAIN',
    'H1', 'H2', 'H3', 'H4', 'H5', 'H6', 'P', 'BR', 'HR', 'PRE', 'CODE', 'BLOCKQUOTE',
    'EM', 'STRONG', 'I', 'B', 'U', 'S', 'SMALL', 'SUB', 'SUP', 'MARK', 'ABBR', 'TIME',
    'CITE', 'Q', 'DFN', 'VAR', 'KBD', 'SAMP', 'UL', 'OL', 'LI', 'DL', 'DT', 'DD',
    'TABLE', 'THEAD', 'TBODY', 'TFOOT', 'TR', 'TH', 'TD', 'CAPTION', 'COLGROUP', 'COL',
    'IMG', 'FIGURE', 'FIGCAPTION', 'PICTURE', 'SOURCE', 'A', 'BUTTON', 'DETAILS',
    'SUMMARY', 'DATA', 'METER', 'PROGRESS', 'OUTPUT'
]);
const HTML_VISUAL_GLOBAL_ATTRIBUTES = new Set([
    'class', 'id', 'title', 'lang', 'dir', 'role', 'tabindex', 'hidden'
]);
const HTML_VISUAL_ELEMENT_ATTRIBUTES = {
    A: new Set(['href', 'target', 'rel']),
    IMG: new Set(['src', 'alt', 'width', 'height', 'loading']),
    BUTTON: new Set(['type', 'disabled', 'data-action', 'data-param', 'data-value']),
    TD: new Set(['colspan', 'rowspan', 'scope', 'headers']),
    TH: new Set(['colspan', 'rowspan', 'scope', 'headers']),
    COL: new Set(['span']), COLGROUP: new Set(['span']),
    OL: new Set(['start', 'type', 'reversed']), TIME: new Set(['datetime']),
    METER: new Set(['min', 'max', 'low', 'high', 'optimum', 'value']),
    PROGRESS: new Set(['max', 'value']), DATA: new Set(['value']), ABBR: new Set(['title']),
    BLOCKQUOTE: new Set(['cite']), Q: new Set(['cite']),
    SOURCE: new Set(['srcset', 'type', 'media']), DETAILS: new Set(['open'])
};

function htmlVisualContainerId(name) {
    return 'etl-v-' + String(name || '').toLowerCase().replaceAll(' ', '-');
}

function isSafeHtmlVisualUrl(value) {
    const url = String(value || '').trim();
/* eslint-disable-next-line no-control-regex -- matching control characters is the point:
       a URL carrying one is how a javascript: scheme gets past a prefix check. */
    if (/[\u0000-\u001f\u007f]/.test(url)) return false;
    if (/^(https?:|mailto:|tel:|#)/i.test(url)) return true;
    if (/^data:image\/(png|jpeg|gif|webp)(;|,)/i.test(url)) return true;
    if (!/^data:image\/svg\+xml(?:;charset=[^;,]+)?(?:;base64)?,/i.test(url)) return false;
    try {
        const comma = url.indexOf(',');
        const header = url.slice(0, comma);
        const payload = url.slice(comma + 1);
        const svg = /;base64/i.test(header) ? atob(payload) : decodeURIComponent(payload);
        return !/<\s*(?:script|foreignObject)\b|\bon[a-z]+\s*=|(?:href|src)\s*=\s*['"]?\s*javascript:/i.test(svg);
    } catch {
        return false;
    }
}

function copyHtmlVisualNode(source, ownerDocument) {
    if (source.nodeType === Node.TEXT_NODE) return ownerDocument.createTextNode(source.nodeValue || '');
    if (source.nodeType !== Node.ELEMENT_NODE || !HTML_VISUAL_ELEMENTS.has(source.tagName)) return null;

    const target = ownerDocument.createElement(source.tagName.toLowerCase());
    const elementAttributes = HTML_VISUAL_ELEMENT_ATTRIBUTES[source.tagName] || new Set();
    for (const attribute of source.attributes) {
        const name = attribute.name.toLowerCase();
        if (name.startsWith('on') || name === 'style') continue;
        if (!HTML_VISUAL_GLOBAL_ATTRIBUTES.has(name)
            && !name.startsWith('aria-')
            && !name.startsWith('data-etl-')
            && !elementAttributes.has(name)) continue;
        if (['href', 'src', 'cite', 'srcset'].includes(name) && !isSafeHtmlVisualUrl(attribute.value)) continue;
        if (source.tagName === 'BUTTON' && name === 'type' && attribute.value.toLowerCase() !== 'button') continue;
        if (source.tagName === 'A' && name === 'target' && attribute.value !== '_blank') continue;
        target.setAttribute(name, attribute.value);
    }

    if (source.tagName === 'A' && target.getAttribute('target') === '_blank') {
        target.setAttribute('rel', 'noopener noreferrer');
    }
    if (source.tagName === 'IMG' && !target.hasAttribute('alt')) return null;
    for (const child of source.childNodes) {
        const copied = copyHtmlVisualNode(child, ownerDocument);
        if (copied) target.appendChild(copied);
    }
    return target;
}

function renderHtmlVisual(container, visual, manifest, embedDepth) {
    const wrapper = document.createElement('div');
    wrapper.className = 'html-visual-content';
    const fallback = String(visual.htmlFallback || visual.name || 'HTML visual');
    wrapper.setAttribute('aria-label', fallback);

    if (visual.htmlCss) {
        const style = document.createElement('style');
        style.className = 'html-visual-scoped-style';
        style.textContent = String(visual.htmlCss);
        container.appendChild(style);
    }

    const parsed = new DOMParser().parseFromString(String(visual.htmlContent || ''), 'text/html');
    for (const child of parsed.body.childNodes) {
        const copied = copyHtmlVisualNode(child, document);
        if (copied) wrapper.appendChild(copied);
    }

    wrapper.addEventListener('click', event => {
        const trigger = /** @type {Element} */ (event.target).closest('[data-action]');
        if (!trigger || !wrapper.contains(trigger)) return;
        const actionType = String(/** @type {HTMLElement} */ (trigger).dataset.action || '').toUpperCase();
        const declared = (visual.actions || []).find(action =>
            String(action.type || '').toUpperCase() === actionType
            && String(action.trigger || '').toUpperCase() === 'ON_CLICK');
        if (!declared) return;
        const action = Object.assign({}, declared);
        if (/** @type {HTMLElement} */ (trigger).dataset.param) action.parameterName = /** @type {HTMLElement} */ (trigger).dataset.param;
        if (/** @type {HTMLElement} */ (trigger).dataset.value !== undefined) {
            action.valueSource = 'LITERAL';
            action.literalValue = /** @type {HTMLElement} */ (trigger).dataset.value;
        }
        executeAction(action, [/** @type {HTMLElement} */ (trigger).dataset.value ?? ''], ['VALUE'], visual.name, visual);
    });

    container.appendChild(wrapper);

    const byName = new Map((manifest.visuals || [])
        .map(candidate => [String(candidate.name || '').toLowerCase(), candidate]));
    const embeds = new Map((visual.htmlEmbeds || [])
        .map(embed => [String(embed.id || ''), embed]));
    const renderedEmbedIds = new Set();
    wrapper.querySelectorAll('[data-etl-embed-id]').forEach(slot => {
        const embedId = String(slot.getAttribute('data-etl-embed-id') || '');
        const descriptor = embeds.get(embedId);
        if (!descriptor) {
            slot.replaceChildren(errorEl('Embedded visual descriptor is unavailable.'));
            return;
        }
        if (renderedEmbedIds.has(embedId)) {
            slot.replaceChildren(errorEl('Duplicate embedded visual slot was rejected.'));
            return;
        }
        renderedEmbedIds.add(embedId);
        if (embedDepth >= 2) {
            slot.replaceChildren(errorEl('Embedded visual depth exceeds the supported limit.'));
            return;
        }
        const target = descriptor.visual
            || byName.get(String(descriptor.targetName || '').toLowerCase());
        if (!target) {
            slot.replaceChildren(errorEl(`Embedded visual not found: ${descriptor.targetName || ''}`));
            return;
        }
        renderVisual(slot, target, null, manifest, embedDepth + 1);
    });

    const microCharts = new Map((visual.microCharts || [])
        .filter(micro => micro.role === 'html.inline')
        .map(micro => [String(micro.id || ''), micro]));
    const renderedMicroChartIds = new Set();
    wrapper.querySelectorAll('[data-etl-microchart-id]').forEach(slot => {
        const microChartId = String(slot.getAttribute('data-etl-microchart-id') || '');
        const microChart = microCharts.get(microChartId);
        if (!microChart || renderedMicroChartIds.has(microChartId)) {
            slot.replaceChildren(errorEl('Inline micro-chart descriptor is unavailable.'));
            return;
        }
        renderedMicroChartIds.add(microChartId);
        slot.classList.add('html-inline-microchart');
        slot.setAttribute('role', 'img');
        slot.setAttribute('aria-label', microChart.accessibleLabel || microChart.plainText || 'Indicator');
        slot.innerHTML = String(microChart.svg || '');
    });
}

export function renderCard(container, visual) {
    const opts = visual.options || {};
    const cardTitle    = getOption(opts, 'title') || visual.name;
    const cardSubtitle = getOption(opts, 'subtitle') || '';

    // ── Value ──────────────────────────────────────────────────────────
    const valueColName = getOption(opts, 'mapping:value');
    const valIdx = valueColName
        ? (visual.columns || []).findIndex(c => c.toLowerCase() === valueColName.toLowerCase())
        : 0;
    const row = visual.rows && visual.rows[0] ? visual.rows[0] : null;
    const rawCell = row ? (row[valIdx >= 0 ? valIdx : 0] ?? null) : null;
    const isNumeric = rawCell !== null && rawCell !== '' && !isNaN(Number(rawCell));
    const rawValue = isNumeric ? parseFloat(String(rawCell)) : null;

    const formatOpt    = getOption(opts, 'format');
    const doAbbreviate = isOn(getOption(opts, 'abbreviate'));
    const prefix       = getOption(opts, 'prefix') || '';
    const suffix       = getOption(opts, 'suffix') || '';

    let displayValue;
    if (rawCell === null) {
        displayValue = 'No data';
    } else if (!isNumeric) {
        displayValue = prefix + String(rawCell) + suffix;
    } else if (doAbbreviate && rawValue !== null) {
        displayValue = prefix + abbreviateNumber(rawValue, formatOpt) + suffix;
    } else if (formatOpt && rawValue !== null) {
        displayValue = prefix + formatValue(rawValue, formatOpt) + suffix;
    } else {
        displayValue = prefix + String(rawValue ?? rawCell) + suffix;
    }

    // ── Goal ───────────────────────────────────────────────────────────
    const goalColName = getOption(opts, 'mapping:goal');
    let goalValue = null;
    if (goalColName && row) {
        const gIdx = (visual.columns || []).findIndex(c => c.toLowerCase() === goalColName.toLowerCase());
        if (gIdx >= 0) goalValue = parseFloat(row[gIdx] ?? '0');
    }
    if (goalValue === null) {
        const goalOpt = getOption(opts, 'goal');
        if (goalOpt !== null) goalValue = parseFloat(goalOpt);
    }

    // ── Status ─────────────────────────────────────────────────────────
    const closePct = parseFloat(getOption(opts, 'close_pct') ?? '0.80');
    const metPct   = parseFloat(getOption(opts, 'met_pct')   ?? '1.00');
    let status = null, ratio = null;
    if (goalValue !== null && rawValue !== null && goalValue !== 0) {
        ratio = rawValue / goalValue;
        if      (ratio >= metPct)   status = 'met';
        else if (ratio >= closePct) status = 'close';
        else                        status = 'missed';
    }

    // ── Colors & icons ─────────────────────────────────────────────────
    const colors = {
        met:    getOption(opts, 'color_met')    || '#10b981',
        close:  getOption(opts, 'color_close')  || '#f59e0b',
        missed: getOption(opts, 'color_missed') || '#ef4444'
    };
    const iconSets = {
        TRAFFIC: { met: '🟢', close: '🟡', missed: '🔴' },
        ARROWS:  { met: '↑',  close: '→',  missed: '↓'  },
        CHECKS:  { met: '✓',  close: '~',  missed: '✗'  }
    };
    const iconSetName  = (getOption(opts, 'icon_set') || '').toUpperCase();
    const presetIcons  = iconSets[iconSetName] || null;
    const icons = {
        met:    getOption(opts, 'icon_met')    ?? (presetIcons ? presetIcons.met    : '✓'),
        close:  getOption(opts, 'icon_close')  ?? (presetIcons ? presetIcons.close  : '⚠'),
        missed: getOption(opts, 'icon_missed') ?? (presetIcons ? presetIcons.missed : '✗')
    };

    // ── Delta ──────────────────────────────────────────────────────────
    const deltaColName = getOption(opts, 'mapping:delta');
    let deltaAmount = null;
    if (deltaColName && row) {
        const dIdx = (visual.columns || []).findIndex(c => c.toLowerCase() === deltaColName.toLowerCase());
        if (dIdx >= 0 && rawValue !== null) deltaAmount = rawValue - parseFloat(row[dIdx] ?? '0');
    }
    const deltaFormat = getOption(opts, 'delta_format') || formatOpt;
    let deltaLabel = '';
    const deltaLabelMapping = getOption(opts, 'mapping:delta_label');
    if (deltaLabelMapping && row) {
        const dlIdx = (visual.columns || []).findIndex(c => c.toLowerCase() === deltaLabelMapping.toLowerCase());
        if (dlIdx >= 0 && row[dlIdx] !== undefined && row[dlIdx] !== null) {
            deltaLabel = String(row[dlIdx]);
        }
    }
    if (!deltaLabel) {
        const deltaLabelOpt = getOption(opts, 'delta_label');
        if (deltaLabelOpt) {
            if (row) {
                const dlIdx = (visual.columns || []).findIndex(c => c.toLowerCase() === deltaLabelOpt.toLowerCase());
                if (dlIdx >= 0 && row[dlIdx] !== undefined && row[dlIdx] !== null) {
                    deltaLabel = String(row[dlIdx]);
                } else {
                    deltaLabel = deltaLabelOpt;
                }
            } else {
                deltaLabel = deltaLabelOpt;
            }
        }
    }
    const trendDir    = (getOption(opts, 'trend_dir') || 'POSITIVE_UP').toUpperCase();

    // ── Status label override ──────────────────────────────────────────
    let subtitleText = cardSubtitle;
    if (status === 'met'    && getOption(opts, 'label_met'))    subtitleText = getOption(opts, 'label_met');
    if (status === 'close'  && getOption(opts, 'label_close'))  subtitleText = getOption(opts, 'label_close');
    if (status === 'missed' && getOption(opts, 'label_missed')) subtitleText = getOption(opts, 'label_missed');

    // ── Build HTML ─────────────────────────────────────────────────────
    // Status badge
    let badgeHtml = '';
    if (status) {
        badgeHtml = `<span class="card-status-badge" style="background:${escHtml(colors[status])}">${escHtml(icons[status])}</span>`;
    }

    // Delta row
    let deltaHtml = '';
    if (deltaAmount !== null) {
        const isPos    = deltaAmount >= 0;
        const isGood   = trendDir === 'POSITIVE_UP' ? isPos : !isPos;
        const arrow    = isPos ? '▲' : '▼';
        const clr      = isGood ? '#10b981' : '#ef4444';
        const absAmt   = Math.abs(deltaAmount);
        const deltaStr = deltaFormat ? formatValue(absAmt, deltaFormat) : String(absAmt);
        const sign     = isPos ? '+' : '-';
        deltaHtml = `<div class="card-delta" style="color:${clr}">` +
            `<span class="card-delta-arrow">${arrow}</span>` +
            `<span class="card-delta-value">${escHtml(sign + deltaStr)}</span>` +
            (deltaLabel ? `<span class="card-delta-label">${escHtml(deltaLabel)}</span>` : '') +
            `</div>`;
    }

    // Goal display line
    let goalLineHtml = '';
    if (goalValue !== null && isOn(getOption(opts, 'show_goal'))) {
        const gDisplay = doAbbreviate
            ? abbreviateNumber(goalValue, formatOpt)
            : (formatOpt ? formatValue(goalValue, formatOpt) : String(goalValue));
        goalLineHtml = `<div class="card-goal">Target: ${escHtml(gDisplay)}</div>`;
    }

    // % of goal line
    let goalPctHtml = '';
    if (ratio !== null && isOn(getOption(opts, 'show_percent_of_goal'))) {
        goalPctHtml = `<div class="card-goal-pct">${Math.round(ratio * 100)}% of target</div>`;
    }

    // Progress bar or ring
    let progressHtml = '';
    const showProgress  = isOn(getOption(opts, 'show_progress'));
    const progressStyle = (getOption(opts, 'progress_style') || 'BAR').toUpperCase();
    if (showProgress && ratio !== null && status) {
        const pct      = Math.min(ratio * 100, 100);
        const barColor = colors[status];
        if (progressStyle === 'RING') {
            const r = 18, circ = 2 * Math.PI * r;
            const dash = (pct / 100) * circ;
            progressHtml = `<div class="card-progress-ring">` +
                `<svg width="48" height="48" viewBox="0 0 48 48">` +
                `<circle cx="24" cy="24" r="${r}" fill="none" stroke="#e5e7eb" stroke-width="4"/>` +
                `<circle cx="24" cy="24" r="${r}" fill="none" stroke="${escHtml(barColor)}" stroke-width="4"` +
                ` stroke-dasharray="${dash.toFixed(2)} ${circ.toFixed(2)}" transform="rotate(-90 24 24)"/>` +
                `</svg><span class="card-ring-pct">${Math.round(pct)}%</span></div>`;
        } else {
            progressHtml = `<div class="card-progress">` +
                `<div class="card-progress-fill" style="width:${pct.toFixed(1)}%;background:${escHtml(barColor)}"></div>` +
                `</div>`;
        }
    }

    const vstyles = visual.styles || {};
    const tColor = getStyle(vstyles, 'TITLE_COLOR');
    const tSize = getStyle(vstyles, 'TITLE_SIZE');
    const tWeight = getStyle(vstyles, 'TITLE_WEIGHT');
    const tFont = getStyle(vstyles, 'TITLE_FONT');
    const tAlign = getStyle(vstyles, 'TITLE_ALIGN');

    let titleStyleAttr = '';
    if (tColor) titleStyleAttr += `color:${escHtml(tColor)};`;
    if (tSize) titleStyleAttr += `font-size:${escHtml(tSize.includes('px') || tSize.includes('rem') || tSize.includes('em') || tSize.includes('%') ? tSize : (tSize + 'px'))};`;
    if (tWeight) titleStyleAttr += `font-weight:${escHtml(tWeight)};`;
    if (tFont) titleStyleAttr += `font-family:${escHtml(tFont)};`;

    let headerRowStyleAttr = '';
    if (tAlign) {
        const alignLower = tAlign.toLowerCase();
        headerRowStyleAttr = `justify-content:${alignLower === 'center' ? 'center' : (alignLower === 'right' ? 'flex-end' : 'flex-start')};`;
    }

    const sColor = getStyle(vstyles, 'SUBTITLE_COLOR');
    const sSize = getStyle(vstyles, 'SUBTITLE_SIZE');
    const sWeight = getStyle(vstyles, 'SUBTITLE_WEIGHT');
    const sFont = getStyle(vstyles, 'SUBTITLE_FONT');
    const sAlign = getStyle(vstyles, 'SUBTITLE_ALIGN') || tAlign;

    let subStyleAttr = '';
    if (sColor) subStyleAttr += `color:${escHtml(sColor)};`;
    if (sSize) subStyleAttr += `font-size:${escHtml(sSize.includes('px') || sSize.includes('rem') || sSize.includes('em') || sSize.includes('%') ? sSize : (sSize + 'px'))};`;
    if (sWeight) subStyleAttr += `font-weight:${escHtml(sWeight)};`;
    if (sFont) subStyleAttr += `font-family:${escHtml(sFont)};`;
    if (sAlign) subStyleAttr += `text-align:${escHtml(sAlign.toLowerCase())};`;

    const titleInner = visual.titleIsMarkdown ? renderInlineMarkdown(cardTitle) : escHtml(cardTitle);
    const subInner = visual.subtitleIsMarkdown ? renderInlineMarkdown(subtitleText) : escHtml(subtitleText);

    // ── Value Color ──────────────────────────────────────────────────
    let valueColor = null;
    if (visual.rowFontStyles && visual.rowFontStyles.length > 0 && visual.rowFontStyles[0]) {
        valueColor = visual.rowFontStyles[0];
    } else if (visual.rowStyles && visual.rowStyles.length > 0 && visual.rowStyles[0]) {
        valueColor = visual.rowStyles[0];
    } else if (visual.formattingRules && visual.formattingRules.length > 0 && rawValue !== null && !isNaN(rawValue)) {
        for (let i = 0; i < visual.formattingRules.length; i++) {
            const rule = visual.formattingRules[i];
            const cond = (rule.condition || rule.Condition || '').trim();
            if (!cond) continue;
            if (matchesCondition(cond, rawValue, 'VALUE')) {
                valueColor = rule.fontColor || rule.FontColor || rule.color || rule.Color;
                break;
            }
        }
    }
    if (!valueColor) {
        valueColor = getOption(opts, 'value_color') || getStyle(vstyles, 'VALUE_COLOR') || null;
    }

    const cardEl = document.createElement('div');
    cardEl.className = 'card-value' + (status ? ` card-status-${status}` : '');
    cardEl.innerHTML =
        `<div class="card-header-row"${headerRowStyleAttr ? ` style="${headerRowStyleAttr}"` : ''}><div class="card-label"${titleStyleAttr ? ` style="${titleStyleAttr}"` : ''}>${titleInner}</div>${badgeHtml}</div>` +
        (subtitleText ? `<div class="card-subtitle"${subStyleAttr ? ` style="${subStyleAttr}"` : ''}>${subInner}</div>` : '') +
        `<div class="card-number"${valueColor ? ` style="color:${escHtml(valueColor)};"` : ''}>${escHtml(String(displayValue))}</div>` +
        goalLineHtml + goalPctHtml + deltaHtml + progressHtml;
    const sparkline = Array.isArray(visual.microCharts)
        ? visual.microCharts.find(micro => micro.role === 'card.sparkline')
        : null;
    if (sparkline && sparkline.svg) {
        const micro = document.createElement('div');
        micro.className = 'card-sparkline';
        micro.setAttribute('role', 'img');
        micro.setAttribute('aria-label', sparkline.accessibleLabel || sparkline.plainText || 'Trend');
        micro.innerHTML = sparkline.svg;
        cardEl.appendChild(micro);
    }
    container.appendChild(cardEl);
}

// ── Text ────────────────────────────────────────────────────────────────

function renderText(container, visual) {
    // Static content from CONTENT/DEFAULT clause; fall back to MAPPINGS(CONTENT=col) first row
    let content = visual.defaultValue || '';
    if (!content && visual.columns && visual.rows && visual.rows.length > 0) {
        const idx = visual.columns.findIndex(c => c.toLowerCase() === 'content');
        if (idx >= 0 && visual.rows[0][idx] != null) content = String(visual.rows[0][idx]);
    }

    // Inline column interpolation: {column FORMAT '...'}
    if (content && visual.columns && visual.rows && visual.rows.length > 0) {
        const row = visual.rows[0];
        content = content.replace(/\{([A-Za-z0-9_]+)(?:\s+FORMAT\s+['"]([^'"]+)['"])?\}/gi, (match, colName, fmt) => {
            const colIdx = visual.columns.findIndex(c => c.toLowerCase() === colName.toLowerCase());
            if (colIdx >= 0 && row[colIdx] != null) {
                const rawVal = row[colIdx];
                if (fmt) {
                    return formatValue(rawVal, fmt);
                }
                return String(rawVal);
            }
            return match;
        });
    }

    const opts = visual.options || {};
    const align = (opts['ALIGN'] || opts['align'] || 'left').toLowerCase();
    const useMd = (opts['MARKDOWN'] || opts['markdown'] || 'ON').toUpperCase() !== 'OFF';
    const maxLines = parseInt(opts['MAX_LINES'] || opts['max_lines'] || '0', 10);
    const overflow = (opts['OVERFLOW'] || opts['overflow'] || '').toUpperCase();
    const fontSize = opts['FONT_SIZE'] || opts['font_size'];
    const fontColor = opts['FONT_COLOR'] || opts['font_color'];
    const fontWeight = opts['FONT_WEIGHT'] || opts['font_weight'];

    const div = document.createElement('div');
    div.className = 'text-visual';
    div.style.textAlign = align;

    if (maxLines > 0) {
        div.style.display = '-webkit-box';
        div.style.webkitLineClamp = String(maxLines);
        div.style.webkitBoxOrient = 'vertical';
        div.style.overflow = 'hidden';
    }

    if (overflow === 'CLIP') div.classList.add('overflow-clip');
    else if (overflow === 'SCROLL') div.classList.add('overflow-scroll');
    else if (overflow === 'ELLIPSIS') div.classList.add('overflow-ellipsis');

    if (fontSize) div.style.fontSize = (fontSize.includes('px') || fontSize.includes('rem') || fontSize.includes('em') || fontSize.includes('pt')) ? fontSize : (fontSize + 'px');
    if (fontColor) div.style.color = fontColor;
    if (fontWeight) div.style.fontWeight = fontWeight;

    div.innerHTML = useMd ? simpleMarkdown(content) : escHtml(content).replace(/\n/g, '<br>');

    const clickActions = actionsFor(visual, 'ON_CLICK');
    if (clickActions.length > 0) {
        div.style.cursor = 'pointer';
        div.addEventListener('click', () => {
            const row = visual.rows && visual.rows.length > 0 ? visual.rows[0] : [];
            clickActions.forEach(a => executeAction(a, row, visual.columns || [], visual.name, visual));
        });
    }

    container.appendChild(div);
}

// ── Image ───────────────────────────────────────────────────────────────

function renderImage(container, visual) {
    const opts = visual.options || {};
    const src  = opts['SRC'] || opts['src'] || '';
    const alt  = opts['ALT'] || opts['alt'] || '';
    const fit  = (opts['FIT'] || opts['fit'] || 'contain').toLowerCase();
    const mode = (opts['MODE'] || opts['mode'] || 'SINGLE').toUpperCase();
    const cols = parseInt(opts['COLUMNS'] || opts['columns'] || '3', 10);
    const aspect = opts['ASPECT_RATIO'] || opts['aspect_ratio'];
    const fallback = opts['FALLBACK'] || opts['fallback'];

    const clickActions = actionsFor(visual, 'ON_CLICK');

    if (mode === 'GALLERY' && visual.rows && visual.rows.length > 0) {
        const gallery = document.createElement('div');
        gallery.className = 'image-gallery';
        gallery.style.gridTemplateColumns = `repeat(${cols > 0 ? cols : 3}, 1fr)`;
        gallery.style.gap = '8px';

        const srcIdx = visual.columns ? visual.columns.findIndex(c => c.toLowerCase() === 'src' || c.toLowerCase() === 'url' || c.toLowerCase() === 'image') : 0;
        const useIdx = srcIdx >= 0 ? srcIdx : 0;

        visual.rows.forEach(row => {
            const rawUrl = String(row[useIdx] ?? '');
            const img = document.createElement('img');
            img.src = safeUrl(rawUrl || fallback || '');
            img.alt = alt;
            img.style.objectFit = fit;
            if (aspect) img.style.aspectRatio = aspect.replace(':', '/');
            if (fallback) {
                img.onerror = () => { img.src = safeUrl(fallback); img.onerror = null; };
            }
            if (clickActions.length > 0) {
                img.style.cursor = 'pointer';
                img.addEventListener('click', () => {
                    clickActions.forEach(a => executeAction(a, row, visual.columns, visual.name, visual));
                });
            }
            gallery.appendChild(img);
        });
        container.appendChild(gallery);
        return;
    }

    const wrapper = document.createElement('div');
    wrapper.style.width  = '100%';
    wrapper.style.height = '100%';
    wrapper.style.display = 'flex';
    wrapper.style.alignItems = 'center';
    wrapper.style.justifyContent = 'center';

    const finalSrc = src || (visual.rows && visual.rows.length > 0 && visual.rows[0][0] != null ? String(visual.rows[0][0]) : '') || fallback || '';
    const img = document.createElement('img');
    img.src   = safeUrl(finalSrc);
    img.alt   = alt;
    img.style.maxWidth  = '100%';
    img.style.maxHeight = '100%';
    img.style.objectFit = fit;
    if (aspect) img.style.aspectRatio = aspect.replace(':', '/');
    if (fallback) {
        img.onerror = () => { img.src = safeUrl(fallback); img.onerror = null; };
    }

    if (clickActions.length > 0) {
        wrapper.style.cursor = 'pointer';
        wrapper.addEventListener('click', () => {
            const row = visual.rows && visual.rows.length > 0 ? visual.rows[0] : [];
            clickActions.forEach(a => executeAction(a, row, visual.columns || [], visual.name, visual));
        });
    }

    wrapper.appendChild(img);
    container.appendChild(wrapper);
}
