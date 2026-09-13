/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-controls-input.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Slicer, slider, search, checkbox, textbox, numberbox, and button controls.
 */
import { actionsFor, evaluateExpressionAgainstParameters, executeAction } from './rt-actions.js';
import { _uiStates, isWebMode, parameters, vscode } from './rt-state.js';
import { escHtml, formatValue, getOption, getParam, getStyle, isOn, parseMultiParameter, safeUrl } from './rt-util.js';
import { postParameters } from './rt-transport.js';
import { renderManifest } from './report-runtime.js';
import { applyDesignTokens } from './rt-theme.js';
export function applyControlState(input, visual, wrapper) {
    const opts = visual?.options || {};
    const disabledExpr = opts['DISABLED'] || opts['disabled'];
    const readOnlyExpr = opts['READ_ONLY'] || opts['read_only'] || opts['READONLY'] || opts['readonly'];
    const isDisabled = disabledExpr != null && evaluateExpressionAgainstParameters(disabledExpr, parameters);
    const isReadOnly = readOnlyExpr != null && evaluateExpressionAgainstParameters(readOnlyExpr, parameters);
    if (input) {
        if (isDisabled) {
            input.disabled = true;
            input.setAttribute('aria-disabled', 'true');
        }
        if (isReadOnly) {
            input.readOnly = true;
            input.setAttribute('aria-readonly', 'true');
        }
    }
    if (wrapper) {
        if (isDisabled)
            wrapper.classList.add('is-disabled');
        if (isReadOnly)
            wrapper.classList.add('is-readonly');
    }
}
// ── Slicer ──────────────────────────────────────────────────────────────
export function setParameterAccessibleName(control, visual, parameterName, qualifier) {
    const visualName = String((visual && visual.name) || '').trim();
    const normalizedParameter = String(parameterName || '').replace(/^@/, '').trim();
    const baseName = visualName || normalizedParameter || 'Report parameter';
    control.setAttribute('aria-label', qualifier ? `${baseName} ${qualifier}` : baseName);
}
export function renderSlicer(container, visual, manifest) {
    const wrapper = document.createElement('div');
    wrapper.className = 'slicer-wrapper';
    const opts = visual.options || {};
    const vstyles = visual.styles || {};
    const action = visual.actions.find((a) => a.type === 'SET_PARAMETER');
    const paramName = action ? action.parameterName : null;
    const typeStr = visual.visualType.toLowerCase();
    const modeOpt = (getOption(opts, 'mode') || '').toUpperCase();
    const isMulti = typeStr === 'multiselect' || modeOpt === 'MULTI' || isOn(opts['multiple'] || opts['MULTIPLE']);
    const changeActions = actionsFor(visual, 'ON_CHANGE').filter((a) => a.type === 'SET_PARAMETER');
    const isInteractive = (isWebMode || vscode) && changeActions.length > 0;
    const valCol = (getOption(opts, 'mapping:value') || visual.columns[0] || 'value').toLowerCase();
    const lblCol = (getOption(opts, 'mapping:label') || (visual.columns.length > 1 ? visual.columns[1] : visual.columns[0]) || 'label').toLowerCase();
    const imgCol = (getOption(opts, 'mapping:image') || getOption(opts, 'image') || '').toLowerCase();
    const valIdx = visual.columns.findIndex(c => c.toLowerCase() === valCol);
    const lblIdx = visual.columns.findIndex(c => c.toLowerCase() === lblCol);
    const imgIdx = imgCol ? visual.columns.findIndex(c => c.toLowerCase() === imgCol) : -1;
    const finalValIdx = valIdx >= 0 ? valIdx : 0;
    const finalLblIdx = lblIdx >= 0 ? lblIdx : (visual.columns.length > 1 ? 1 : 0);
    // Extract options
    const rawItems = (visual.rows || []).map((row, idx) => {
        const val = String(row[finalValIdx] ?? '');
        const lbl = String(row[finalLblIdx] ?? val);
        const img = imgIdx >= 0 && row[imgIdx] != null ? String(row[imgIdx]) : null;
        return { value: val, label: lbl, image: img, origIndex: idx };
    });
    // Deduplicate by value
    const seen = new Set();
    const items = [];
    rawItems.forEach((it) => {
        if (!seen.has(it.value)) {
            seen.add(it.value);
            items.push(it);
        }
    });
    // Sorting
    const sortOpt = (getOption(opts, 'sort') || 'SOURCE').toUpperCase();
    if (sortOpt === 'ALPHA' || sortOpt === 'LABEL') {
        items.sort((a, b) => a.label.localeCompare(b.label, undefined, { numeric: true }));
    }
    else if (sortOpt === 'VALUE') {
        items.sort((a, b) => {
            const an = parseFloat(a.value), bn = parseFloat(b.value);
            return (!isNaN(an) && !isNaN(bn)) ? an - bn : a.value.localeCompare(b.value);
        });
    }
    // Option limit
    const maxOptions = parseInt(getOption(opts, 'max_options') || '0', 10);
    const totalOptions = items.length;
    const hasOverflow = maxOptions > 0 && totalOptions > maxOptions;
    const displayedItems = hasOverflow ? items.slice(0, maxOptions) : items;
    // Current parameter / default value
    let currentVal = undefined;
    if (paramName && manifest && manifest.parameters) {
        currentVal = getParam(manifest.parameters, paramName);
    }
    if (currentVal === undefined || currentVal === null || currentVal === '') {
        currentVal = visual.defaultValue || getOption(opts, 'default') || '';
    }
    const param = paramName;
    let selected = isMulti
        ? new Set(parseMultiParameter(currentVal))
        : (currentVal !== undefined && currentVal !== '' ? String(currentVal) : (displayedItems[0]?.value ?? ''));
    // Layout
    let layout = (getStyle(vstyles, 'LAYOUT') || getOption(opts, 'layout') || '').toUpperCase();
    if (!layout) {
        layout = typeStr === 'multiselect' ? 'LIST' : 'DROPDOWN';
    }
    // Searchable
    const isSearchable = isOn(getOption(opts, 'searchable'));
    // Select All control
    const showSelectAll = isMulti && (isOn(getOption(opts, 'show_select_all')) || isOn(getOption(opts, 'legend')));
    const selectAllLabel = getOption(opts, 'select_all_label') || 'Select All';
    const clearAllLabel = getOption(opts, 'clear_all_label') || 'Clear All';
    // Image styling options
    const imgSize = getOption(opts, 'image_size') || '24px';
    const imgPos = (getOption(opts, 'image_position') || 'LEFT').toUpperCase();
    const imgFit = getOption(opts, 'image_fit') || 'cover';
    function postBatch(val) {
        if (!isInteractive)
            return;
        const batch = {};
        changeActions.forEach((a) => {
            batch[a.parameterName] = val;
        });
        postParameters(batch).then(m => { if (m)
            renderManifest(m); });
    }
    function createOptionImage(src) {
        if (!src)
            return null;
        const img = document.createElement('img');
        img.src = src;
        img.className = 'slicer-option-image' + (imgPos === 'TOP' ? ' pos-top' : '');
        img.style.width = imgSize;
        img.style.height = imgSize;
        img.style.objectFit = imgFit;
        return img;
    }
    function createOverflowIndicator() {
        if (!hasOverflow)
            return null;
        const div = document.createElement('div');
        div.className = isMulti ? 'multiselect-overflow' : 'slicer-overflow';
        div.textContent = `Showing first ${maxOptions} of ${totalOptions} options`;
        return div;
    }
    // Render based on layout
    if (layout === 'TILE' || layout === 'BUTTON_BAR' || layout === 'CHIPS') {
        if (isSearchable) {
            const searchIn = document.createElement('input');
            searchIn.type = 'search';
            searchIn.className = isMulti ? 'multiselect-search' : 'slicer-search';
            searchIn.placeholder = 'Type to filter…';
            wrapper.appendChild(searchIn);
        }
        if (showSelectAll) {
            const headerActions = document.createElement('div');
            headerActions.className = 'multiselect-header-actions';
            const selAllBtn = document.createElement('button');
            selAllBtn.type = 'button';
            selAllBtn.className = 'multiselect-link';
            selAllBtn.textContent = selectAllLabel;
            const clrAllBtn = document.createElement('button');
            clrAllBtn.type = 'button';
            clrAllBtn.className = 'multiselect-link';
            clrAllBtn.textContent = clearAllLabel;
            headerActions.appendChild(selAllBtn);
            headerActions.appendChild(clrAllBtn);
            wrapper.appendChild(headerActions);
            selAllBtn.addEventListener('click', () => {
                displayedItems.forEach(it => selected.add(it.value));
                tileContainer.querySelectorAll('.slicer-tile, .multiselect-chip').forEach(t => t.classList.add('active'));
                postBatch(JSON.stringify(Array.from(selected)));
            });
            clrAllBtn.addEventListener('click', () => {
                selected.clear();
                tileContainer.querySelectorAll('.slicer-tile, .multiselect-chip').forEach(t => t.classList.remove('active'));
                postBatch(JSON.stringify([]));
            });
        }
        const tileContainer = document.createElement('div');
        tileContainer.className = layout === 'BUTTON_BAR' ? 'slicer-button-bar' : (layout === 'CHIPS' ? 'multiselect-chips' : 'slicer-tile-container');
        if (paramName)
            tileContainer.setAttribute('data-parameter', paramName);
        const tileEntries = [];
        displayedItems.forEach((item) => {
            const tile = document.createElement('button');
            tile.type = 'button';
            tile.className = (layout === 'CHIPS' ? 'multiselect-chip' : 'slicer-tile') + (imgPos === 'TOP' ? ' pos-top' : '');
            const isSelected = isMulti ? selected.has(item.value) : (selected === item.value);
            if (isSelected)
                tile.classList.add('active');
            setParameterAccessibleName(tile, visual, paramName, item.label);
            const imgEl = createOptionImage(item.image);
            const labelSpan = document.createElement('span');
            labelSpan.textContent = item.label;
            if (imgEl && imgPos === 'RIGHT') {
                tile.appendChild(labelSpan);
                tile.appendChild(imgEl);
            }
            else {
                if (imgEl)
                    tile.appendChild(imgEl);
                tile.appendChild(labelSpan);
            }
            tile.addEventListener('click', () => {
                if (isMulti) {
                    if (selected.has(item.value)) {
                        selected.delete(item.value);
                        tile.classList.remove('active');
                    }
                    else {
                        selected.add(item.value);
                        tile.classList.add('active');
                    }
                    postBatch(JSON.stringify(Array.from(selected)));
                }
                else {
                    tileContainer.querySelectorAll('.slicer-tile, .multiselect-chip').forEach(t => t.classList.remove('active'));
                    tile.classList.add('active');
                    selected = item.value;
                    postBatch(item.value);
                }
            });
            tileContainer.appendChild(tile);
            tileEntries.push({ el: tile, item });
        });
        wrapper.appendChild(tileContainer);
        if (isSearchable) {
            const searchIn = /** @type {HTMLInputElement | null} */ (wrapper.querySelector('.slicer-search, .multiselect-search'));
            searchIn?.addEventListener('input', () => {
                const q = searchIn.value.toLowerCase().trim();
                tileEntries.forEach(({ el, item }) => {
                    const m = !q || item.label.toLowerCase().includes(q) || item.value.toLowerCase().includes(q);
                    el.style.display = m ? '' : 'none';
                });
            });
        }
        const overflowEl = createOverflowIndicator();
        if (overflowEl)
            wrapper.appendChild(overflowEl);
    }
    else if (layout === 'LIST') {
        if (isSearchable) {
            const searchIn = document.createElement('input');
            searchIn.type = 'search';
            searchIn.className = isMulti ? 'multiselect-search' : 'slicer-search';
            searchIn.placeholder = 'Type to filter…';
            wrapper.appendChild(searchIn);
        }
        if (showSelectAll) {
            const headerActions = document.createElement('div');
            headerActions.className = 'multiselect-header-actions';
            const selAllBtn = document.createElement('button');
            selAllBtn.type = 'button';
            selAllBtn.className = 'multiselect-link';
            selAllBtn.textContent = selectAllLabel;
            const clrAllBtn = document.createElement('button');
            clrAllBtn.type = 'button';
            clrAllBtn.className = 'multiselect-link';
            clrAllBtn.textContent = clearAllLabel;
            headerActions.appendChild(selAllBtn);
            headerActions.appendChild(clrAllBtn);
            wrapper.appendChild(headerActions);
            selAllBtn.addEventListener('click', () => {
                displayedItems.forEach(it => selected.add(it.value));
                /** @type {NodeListOf<HTMLInputElement>} */ (list.querySelectorAll('input[type="checkbox"]')).forEach(cb => { cb.checked = true; });
                postBatch(JSON.stringify(Array.from(selected)));
            });
            clrAllBtn.addEventListener('click', () => {
                selected.clear();
                /** @type {NodeListOf<HTMLInputElement>} */ (list.querySelectorAll('input[type="checkbox"]')).forEach(cb => { cb.checked = false; });
                postBatch(JSON.stringify([]));
            });
        }
        const list = document.createElement('div');
        list.className = 'multiselect-list';
        if (paramName)
            list.setAttribute('data-parameter', paramName);
        const listEntries = [];
        displayedItems.forEach((item) => {
            const itemEl = document.createElement('label');
            itemEl.className = 'multiselect-item' + (imgPos === 'TOP' ? ' pos-top' : '');
            const input = document.createElement('input');
            input.type = isMulti ? 'checkbox' : 'radio';
            if (!isMulti && paramName)
                input.name = paramName;
            input.value = item.value;
            input.checked = isMulti ? selected.has(item.value) : (selected === item.value);
            setParameterAccessibleName(input, visual, paramName, item.label);
            input.addEventListener('change', () => {
                if (isMulti) {
                    if (input.checked)
                        selected.add(item.value);
                    else
                        selected.delete(item.value);
                    postBatch(JSON.stringify(Array.from(selected)));
                }
                else {
                    selected = item.value;
                    postBatch(item.value);
                }
            });
            const imgEl = createOptionImage(item.image);
            const span = document.createElement('span');
            span.textContent = item.label;
            itemEl.appendChild(input);
            if (imgEl && imgPos === 'RIGHT') {
                itemEl.appendChild(span);
                itemEl.appendChild(imgEl);
            }
            else {
                if (imgEl)
                    itemEl.appendChild(imgEl);
                itemEl.appendChild(span);
            }
            list.appendChild(itemEl);
            listEntries.push({ el: itemEl, item });
        });
        wrapper.appendChild(list);
        if (isSearchable) {
            const searchIn = /** @type {HTMLInputElement | null} */ (wrapper.querySelector('.slicer-search, .multiselect-search'));
            searchIn?.addEventListener('input', () => {
                const q = searchIn.value.toLowerCase().trim();
                listEntries.forEach(({ el, item }) => {
                    const m = !q || item.label.toLowerCase().includes(q) || item.value.toLowerCase().includes(q);
                    el.style.display = m ? '' : 'none';
                });
            });
        }
        const overflowEl = createOverflowIndicator();
        if (overflowEl)
            wrapper.appendChild(overflowEl);
    }
    else {
        // DROPDOWN layout
        if (isMulti) {
            const dropWrapper = document.createElement('div');
            dropWrapper.className = 'multiselect-dropdown';
            const toggle = document.createElement('button');
            toggle.type = 'button';
            toggle.className = 'multiselect-toggle';
            const updateToggleText = () => {
                if (selected.size === 0)
                    toggle.innerHTML = '<span>All</span>';
                else if (selected.size === 1)
                    toggle.innerHTML = `<span>${escHtml(Array.from(selected)[0])}</span>`;
                else
                    toggle.innerHTML = `<span>${selected.size} selected</span>`;
                toggle.innerHTML += '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><polyline points="6 9 12 15 18 9"></polyline></svg>';
            };
            updateToggleText();
            const popup = document.createElement('div');
            popup.className = 'multiselect-popup';
            if (paramName)
                popup.setAttribute('data-parameter', paramName);
            if (isSearchable) {
                const searchIn = document.createElement('input');
                searchIn.type = 'search';
                searchIn.className = 'multiselect-search';
                searchIn.placeholder = 'Type to filter…';
                searchIn.addEventListener('click', e => e.stopPropagation());
                popup.appendChild(searchIn);
            }
            if (showSelectAll) {
                const headerActions = document.createElement('div');
                headerActions.className = 'multiselect-header-actions';
                const selAllBtn = document.createElement('button');
                selAllBtn.type = 'button';
                selAllBtn.className = 'multiselect-link';
                selAllBtn.textContent = selectAllLabel;
                const clrAllBtn = document.createElement('button');
                clrAllBtn.type = 'button';
                clrAllBtn.className = 'multiselect-link';
                clrAllBtn.textContent = clearAllLabel;
                headerActions.appendChild(selAllBtn);
                headerActions.appendChild(clrAllBtn);
                popup.appendChild(headerActions);
                selAllBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    displayedItems.forEach(it => selected.add(it.value));
                    /** @type {NodeListOf<HTMLInputElement>} */ (popup.querySelectorAll('input[type="checkbox"]')).forEach(cb => { cb.checked = true; });
                    updateToggleText();
                    postBatch(JSON.stringify(Array.from(selected)));
                });
                clrAllBtn.addEventListener('click', (e) => {
                    e.stopPropagation();
                    selected.clear();
                    /** @type {NodeListOf<HTMLInputElement>} */ (popup.querySelectorAll('input[type="checkbox"]')).forEach(cb => { cb.checked = false; });
                    updateToggleText();
                    postBatch(JSON.stringify([]));
                });
            }
            const popupEntries = [];
            displayedItems.forEach((item) => {
                const itemEl = document.createElement('label');
                itemEl.className = 'multiselect-item';
                const cb = document.createElement('input');
                cb.type = 'checkbox';
                cb.value = item.value;
                cb.checked = selected.has(item.value);
                const val = item.value;
                setParameterAccessibleName(cb, visual, param, val);
                cb.addEventListener('change', () => {
                    if (cb.checked)
                        selected.add(item.value);
                    else
                        selected.delete(item.value);
                    updateToggleText();
                    postBatch(JSON.stringify(Array.from(selected)));
                });
                const imgEl = createOptionImage(item.image);
                const span = document.createElement('span');
                span.textContent = item.label;
                itemEl.appendChild(cb);
                if (imgEl && imgPos === 'RIGHT') {
                    itemEl.appendChild(span);
                    itemEl.appendChild(imgEl);
                }
                else {
                    if (imgEl)
                        itemEl.appendChild(imgEl);
                    itemEl.appendChild(span);
                }
                popup.appendChild(itemEl);
                popupEntries.push({ el: itemEl, item });
            });
            if (isSearchable) {
                const searchIn = /** @type {HTMLInputElement | null} */ (popup.querySelector('.multiselect-search'));
                searchIn?.addEventListener('input', () => {
                    const q = searchIn.value.toLowerCase().trim();
                    popupEntries.forEach(({ el, item }) => {
                        const m = !q || item.label.toLowerCase().includes(q) || item.value.toLowerCase().includes(q);
                        el.style.display = m ? '' : 'none';
                    });
                });
            }
            const overflowEl = createOverflowIndicator();
            if (overflowEl)
                popup.appendChild(overflowEl);
            toggle.addEventListener('click', (e) => {
                e.stopPropagation();
                const isOpen = popup.classList.contains('open');
                document.querySelectorAll('.multiselect-popup.open').forEach(p => p.classList.remove('open'));
                if (!isOpen)
                    popup.classList.add('open');
            });
            popup.addEventListener('click', e => e.stopPropagation());
            document.addEventListener('click', () => { popup.classList.remove('open'); });
            dropWrapper.appendChild(toggle);
            dropWrapper.appendChild(popup);
            wrapper.appendChild(dropWrapper);
        }
        else {
            // Single-select dropdown
            if (isSearchable) {
                const searchIn = document.createElement('input');
                searchIn.type = 'search';
                searchIn.className = 'slicer-search';
                searchIn.placeholder = 'Type to filter…';
                wrapper.appendChild(searchIn);
            }
            const select = document.createElement('select');
            setParameterAccessibleName(select, visual, paramName);
            if (paramName)
                select.setAttribute('data-parameter', paramName);
            displayedItems.forEach(item => {
                const opt = document.createElement('option');
                opt.value = item.value;
                opt.textContent = item.label;
                select.appendChild(opt);
            });
            if (selected !== undefined && selected !== '') {
                select.value = selected;
            }
            if (isInteractive) {
                select.addEventListener('change', () => {
                    postBatch(select.value);
                });
                wrapper.appendChild(select);
            }
            else {
                const note = document.createElement('p');
                note.className = 'slicer-note';
                note.textContent = '[Slicer — interactive in ReportPlayer only]';
                wrapper.appendChild(note);
            }
            if (isSearchable) {
                const searchIn = /** @type {HTMLInputElement | null} */ (wrapper.querySelector('.slicer-search'));
                searchIn?.addEventListener('input', () => {
                    const q = searchIn.value.toLowerCase().trim();
                    Array.from(select.options).forEach(opt => {
                        const m = !q || opt.text.toLowerCase().includes(q) || opt.value.toLowerCase().includes(q);
                        opt.hidden = !m;
                    });
                });
            }
            const overflowEl = createOverflowIndicator();
            if (overflowEl)
                wrapper.appendChild(overflowEl);
        }
    }
    container.appendChild(wrapper);
}
// ── Slider ──────────────────────────────────────────────────────────────
export function renderSlider(container, visual, manifest) {
    const opts = visual.options || {};
    const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
    const startAction = changeActions.length > 0 ? changeActions[0] : null;
    const param = startAction ? startAction.parameterName : null;
    const secondaryParam = (startAction && startAction.secondaryParameterName) || (changeActions.length > 1 ? changeActions[1].parameterName : null);
    const mode = (getOption(opts, 'mode') || 'SINGLE').toUpperCase();
    const isRange = mode === 'RANGE';
    const min = parseFloat(opts['MIN'] || opts['min'] || '0');
    const max = parseFloat(opts['MAX'] || opts['max'] || '100');
    const step = parseFloat(opts['STEP'] || opts['step'] || '1');
    const formatOpt = getOption(opts, 'format');
    const fireOn = (getOption(opts, 'fire_on') || 'RELEASE').toUpperCase();
    const showTicks = isOn(getOption(opts, 'show_ticks'));
    const showTickLabels = isOn(getOption(opts, 'tick_labels'));
    let dataTicks = null;
    if (opts['DATA_TICKS']) {
        try {
            dataTicks = typeof opts['DATA_TICKS'] === 'string' ? JSON.parse(opts['DATA_TICKS']) : opts['DATA_TICKS'];
        }
        catch {
            // DATA_TICKS is author-supplied; unparseable leaves the computed ticks.
        }
    }
    function snapValue(val) {
        if (!Array.isArray(dataTicks) || dataTicks.length === 0)
            return val;
        let closest = dataTicks[0];
        let minDiff = Math.abs(val - closest);
        for (let i = 1; i < dataTicks.length; i++) {
            const diff = Math.abs(val - dataTicks[i]);
            if (diff < minDiff) {
                minDiff = diff;
                closest = dataTicks[i];
            }
        }
        return closest;
    }
    function formatDisplay(val) {
        return formatOpt ? formatValue(val, formatOpt) : String(val);
    }
    const wrapper = document.createElement('div');
    wrapper.className = 'filter-wrapper' + (isRange ? ' slider-range-wrapper' : '');
    const datalistId = 'ticks-' + (visual.name || Math.random().toString(36).slice(2));
    if (showTicks) {
        const dl = document.createElement('datalist');
        dl.id = datalistId;
        if (Array.isArray(dataTicks) && dataTicks.length > 0) {
            dataTicks.forEach((t) => {
                const opt = document.createElement('option');
                opt.value = t;
                if (showTickLabels)
                    opt.label = formatDisplay(t);
                dl.appendChild(opt);
            });
        }
        else {
            for (let v = min; v <= max; v += step) {
                const opt = document.createElement('option');
                opt.value = String(v);
                if (showTickLabels)
                    opt.label = formatDisplay(v);
                dl.appendChild(opt);
            }
        }
        wrapper.appendChild(dl);
    }
    if (isRange) {
        let lowVal = min;
        let highVal = max;
        if (manifest && manifest.parameters) {
            if (param) {
                const v = parseFloat(getParam(manifest.parameters, param));
                if (!isNaN(v))
                    lowVal = v;
            }
            if (secondaryParam) {
                const v = parseFloat(getParam(manifest.parameters, secondaryParam));
                if (!isNaN(v))
                    highVal = v;
            }
        }
        else {
            const def = visual.defaultValue || opts['DEFAULT'] || opts['default'] || '';
            const parts = parseMultiParameter(def);
            if (parts.length > 0 && !isNaN(parseFloat(parts[0])))
                lowVal = parseFloat(parts[0]);
            if (parts.length > 1 && !isNaN(parseFloat(parts[1])))
                highVal = parseFloat(parts[1]);
        }
        lowVal = snapValue(lowVal);
        highVal = snapValue(highVal);
        const rangeInputs = document.createElement('div');
        rangeInputs.className = 'slider-range-inputs';
        const lowInput = document.createElement('input');
        lowInput.type = 'range';
        setParameterAccessibleName(lowInput, visual, param, 'minimum');
        lowInput.min = String(min);
        lowInput.max = String(max);
        lowInput.step = String(step);
        lowInput.value = String(lowVal);
        if (showTicks)
            lowInput.setAttribute('list', datalistId);
        if (param)
            lowInput.setAttribute('data-parameter', param);
        const highInput = document.createElement('input');
        highInput.type = 'range';
        setParameterAccessibleName(highInput, visual, secondaryParam, 'maximum');
        highInput.min = String(min);
        highInput.max = String(max);
        highInput.step = String(step);
        highInput.value = String(highVal);
        if (showTicks)
            highInput.setAttribute('list', datalistId);
        if (secondaryParam)
            highInput.setAttribute('data-parameter', secondaryParam);
        const valueLabel = document.createElement('span');
        valueLabel.className = 'range-value';
        valueLabel.textContent = `${formatDisplay(lowVal)} – ${formatDisplay(highVal)}`;
        function updateRangeDisplay() {
            let l = snapValue(parseFloat(lowInput.value));
            let h = snapValue(parseFloat(highInput.value));
            if (l > h) {
                l = h;
                lowInput.value = l;
            }
            valueLabel.textContent = `${formatDisplay(l)} – ${formatDisplay(h)}`;
        }
        function postRangeValues() {
            let l = snapValue(parseFloat(lowInput.value));
            let h = snapValue(parseFloat(highInput.value));
            if (l > h)
                l = h;
            if (isWebMode && changeActions.length > 0) {
                const batch = {};
                if (param)
                    batch[param] = String(l);
                if (secondaryParam)
                    batch[secondaryParam] = String(h);
                postParameters(batch).then(m => { if (m)
                    renderManifest(m); });
            }
        }
        lowInput.addEventListener('input', updateRangeDisplay);
        highInput.addEventListener('input', updateRangeDisplay);
        const debounceOpt = opts['DEBOUNCE'] || opts['debounce'];
        const debounceMs = parseInt(debounceOpt || '200', 10);
        if (fireOn === 'CHANGE') {
            let timer = null;
            const debounced = () => {
                clearTimeout(timer);
                timer = setTimeout(postRangeValues, debounceMs);
            };
            lowInput.addEventListener('input', debounced);
            highInput.addEventListener('input', debounced);
        }
        else {
            lowInput.addEventListener('change', postRangeValues);
            highInput.addEventListener('change', postRangeValues);
        }
        applyControlState(lowInput, visual, wrapper);
        if (lowInput.disabled)
            highInput.disabled = true;
        rangeInputs.appendChild(lowInput);
        rangeInputs.appendChild(highInput);
        wrapper.appendChild(rangeInputs);
        wrapper.appendChild(valueLabel);
    }
    else {
        // SINGLE mode
        let def = min;
        if (param && manifest && manifest.parameters) {
            const current = parseFloat(getParam(manifest.parameters, param));
            if (!isNaN(current))
                def = current;
        }
        else {
            const rawDef = parseFloat(visual.defaultValue || opts['DEFAULT'] || opts['default']);
            if (!isNaN(rawDef))
                def = rawDef;
        }
        def = snapValue(def);
        const input = document.createElement('input');
        input.type = 'range';
        setParameterAccessibleName(input, visual, param);
        input.min = String(min);
        input.max = String(max);
        input.step = String(step);
        input.value = String(def);
        if (showTicks)
            input.setAttribute('list', datalistId);
        if (param)
            input.setAttribute('data-parameter', param);
        applyControlState(input, visual, wrapper);
        const valueLabel = document.createElement('span');
        valueLabel.className = 'range-value';
        valueLabel.textContent = formatDisplay(def);
        function updateDisplay() {
            const snapped = snapValue(parseFloat(input.value));
            valueLabel.textContent = formatDisplay(snapped);
        }
        function postSliderValue() {
            const snapped = snapValue(parseFloat(input.value));
            if (isWebMode && changeActions.length > 0) {
                const batch = changeActions.reduce((o, a) => {
                    o[a.parameterName] = String(snapped);
                    return o;
                }, {});
                postParameters(batch).then(m => { if (m)
                    renderManifest(m); });
            }
        }
        input.addEventListener('input', updateDisplay);
        const debounceOpt = opts['DEBOUNCE'] || opts['debounce'];
        const debounceMs = parseInt(debounceOpt || '200', 10);
        if (fireOn === 'CHANGE') {
            let timer = null;
            input.addEventListener('input', () => {
                clearTimeout(timer);
                timer = setTimeout(postSliderValue, debounceMs);
            });
        }
        else {
            input.addEventListener('change', postSliderValue);
        }
        wrapper.appendChild(input);
        wrapper.appendChild(valueLabel);
    }
    container.appendChild(wrapper);
}
// ── Search ──────────────────────────────────────────────────────────────
export function renderSearch(container, visual, manifest) {
    const opts = visual.options || {};
    const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
    const param = changeActions.length > 0 ? changeActions[0].parameterName : null;
    const placeholder = visual.placeholder || opts['PLACEHOLDER'] || opts['placeholder'] || 'Search…';
    const showClear = isOn(opts['SHOW_CLEAR'] ?? opts['show_clear']);
    const matchMode = (getOption(opts, 'match_mode') || 'EXACT').toUpperCase();
    const minChars = parseInt(getOption(opts, 'min_chars') || '0', 10);
    function formatSearchValue(raw) {
        if (!raw)
            return '';
        switch (matchMode) {
            case 'CONTAINS': return `%${raw}%`;
            case 'STARTS_WITH': return `${raw}%`;
            default: return raw;
        }
    }
    const wrapper = document.createElement('div');
    wrapper.className = 'filter-wrapper';
    const inputShell = document.createElement('div');
    inputShell.className = 'search-input-shell';
    const input = document.createElement('input');
    input.type = 'search';
    setParameterAccessibleName(input, visual, param);
    input.placeholder = placeholder;
    if (param)
        input.setAttribute('data-parameter', param);
    // Restore current value from manifest parameters
    if (param && manifest && manifest.parameters) {
        const current = getParam(manifest.parameters, param);
        if (current)
            input.value = current;
    }
    inputShell.appendChild(input);
    let clearButton = null;
    if (showClear) {
        clearButton = document.createElement('button');
        clearButton.type = 'button';
        clearButton.className = 'search-clear-button';
        clearButton.textContent = '×';
        clearButton.setAttribute('aria-label', `Clear ${visual.title || visual.name || 'search'}`);
        clearButton.hidden = input.value.length === 0;
        clearButton.addEventListener('click', () => {
            input.value = '';
            clearButton.hidden = true;
            input.focus();
            input.dispatchEvent(new Event('input', { bubbles: true }));
        });
        inputShell.appendChild(clearButton);
    }
    wrapper.appendChild(inputShell);
    input.addEventListener('input', () => {
        if (clearButton)
            clearButton.hidden = input.value.length === 0;
    });
    applyControlState(input, visual, wrapper);
    if (isWebMode && changeActions.length > 0) {
        let debounceTimer = null;
        const debounceOpt = opts['DEBOUNCE'] || opts['debounce'];
        const debounceMs = parseInt(debounceOpt || '350', 10);
        input.addEventListener('input', () => {
            clearTimeout(debounceTimer);
            debounceTimer = setTimeout(() => {
                const raw = input.value.trim();
                if (raw.length > 0 && raw.length < minChars) {
                    return; // Suppress ON_CHANGE until minimum characters typed
                }
                const searchVal = raw.length === 0 ? '' : formatSearchValue(raw);
                const batch = changeActions.reduce((o, a) => {
                    o[a.parameterName] = searchVal;
                    return o;
                }, {});
                postParameters(batch).then(m => { if (m)
                    renderManifest(m); });
            }, debounceMs);
        });
    }
    container.appendChild(wrapper);
}
// ── Checkbox ────────────────────────────────────────────────────────────
export function renderCheckbox(container, visual, manifest) {
    const opts = visual.options || {};
    const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
    const param = changeActions.length > 0 ? changeActions[0].parameterName : null;
    const labelPos = (visual.labelPosition || 'TOP').toUpperCase();
    const labelText = (opts['LABEL'] || opts['label'] || visual.title || visual.name);
    const displayStyle = (opts['DISPLAY_STYLE'] || opts['display_style'] || 'CHECKBOX').toUpperCase();
    const isToggle = displayStyle === 'TOGGLE';
    const trueVal = opts['TRUE_VALUE'] ?? opts['true_value'] ?? '1';
    const falseVal = opts['FALSE_VALUE'] ?? opts['false_value'] ?? '0';
    let def = visual.defaultValue ?? opts['DEFAULT'] ?? opts['default'] ?? 'FALSE';
    let currentVal = undefined;
    if (param && manifest && manifest.parameters) {
        currentVal = getParam(manifest.parameters, param);
    }
    let checked;
    if (currentVal !== undefined) {
        const strVal = String(currentVal).trim();
        checked = strVal === String(trueVal) || isOn(strVal);
    }
    else {
        const strDef = String(def).trim();
        checked = strDef === String(trueVal) || isOn(strDef);
    }
    const wrapper = document.createElement('div');
    wrapper.className = 'filter-wrapper checkbox-wrapper pos-' + labelPos.toLowerCase();
    const input = document.createElement('input');
    input.type = 'checkbox';
    input.checked = checked;
    setParameterAccessibleName(input, visual, param);
    if (param)
        input.setAttribute('data-parameter', param);
    const label = document.createElement('label');
    label.textContent = labelText;
    if (isToggle) {
        const toggleWrapper = document.createElement('label');
        toggleWrapper.className = 'checkbox-toggle-wrapper';
        const switchSpan = document.createElement('span');
        switchSpan.className = 'checkbox-toggle-switch';
        switchSpan.appendChild(input);
        const sliderSpan = document.createElement('span');
        sliderSpan.className = 'checkbox-toggle-slider';
        switchSpan.appendChild(sliderSpan);
        toggleWrapper.appendChild(switchSpan);
        const toggleLabel = document.createElement('span');
        toggleLabel.className = 'toggle-label';
        toggleLabel.textContent = labelText;
        toggleWrapper.appendChild(toggleLabel);
        wrapper.appendChild(toggleWrapper);
    }
    else {
        if (labelPos === 'TOP' || labelPos === 'LEFT') {
            wrapper.appendChild(label);
        }
        wrapper.appendChild(input);
        if (labelPos !== 'TOP' && labelPos !== 'LEFT' && labelPos !== 'HIDDEN') {
            wrapper.appendChild(label);
        }
    }
    if (isWebMode && changeActions.length > 0) {
        input.addEventListener('change', () => {
            const val = input.checked ? trueVal : falseVal;
            const batch = changeActions.reduce((o, a) => {
                o[a.parameterName] = String(val);
                return o;
            }, {});
            postParameters(batch).then(m => { if (m)
                renderManifest(m); });
        });
    }
    applyControlState(input, visual, wrapper);
    container.appendChild(wrapper);
}
// ── Textbox ─────────────────────────────────────────────────────────────
export function renderTextbox(container, visual, manifest) {
    const opts = visual.options || {};
    const submitActions = actionsFor(visual, 'ON_SUBMIT').filter(a => a.type === 'SET_PARAMETER');
    const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
    const activeActions = submitActions.length > 0 ? submitActions : changeActions;
    const param = activeActions.length > 0 ? activeActions[0].parameterName : null;
    const labelPos = (visual.labelPosition || 'TOP').toUpperCase();
    const labelText = (opts['LABEL'] || opts['label'] || visual.title || visual.name);
    const placeholder = visual.placeholder || opts['PLACEHOLDER'] || opts['placeholder'] || '';
    const maxLengthValue = opts['MAX_LENGTH'] ?? opts['max_length'];
    const maxLength = typeof maxLengthValue === 'number'
        ? maxLengthValue
        : Number(String(maxLengthValue ?? '').trim());
    const isMultiline = isOn(opts['MULTILINE'] ?? opts['multiline']) || opts['ROWS'] != null || opts['rows'] != null;
    const rows = parseInt(opts['ROWS'] || opts['rows'] || '3', 10);
    const pattern = opts['PATTERN'] || opts['pattern'] || null;
    const validationMsg = opts['VALIDATION_MESSAGE'] || opts['validation_message'] || 'Invalid format';
    let regex = null;
    if (pattern) {
        try {
            regex = new RegExp(pattern);
        }
        catch (err) {
            console.warn(`PATTERN is not a valid regular expression, so this parameter is not validated: ${pattern}`, err);
        }
    }
    let def = visual.defaultValue || opts['DEFAULT'] || opts['default'] || '';
    if (param && manifest && manifest.parameters) {
        const current = getParam(manifest.parameters, param);
        if (current !== undefined)
            def = current;
    }
    const wrapper = document.createElement('div');
    wrapper.className = 'filter-wrapper textbox-wrapper pos-' + labelPos.toLowerCase() + (isMultiline ? ' is-multiline' : '');
    const input = isMultiline ? document.createElement('textarea') : document.createElement('input');
    if (!isMultiline) /** @type {HTMLInputElement} */
        (input).type = 'text';
    else /** @type {HTMLTextAreaElement} */
        (input).rows = rows > 0 ? rows : 3;
    setParameterAccessibleName(input, visual, param);
    input.value = def;
    input.placeholder = placeholder;
    if (Number.isSafeInteger(maxLength) && maxLength > 0)
        input.maxLength = maxLength;
    if (param)
        input.setAttribute('data-parameter', param);
    const label = document.createElement('label');
    label.textContent = labelText;
    if (labelPos === 'TOP' || labelPos === 'LEFT') {
        wrapper.appendChild(label);
    }
    wrapper.appendChild(input);
    const errorEl = document.createElement('div');
    errorEl.className = 'filter-error';
    errorEl.textContent = validationMsg;
    errorEl.style.display = 'none';
    wrapper.appendChild(errorEl);
    function validateInput() {
        if (!regex)
            return true;
        const val = input.value;
        if (val === '') {
            input.classList.remove('is-invalid');
            errorEl.style.display = 'none';
            return true;
        }
        const valid = regex.test(val);
        if (!valid) {
            input.classList.add('is-invalid');
            errorEl.style.display = 'block';
        }
        else {
            input.classList.remove('is-invalid');
            errorEl.style.display = 'none';
        }
        return valid;
    }
    input.addEventListener('input', () => {
        if (regex)
            validateInput();
    });
    function postValues(actionsList) {
        if (!validateInput())
            return;
        if (isWebMode && actionsList.length > 0) {
            const batch = actionsList.reduce((o, a) => {
                o[a.parameterName] = input.value;
                return o;
            }, {});
            postParameters(batch).then(m => { if (m)
                renderManifest(m); });
        }
    }
    applyControlState(input, visual, wrapper);
    if (submitActions.length > 0) {
        input.addEventListener('blur', () => postValues(submitActions));
        input.addEventListener('keydown', (e) => {
            if ( /** @type {KeyboardEvent} */(e).key === 'Enter' && (!isMultiline || /** @type {KeyboardEvent} */ (e).ctrlKey || /** @type {KeyboardEvent} */ (e).metaKey)) {
                if (!isMultiline)
                    e.preventDefault();
                postValues(submitActions);
            }
        });
    }
    else if (changeActions.length > 0) {
        const debounceOpt = opts['DEBOUNCE'] || opts['debounce'];
        if (debounceOpt != null) {
            const debounceMs = parseInt(debounceOpt, 10) || 300;
            let timer = null;
            input.addEventListener('input', () => {
                clearTimeout(timer);
                timer = setTimeout(() => postValues(changeActions), debounceMs);
            });
        }
        else {
            input.addEventListener('change', () => postValues(changeActions));
        }
    }
    container.appendChild(wrapper);
}
// ── Numberbox ───────────────────────────────────────────────────────────
export function renderNumberbox(container, visual, manifest) {
    const opts = visual.options || {};
    const submitActions = actionsFor(visual, 'ON_SUBMIT').filter(a => a.type === 'SET_PARAMETER');
    const changeActions = actionsFor(visual, 'ON_CHANGE').filter(a => a.type === 'SET_PARAMETER');
    const activeActions = submitActions.length > 0 ? submitActions : changeActions;
    const param = activeActions.length > 0 ? activeActions[0].parameterName : null;
    const labelPos = (visual.labelPosition || 'TOP').toUpperCase();
    const labelText = (opts['LABEL'] || opts['label'] || visual.title || visual.name);
    const min = visual.min != null ? visual.min : (opts['MIN'] != null ? parseFloat(opts['MIN']) : null);
    const max = visual.max != null ? visual.max : (opts['MAX'] != null ? parseFloat(opts['MAX']) : null);
    const decimals = visual.decimals != null ? visual.decimals : (opts['DECIMALS'] != null ? parseInt(opts['DECIMALS'], 10) : 0);
    const stepOpt = opts['STEP'] || opts['step'];
    const stepVal = stepOpt != null ? parseFloat(stepOpt) : (decimals > 0 ? Math.pow(10, -decimals) : 1);
    const stepStr = stepOpt != null ? String(stepOpt) : (decimals > 0 ? Math.pow(10, -decimals).toFixed(decimals) : '1');
    const showStepper = isOn(opts['SHOW_STEPPER'] ?? opts['show_stepper']);
    const prefix = opts['PREFIX'] || opts['prefix'] || '';
    const suffix = opts['SUFFIX'] || opts['suffix'] || '';
    const formatOpt = opts['FORMAT'] || opts['format'] || null;
    let def = visual.defaultValue ?? opts['DEFAULT'] ?? opts['default'] ?? '0';
    let rawNum = parseFloat(def);
    if (isNaN(rawNum))
        rawNum = 0;
    if (param && manifest && manifest.parameters) {
        const current = getParam(manifest.parameters, param);
        if (current !== undefined && !isNaN(parseFloat(current)))
            rawNum = parseFloat(current);
    }
    const wrapper = document.createElement('div');
    wrapper.className = 'filter-wrapper numberbox-wrapper pos-' + labelPos.toLowerCase();
    const input = document.createElement('input');
    input.type = formatOpt ? 'text' : 'number';
    setParameterAccessibleName(input, visual, param);
    input.placeholder = visual.placeholder || opts['PLACEHOLDER'] || opts['placeholder'] || '';
    if (min !== undefined && min !== null)
        input.min = min;
    if (max !== null && max !== undefined)
        input.max = max;
    input.step = stepStr;
    if (param)
        input.setAttribute('data-parameter', param);
    function displayVal(val) {
        return formatOpt ? formatValue(val, formatOpt) : String(val);
    }
    input.value = displayVal(rawNum);
    if (formatOpt) {
        input.addEventListener('focus', () => {
            input.value = String(rawNum);
        });
        input.addEventListener('blur', () => {
            const parsed = parseFloat(input.value);
            if (!isNaN(parsed))
                rawNum = parsed;
            input.value = displayVal(rawNum);
        });
    }
    const label = document.createElement('label');
    label.textContent = labelText;
    if (labelPos === 'TOP' || labelPos === 'LEFT') {
        wrapper.appendChild(label);
    }
    function setNumericValue(val) {
        let n = val;
        if (min !== null && min !== undefined && n < min)
            n = min;
        if (max !== null && max !== undefined && n > max)
            n = max;
        rawNum = n;
        input.value = (document.activeElement === input && formatOpt) ? String(rawNum) : displayVal(rawNum);
    }
    function postValues(actionsList) {
        const parsed = parseFloat(input.value.replace(/[^0-9.-]+/g, ''));
        if (!isNaN(parsed))
            setNumericValue(parsed);
        if (isWebMode && actionsList.length > 0) {
            const batch = actionsList.reduce((o, a) => {
                o[a.parameterName] = String(rawNum);
                return o;
            }, {});
            postParameters(batch).then(m => { if (m)
                renderManifest(m); });
        }
    }
    const hasGroup = prefix || suffix || showStepper;
    if (hasGroup) {
        const group = document.createElement('div');
        group.className = 'numberbox-group';
        if (prefix) {
            const preSpan = document.createElement('span');
            preSpan.className = 'numberbox-prefix';
            preSpan.textContent = prefix;
            group.appendChild(preSpan);
        }
        if (showStepper) {
            const decBtn = document.createElement('button');
            decBtn.type = 'button';
            decBtn.className = 'numberbox-stepper-btn stepper-dec';
            decBtn.textContent = '−';
            decBtn.setAttribute('aria-label', `Decrease ${labelText}`);
            decBtn.addEventListener('click', () => {
                setNumericValue(rawNum - stepVal);
                postValues(activeActions);
            });
            group.appendChild(decBtn);
        }
        group.appendChild(input);
        if (showStepper) {
            const incBtn = document.createElement('button');
            incBtn.type = 'button';
            incBtn.className = 'numberbox-stepper-btn stepper-inc';
            incBtn.textContent = '+';
            incBtn.setAttribute('aria-label', `Increase ${labelText}`);
            incBtn.addEventListener('click', () => {
                setNumericValue(rawNum + stepVal);
                postValues(activeActions);
            });
            group.appendChild(incBtn);
        }
        if (suffix) {
            const sufSpan = document.createElement('span');
            sufSpan.className = 'numberbox-suffix';
            sufSpan.textContent = suffix;
            group.appendChild(sufSpan);
        }
        wrapper.appendChild(group);
    }
    else {
        wrapper.appendChild(input);
    }
    applyControlState(input, visual, wrapper);
    if (input.disabled) {
        /** @type {NodeListOf<HTMLInputElement | HTMLButtonElement>} */ (wrapper.querySelectorAll('.numberbox-stepper-btn')).forEach(b => b.disabled = true);
    }
    if (submitActions.length > 0) {
        input.addEventListener('blur', () => postValues(submitActions));
        input.addEventListener('keydown', (e) => {
            if (e.key === 'Enter') {
                e.preventDefault();
                postValues(submitActions);
            }
        });
    }
    else if (changeActions.length > 0) {
        const debounceOpt = opts['DEBOUNCE'] || opts['debounce'];
        if (debounceOpt != null) {
            const debounceMs = parseInt(debounceOpt, 10) || 300;
            let timer = null;
            input.addEventListener('input', () => {
                clearTimeout(timer);
                timer = setTimeout(() => postValues(changeActions), debounceMs);
            });
        }
        else {
            input.addEventListener('change', () => postValues(changeActions));
        }
    }
    container.appendChild(wrapper);
}
// ── Button ──────────────────────────────────────────────────────────────
export function renderButton(container, btn) {
    const styles = btn.styles || {};
    const opts = btn.options || {};
    const btnEl = document.createElement('button');
    applyDesignTokens(btnEl, btn.styles, false);
    btnEl.className = 'report-btn';
    btnEl.setAttribute('data-name', btn.name);
    const variant = (getOption(opts, 'VARIANT') || 'secondary').toLowerCase();
    btnEl.classList.add('btn-' + variant);
    const tag = getOption(opts, 'TAG') || getStyle(styles, 'TAG');
    if (tag)
        btnEl.setAttribute('data-tag', tag);
    if (btn.tooltip && btn.tooltip.text)
        btnEl.title = btn.tooltip.text;
    // Apply inline styles from STYLE definition
    const bg = getStyle(styles, 'BACKGROUND') || getStyle(styles, 'BACKGROUND-COLOR');
    const fg = getStyle(styles, 'COLOR');
    const pad = getStyle(styles, 'PADDING');
    const rad = getStyle(styles, 'BORDER-RADIUS');
    const fw = getStyle(styles, 'FONT-WEIGHT');
    const fs = getStyle(styles, 'FONT-SIZE');
    const brd = getStyle(styles, 'BORDER');
    const shd = getStyle(styles, 'BOX-SHADOW');
    if (bg)
        btnEl.style.background = bg;
    if (fg)
        btnEl.style.color = fg;
    if (pad)
        btnEl.style.padding = pad;
    if (rad)
        btnEl.style.borderRadius = rad;
    if (fw)
        btnEl.style.fontWeight = fw;
    if (fs)
        btnEl.style.fontSize = fs;
    if (brd)
        btnEl.style.border = brd;
    if (shd)
        btnEl.style.boxShadow = shd;
    btnEl.style.cursor = 'pointer';
    if (!brd && !variant)
        btnEl.style.border = 'none';
    if (!fw)
        btnEl.style.fontWeight = '600';
    const icon = getOption(opts, 'ICON');
    const iconPos = (getOption(opts, 'ICON_POSITION') || 'left').toLowerCase();
    const baseTitle = btn.title || btn.name;
    function updateButtonContent(titleText) {
        btnEl.innerHTML = '';
        const textSpan = document.createElement('span');
        textSpan.className = 'btn-label';
        textSpan.textContent = titleText;
        let iconEl = null;
        if (icon) {
            iconEl = document.createElement('span');
            iconEl.className = 'btn-icon';
            if (icon.includes('.') || icon.includes('/')) {
                iconEl.innerHTML = `<img src="${escHtml(safeUrl(icon))}" style="width:16px;height:16px;vertical-align:middle;">`;
            }
            else {
                iconEl.textContent = icon;
            }
        }
        if (iconEl && iconPos === 'left') {
            btnEl.appendChild(iconEl);
            btnEl.appendChild(document.createTextNode(' '));
        }
        btnEl.appendChild(textSpan);
        if (iconEl && iconPos === 'right') {
            btnEl.appendChild(document.createTextNode(' '));
            btnEl.appendChild(iconEl);
        }
    }
    updateButtonContent(baseTitle);
    // Disabled expression
    const disabledExpr = getOption(opts, 'DISABLED') || getStyle(styles, 'DISABLED');
    if (disabledExpr != null && evaluateExpressionAgainstParameters(disabledExpr, parameters)) {
        btnEl.disabled = true;
        btnEl.classList.add('is-disabled');
        btnEl.setAttribute('aria-disabled', 'true');
    }
    // Toggle mode support
    const mode = (getOption(opts, 'MODE') || '').toUpperCase();
    const isToggle = mode === 'TOGGLE';
    const onValue = getOption(opts, 'ON_VALUE') || '1';
    const offValue = getOption(opts, 'OFF_VALUE') || '0';
    const defaultState = (getOption(opts, 'DEFAULT') || 'OFF').toUpperCase();
    let isToggledOn = _uiStates[btn.name]?.toggled ?? (defaultState === 'ON');
    if (isToggle) {
        btnEl.classList.add('mode-toggle');
        if (isToggledOn)
            btnEl.classList.add('btn-active');
    }
    // Mark RUN buttons so updateStagedUI can target them precisely
    if ((btn.actions || []).some((a) => a.type === 'APPLY_PARAMETERS')) {
        btnEl.dataset.isRunBtn = 'true';
    }
    btnEl.addEventListener('click', async () => {
        if (btnEl.disabled)
            return;
        // Confirm prompt
        const confirmMsg = getOption(opts, 'CONFIRM');
        if (confirmMsg) {
            if (!await window.ETLSQLFeedback.confirm(confirmMsg, { title: 'Confirm action', confirmLabel: 'Continue', auditAction: `report.button.${btn.name}` }))
                return;
        }
        // Toggle mode state flip
        if (isToggle) {
            isToggledOn = !isToggledOn;
            _uiStates[btn.name] = Object.assign({}, _uiStates[btn.name], { toggled: isToggledOn });
            if (isToggledOn)
                btnEl.classList.add('btn-active');
            else
                btnEl.classList.remove('btn-active');
            const toggleVal = isToggledOn ? onValue : offValue;
            const setParams = (btn.actions || []).filter((a) => a.type === 'SET_PARAMETER');
            if (setParams.length > 0 && !setParams[0].valueExpression) {
                const batch = {};
                setParams.forEach((a) => batch[a.parameterName] = toggleVal);
                if (vscode)
                    vscode.postMessage({ type: 'refreshReport', parameters: batch });
                else
                    postParameters(batch).then(m => { if (m)
                        renderManifest(m); });
                return;
            }
        }
        const clickActions = actionsFor(btn, 'ON_CLICK');
        if (clickActions.length === 0)
            return;
        // Spinner feedback
        const showSpinner = isOn(getOption(opts, 'SHOW_SPINNER'));
        let spinnerEl = null;
        if (showSpinner) {
            btnEl.classList.add('btn-loading');
            spinnerEl = document.createElement('span');
            spinnerEl.className = 'btn-spinner';
            btnEl.prepend(spinnerEl);
        }
        try {
            for (const action of clickActions) {
                await executeAction(action, [], [], btn.name, btn);
            }
        }
        finally {
            if (spinnerEl) {
                spinnerEl.remove();
                btnEl.classList.remove('btn-loading');
            }
        }
    });
    container.appendChild(btnEl);
}
