// @ts-nocheck — generated copy; check the canonical source.
/* GENERATED FILE - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/Shared/rt-controls-date.js
 * Edit the canonical source, then run: node .\scripts\sync-assets.js
 */

/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.ReportRuntime/Resources/TypeScript/rt-controls-date.ts
 * Run: node scripts/sync-assets.js
 */
/**
 * Copyright 2026 Charles Clemens and ETL-SQL contributors
 * Licensed under the Apache License, Version 2.0.
 *
 * Absolute and relative date parameter controls.
 */
import { actionsFor } from './rt-actions.js';
import { getOption, getParam, parseMultiParameter } from './rt-util.js';
import { applyControlState, setParameterAccessibleName } from './rt-controls-input.js';
import { isWebMode } from './rt-state.js';
import { postParameters } from './rt-transport.js';
import { renderManifest } from './report-runtime.js';
// ── DatePicker ──────────────────────────────────────────────────────────
export function renderDatePicker(container, visual, manifest) {
    const opts = visual.options || {};
    const changeActions = actionsFor(visual, 'ON_CHANGE').filter((a) => a.type === 'SET_PARAMETER');
    const startAction = changeActions.length > 0 ? changeActions[0] : null;
    const param = startAction ? startAction.parameterName : null;
    const secondaryParam = (startAction && startAction.secondaryParameterName) || (changeActions.length > 1 ? changeActions[1].parameterName : null);
    const min = opts['MIN'] || opts['min'] || '';
    const max = opts['MAX'] || opts['max'] || '';
    const mode = (getOption(opts, 'mode') || 'SINGLE').toUpperCase();
    const isRange = mode === 'RANGE';
    const formatOpt = getOption(opts, 'format') || '';
    const weekStart = (getOption(opts, 'week_start') || 'SUN').toUpperCase();
    const displayOpt = (getOption(opts, 'display') || 'DROPDOWN').toUpperCase();
    const isInline = displayOpt === 'INLINE';
    function parseArrayOption(opt) {
        if (!opt)
            return [];
        if (Array.isArray(opt))
            return opt.map(s => String(s).trim().toUpperCase());
        if (typeof opt === 'string' && opt.startsWith('[')) {
            try {
                const parsed = JSON.parse(opt);
                if (Array.isArray(parsed))
                    return parsed.map(s => String(s).trim().toUpperCase());
            }
            catch {
                // Not JSON after all, so fall through to the comma-separated form below.
            }
        }
        return String(opt).split(',').map(s => s.trim().toUpperCase()).filter(Boolean);
    }
    const disabledDates = parseArrayOption(opts['DISABLED_DATES'] || opts['disabled_dates']);
    const disabledDays = parseArrayOption(opts['DISABLED_DAYS'] || opts['disabled_days']);
    function isDateDisabled(dateStr) {
        if (!dateStr)
            return false;
        const norm = dateStr.trim().toUpperCase();
        if (disabledDates.includes(norm))
            return true;
        if (disabledDays.length > 0) {
            const dt = new Date(dateStr + 'T00:00:00Z');
            if (!isNaN(dt.getTime())) {
                const dayNames = ['SUN', 'MON', 'TUE', 'WED', 'THU', 'FRI', 'SAT'];
                const dName = dayNames[dt.getUTCDay()];
                if (disabledDays.includes(dName))
                    return true;
            }
        }
        return false;
    }
    const wrapper = document.createElement('div');
    wrapper.className = 'filter-wrapper' + (isInline ? ' datepicker-inline' : '');
    if (weekStart)
        wrapper.setAttribute('data-week-start', weekStart);
    const errorEl = document.createElement('div');
    errorEl.className = 'filter-error';
    errorEl.style.display = 'none';
    if (isRange) {
        let startVal = '';
        let endVal = '';
        if (manifest && manifest.parameters) {
            if (param)
                startVal = getParam(manifest.parameters, param) ?? '';
            if (secondaryParam)
                endVal = getParam(manifest.parameters, secondaryParam) ?? '';
        }
        if (!startVal && !endVal) {
            const def = visual.defaultValue || opts['DEFAULT'] || opts['default'] || '';
            const parts = parseMultiParameter(def);
            if (parts.length > 0)
                startVal = parts[0];
            if (parts.length > 1)
                endVal = parts[1];
        }
        const rangeWrapper = document.createElement('div');
        rangeWrapper.className = 'datepicker-range-wrapper';
        function createDateBox(initialVal, pName, qualifier, onValChange) {
            const box = document.createElement('div');
            box.className = 'reldate-wrapper';
            const textInput = document.createElement('input');
            textInput.type = 'text';
            setParameterAccessibleName(textInput, visual, pName, qualifier);
            textInput.placeholder = formatOpt || 'YYYY-MM-DD';
            textInput.value = initialVal;
            if (pName)
                textInput.setAttribute('data-parameter', pName);
            const datePicker = document.createElement('input');
            datePicker.type = 'date';
            setParameterAccessibleName(datePicker, visual, pName, `${qualifier} picker`);
            datePicker.className = 'reldate-native-picker';
            if (min)
                datePicker.min = min;
            if (max)
                datePicker.max = max;
            if (initialVal && /^\d{4}-\d{2}-\d{2}$/.test(initialVal))
                datePicker.value = initialVal;
            if (pName)
                datePicker.setAttribute('data-parameter', pName);
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'reldate-btn';
            btn.title = `Pick ${qualifier}`;
            btn.setAttribute('aria-label', `Pick ${qualifier}`);
            btn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect><line x1="16" y1="2" x2="16" y2="6"></line><line x1="8" y1="2" x2="8" y2="6"></line><line x1="3" y1="10" x2="21" y2="10"></line></svg>';
            btn.addEventListener('click', () => {
                if (typeof datePicker.showPicker === 'function')
                    datePicker.showPicker();
                else
                    datePicker.focus();
            });
            datePicker.addEventListener('change', () => {
                textInput.value = datePicker.value;
                textInput.dispatchEvent(new Event('change'));
            });
            textInput.addEventListener('change', () => {
                if (/^\d{4}-\d{2}-\d{2}$/.test(textInput.value)) {
                    datePicker.value = textInput.value;
                }
                onValChange();
            });
            const actions = document.createElement('div');
            actions.className = 'reldate-actions';
            const pickerSlot = document.createElement('span');
            pickerSlot.className = 'reldate-picker-slot';
            pickerSlot.appendChild(btn);
            pickerSlot.appendChild(datePicker);
            actions.appendChild(pickerSlot);
            box.appendChild(textInput);
            box.appendChild(actions);
            applyControlState(textInput, visual, box);
            if (textInput.disabled) {
                datePicker.disabled = true;
                btn.disabled = true;
            }
            return { box, textInput, datePicker };
        }
        function validateAndPostRange() {
            const sVal = startBox.textInput.value.trim();
            const eVal = endBox.textInput.value.trim();
            let errMsg = null;
            if (isDateDisabled(sVal)) {
                errMsg = 'Start date is disabled';
                startBox.box.classList.add('is-invalid');
            }
            else {
                startBox.box.classList.remove('is-invalid');
            }
            if (isDateDisabled(eVal)) {
                errMsg = errMsg ? (errMsg + '; End date is disabled') : 'End date is disabled';
                endBox.box.classList.add('is-invalid');
            }
            else {
                endBox.box.classList.remove('is-invalid');
            }
            if (!errMsg && sVal && eVal && sVal > eVal) {
                errMsg = 'Start date cannot be after end date';
                startBox.box.classList.add('is-invalid');
                endBox.box.classList.add('is-invalid');
            }
            if (errMsg) {
                errorEl.textContent = errMsg;
                errorEl.style.display = 'block';
                return;
            }
            errorEl.style.display = 'none';
            startBox.box.classList.remove('is-invalid');
            endBox.box.classList.remove('is-invalid');
            if (isWebMode && changeActions.length > 0) {
                const batch = {};
                if (param)
                    batch[param] = sVal;
                if (secondaryParam)
                    batch[secondaryParam] = eVal;
                if (Object.keys(batch).length > 0) {
                    postParameters(batch).then(m => { if (m)
                        renderManifest(m); });
                }
            }
        }
        const startBox = createDateBox(startVal, param, 'start date', validateAndPostRange);
        const sep = document.createElement('span');
        sep.textContent = '–';
        sep.style.fontWeight = 'bold';
        const endBox = createDateBox(endVal, secondaryParam, 'end date', validateAndPostRange);
        rangeWrapper.appendChild(startBox.box);
        rangeWrapper.appendChild(sep);
        rangeWrapper.appendChild(endBox.box);
        wrapper.appendChild(rangeWrapper);
        wrapper.appendChild(errorEl);
    }
    else {
        // SINGLE mode
        let def = (visual.defaultValue || opts['DEFAULT'] || opts['default'] || '');
        if (param && manifest && manifest.parameters) {
            const current = getParam(manifest.parameters, param);
            if (current !== undefined)
                def = current;
        }
        const inputRow = document.createElement('div');
        inputRow.className = 'reldate-wrapper';
        const textInput = document.createElement('input');
        textInput.type = 'text';
        setParameterAccessibleName(textInput, visual, param, 'date');
        textInput.placeholder = formatOpt || 'YYYY-MM-DD or T-1…';
        textInput.value = def;
        if (param)
            textInput.setAttribute('data-parameter', param);
        const datePicker = document.createElement('input');
        datePicker.type = 'date';
        setParameterAccessibleName(datePicker, visual, param, 'native date picker');
        datePicker.className = 'reldate-native-picker';
        if (min)
            datePicker.min = min;
        if (max)
            datePicker.max = max;
        if (def && /^\d{4}-\d{2}-\d{2}$/.test(def))
            datePicker.value = def;
        if (param)
            datePicker.setAttribute('data-parameter', param);
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = 'reldate-btn';
        btn.title = 'Pick a date';
        btn.setAttribute('aria-label', 'Pick a date');
        btn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect><line x1="16" y1="2" x2="16" y2="6"></line><line x1="8" y1="2" x2="8" y2="6"></line><line x1="3" y1="10" x2="21" y2="10"></line></svg>';
        btn.addEventListener('click', () => {
            if (typeof datePicker.showPicker === 'function')
                datePicker.showPicker();
            else
                datePicker.focus();
        });
        datePicker.addEventListener('change', () => {
            textInput.value = datePicker.value;
            textInput.dispatchEvent(new Event('change'));
        });
        const actions = document.createElement('div');
        actions.className = 'reldate-actions';
        const pickerSlot = document.createElement('span');
        pickerSlot.className = 'reldate-picker-slot';
        pickerSlot.appendChild(btn);
        pickerSlot.appendChild(datePicker);
        actions.appendChild(pickerSlot);
        inputRow.appendChild(textInput);
        inputRow.appendChild(actions);
        wrapper.appendChild(inputRow);
        wrapper.appendChild(errorEl);
        applyControlState(textInput, visual, inputRow);
        if (textInput.disabled) {
            datePicker.disabled = true;
            btn.disabled = true;
        }
        if (isWebMode && changeActions.length > 0) {
            const debounceOpt = opts['DEBOUNCE'] || opts['debounce'];
            const onDateChange = () => {
                const val = textInput.value.trim();
                if (/^\d{4}-\d{2}-\d{2}$/.test(val)) {
                    datePicker.value = val;
                }
                if (isDateDisabled(val)) {
                    inputRow.classList.add('is-invalid');
                    errorEl.textContent = 'Selected date is disabled';
                    errorEl.style.display = 'block';
                    return;
                }
                inputRow.classList.remove('is-invalid');
                errorEl.style.display = 'none';
                const batch = changeActions.reduce((o, a) => { o[a.parameterName] = textInput.value; return o; }, {});
                postParameters(batch).then(m => { if (m)
                    renderManifest(m); });
            };
            if (debounceOpt != null) {
                let dTimer = null;
                const dMs = parseInt(debounceOpt, 10) || 300;
                textInput.addEventListener('input', () => {
                    clearTimeout(dTimer);
                    dTimer = setTimeout(onDateChange, dMs);
                });
            }
            textInput.addEventListener('change', onDateChange);
        }
    }
    container.appendChild(wrapper);
}
// ── RelDatePicker ────────────────────────────────────────────────────────
function showRelDateHelpModal() {
    const modal = document.createElement('div');
    modal.className = 'required-params-modal'; // recycle the overlay styling
    modal.style.zIndex = '30000'; // above everything
    const content = document.createElement('div');
    content.className = 'modal-content';
    content.style.width = '550px';
    const title = document.createElement('h2');
    title.textContent = 'Relative Date Syntax';
    title.style.marginTop = '0';
    const desc = document.createElement('div');
    desc.style.fontSize = '14px';
    desc.style.lineHeight = '1.5';
    desc.innerHTML = `
            <p><code>RELDATE</code> parameters resolve to the exact local time at the moment of execution.</p>
            <table class="md-table" style="margin-top: 12px; margin-bottom: 16px;">
                <tr><th>Anchor</th><th>Resolves to</th></tr>
                <tr><td><strong>D</strong></td><td>Today at midnight</td></tr>
                <tr><td><strong>W</strong> / <strong>WS</strong></td><td>Start of current week</td></tr>
                <tr><td><strong>WE</strong></td><td>Last day of current week</td></tr>
                <tr><td><strong>M</strong> / <strong>MS</strong></td><td>1st of current month at midnight</td></tr>
                <tr><td><strong>ME</strong></td><td>Last day of current month at midnight</td></tr>
                <tr><td><strong>FQ</strong> / <strong>FQS</strong></td><td>Start of current fiscal quarter</td></tr>
                <tr><td><strong>FQE</strong></td><td>Last day of current fiscal quarter</td></tr>
                <tr><td><strong>FY</strong> / <strong>FYS</strong></td><td>Start of current fiscal year</td></tr>
                <tr><td><strong>FYE</strong></td><td>Last day of current fiscal year</td></tr>
                <tr><td><strong>Y</strong> / <strong>YS</strong></td><td>Jan 1 of current year at midnight</td></tr>
                <tr><td><strong>YE</strong></td><td>Dec 31 of current year at midnight</td></tr>
                <tr><td><strong>N</strong></td><td>Exact current local datetime</td></tr>
            </table>
            <p><strong>Arithmetic:</strong> Append <code>-n</code> or <code>+n</code> to shift by <em>n</em> periods.</p>
            <ul>
                <li><code>D-1</code> = Yesterday</li>
                <li><code>D+30</code> = 30 days in future</li>
                <li><code>FQ-1</code> = Previous fiscal quarter</li>
                <li><code>FY+1</code> = Next fiscal year</li>
                <li><code>M-1</code> = First day of last month</li>
                <li><code>ME-1</code> = Last day of last month</li>
            </ul>
            <p style="margin-top: 12px; margin-bottom: 8px;"><strong>Time Offsets (from N):</strong> Use <code>H</code> (hours), <code>I</code> (minutes), or <code>S</code> (seconds).</p>
            <ul style="margin-bottom: 0;">
                <li><code>N-2H</code> = Exactly 2 hours ago</li>
                <li><code>N+30I</code> = Exactly 30 minutes from now</li>
            </ul>
            <p style="margin-top: 12px; font-size: 12px; color: #667085;">Fiscal anchors evaluate with <code>FISCAL_YEAR_START = month</code> (default 1 = January).</p>
        `;
    const footer = document.createElement('div');
    footer.className = 'modal-footer';
    footer.style.marginTop = '24px';
    const closeBtn = document.createElement('button');
    closeBtn.className = 'header-btn primary';
    closeBtn.textContent = 'Got it';
    closeBtn.addEventListener('click', () => {
        document.body.removeChild(modal);
    });
    footer.appendChild(closeBtn);
    content.appendChild(title);
    content.appendChild(desc);
    content.appendChild(footer);
    modal.appendChild(content);
    document.body.appendChild(modal);
}
export function renderRelDatePicker(container, visual, manifest) {
    const opts = visual.options || {};
    const changeActions = actionsFor(visual, 'ON_CHANGE').filter((a) => a.type === 'SET_PARAMETER');
    const startAction = changeActions.length > 0 ? changeActions[0] : null;
    const param = startAction ? startAction.parameterName : null;
    const secondaryParam = (startAction && startAction.secondaryParameterName) || (changeActions.length > 1 ? changeActions[1].parameterName : null);
    const min = opts['MIN'] || opts['min'] || '';
    const max = opts['MAX'] || opts['max'] || '';
    const mode = (getOption(opts, 'mode') || 'SINGLE').toUpperCase();
    const isRange = mode === 'RANGE';
    const relDateRegex = /^\s*(D|W|WS|WE|M|MS|ME|Y|YS|YE|FQ|FQS|FQE|FY|FYS|FYE|N)([-+]\d+[DHIMS]?)?\s*$/i;
    const isoDateRegex = /^\d{4}-\d{2}-\d{2}$/;
    function isValidRelDate(expr) {
        if (!expr || !expr.trim())
            return false;
        const s = expr.trim();
        return relDateRegex.test(s) || isoDateRegex.test(s);
    }
    let quickPicks = [
        { label: 'D', value: 'D-0' },
        { label: 'D-1', value: 'D-1' },
        { label: 'M', value: 'M-0' },
        { label: 'M-1', value: 'M-1' },
        { label: 'Y', value: 'Y-0' },
        { label: 'Y-1', value: 'Y-1' },
    ];
    const qpOpt = opts['QUICK_PICKS'] || opts['quick_picks'];
    if (qpOpt) {
        try {
            const customQp = typeof qpOpt === 'string' ? JSON.parse(qpOpt) : qpOpt;
            if (Array.isArray(customQp) && customQp.length > 0) {
                quickPicks = customQp;
            }
        }
        catch {
            // QUICK_PICKS is author-supplied; unparseable leaves the built-in picks.
        }
    }
    const wrapper = document.createElement('div');
    wrapper.className = 'filter-wrapper';
    const errorEl = document.createElement('div');
    errorEl.className = 'filter-error';
    errorEl.style.display = 'none';
    function createRelDateRow(initialVal, pName, qualifier, onValChange) {
        const rowWrapper = document.createElement('div');
        rowWrapper.style.display = 'flex';
        rowWrapper.style.flexDirection = 'column';
        rowWrapper.style.gap = '6px';
        const inputRow = document.createElement('div');
        inputRow.className = 'reldate-wrapper';
        const textInput = document.createElement('input');
        textInput.type = 'text';
        if (qualifier === 'relative date') {
            setParameterAccessibleName(textInput, visual, param, 'relative date');
        }
        else {
            setParameterAccessibleName(textInput, visual, pName, qualifier);
        }
        textInput.placeholder = 'D-7, M-1, Y-1 or YYYY-MM-DD';
        textInput.value = initialVal;
        if (pName)
            textInput.setAttribute('data-parameter', pName);
        const hiddenDate = document.createElement('input');
        hiddenDate.type = 'date';
        setParameterAccessibleName(hiddenDate, visual, pName, `${qualifier} native date picker`);
        hiddenDate.className = 'reldate-native-picker';
        if (min)
            hiddenDate.min = min;
        if (max)
            hiddenDate.max = max;
        if (initialVal && /^\d{4}-\d{2}-\d{2}$/.test(initialVal))
            hiddenDate.value = initialVal;
        if (pName)
            hiddenDate.setAttribute('data-parameter', pName);
        const calBtn = document.createElement('button');
        calBtn.type = 'button';
        calBtn.className = 'reldate-btn';
        calBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"></rect><line x1="16" y1="2" x2="16" y2="6"></line><line x1="8" y1="2" x2="8" y2="6"></line><line x1="3" y1="10" x2="21" y2="10"></line></svg>';
        calBtn.title = 'Pick a date (writes ISO date)';
        calBtn.setAttribute('aria-label', 'Pick a date');
        calBtn.addEventListener('click', () => {
            if (typeof hiddenDate.showPicker === 'function')
                hiddenDate.showPicker();
            else
                hiddenDate.focus();
        });
        const infoBtn = document.createElement('button');
        infoBtn.type = 'button';
        infoBtn.className = 'reldate-btn';
        infoBtn.innerHTML = '<svg xmlns="http://www.w3.org/2000/svg" width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>';
        infoBtn.title = 'View Relative Date Syntax Help';
        infoBtn.setAttribute('aria-label', 'View relative date syntax help');
        infoBtn.addEventListener('click', showRelDateHelpModal);
        hiddenDate.addEventListener('change', () => {
            textInput.value = hiddenDate.value;
            textInput.dispatchEvent(new Event('change'));
        });
        const actions = document.createElement('div');
        actions.className = 'reldate-actions';
        const pickerSlot = document.createElement('span');
        pickerSlot.className = 'reldate-picker-slot';
        pickerSlot.appendChild(calBtn);
        pickerSlot.appendChild(hiddenDate);
        actions.appendChild(pickerSlot);
        actions.appendChild(infoBtn);
        inputRow.appendChild(textInput);
        inputRow.appendChild(actions);
        // Quick-pick buttons
        const quickRow = document.createElement('div');
        quickRow.className = 'reldate-quick';
        quickPicks.forEach((qp) => {
            const btn = document.createElement('button');
            btn.type = 'button';
            btn.className = 'reldate-quick-btn' + (initialVal === qp.value ? ' active' : '');
            btn.textContent = qp.label;
            btn.addEventListener('click', () => {
                textInput.value = qp.value;
                Array.from(quickRow.children).forEach(c => c.classList.remove('active'));
                btn.classList.add('active');
                textInput.dispatchEvent(new Event('change'));
            });
            quickRow.appendChild(btn);
        });
        textInput.addEventListener('change', () => {
            if (/^\d{4}-\d{2}-\d{2}$/.test(textInput.value)) {
                hiddenDate.value = textInput.value;
            }
            onValChange();
        });
        rowWrapper.appendChild(inputRow);
        rowWrapper.appendChild(quickRow);
        return { rowWrapper, inputRow, textInput, quickRow };
    }
    if (isRange) {
        let startVal = '';
        let endVal = '';
        if (manifest && manifest.parameters) {
            if (param)
                startVal = getParam(manifest.parameters, param) ?? '';
            if (secondaryParam)
                endVal = getParam(manifest.parameters, secondaryParam) ?? '';
        }
        if (!startVal && !endVal) {
            const def = (visual.defaultValue || opts['DEFAULT'] || opts['default'] || '');
            const parts = parseMultiParameter(def);
            if (parts.length > 0)
                startVal = parts[0];
            if (parts.length > 1)
                endVal = parts[1];
        }
        function validateAndPostRange() {
            const sVal = startRow.textInput.value.trim();
            const eVal = endRow.textInput.value.trim();
            let errMsg = null;
            if (!isValidRelDate(sVal)) {
                errMsg = 'Invalid start relative date expression';
                startRow.inputRow.classList.add('is-invalid');
            }
            else {
                startRow.inputRow.classList.remove('is-invalid');
            }
            if (!isValidRelDate(eVal)) {
                errMsg = errMsg ? (errMsg + '; Invalid end relative date expression') : 'Invalid end relative date expression';
                endRow.inputRow.classList.add('is-invalid');
            }
            else {
                endRow.inputRow.classList.remove('is-invalid');
            }
            if (errMsg) {
                errorEl.textContent = errMsg;
                errorEl.style.display = 'block';
                return;
            }
            errorEl.style.display = 'none';
            startRow.inputRow.classList.remove('is-invalid');
            endRow.inputRow.classList.remove('is-invalid');
            if (isWebMode && changeActions.length > 0) {
                const batch = {};
                if (param)
                    batch[param] = sVal;
                if (secondaryParam)
                    batch[secondaryParam] = eVal;
                if (Object.keys(batch).length > 0) {
                    postParameters(batch).then(m => { if (m)
                        renderManifest(m); });
                }
            }
        }
        const rangeContainer = document.createElement('div');
        rangeContainer.className = 'datepicker-range-wrapper';
        const startRow = createRelDateRow(startVal, param, 'start date', validateAndPostRange);
        const sep = document.createElement('span');
        sep.textContent = '–';
        sep.style.fontWeight = 'bold';
        const endRow = createRelDateRow(endVal, secondaryParam, 'end date', validateAndPostRange);
        rangeContainer.appendChild(startRow.rowWrapper);
        rangeContainer.appendChild(sep);
        rangeContainer.appendChild(endRow.rowWrapper);
        wrapper.appendChild(rangeContainer);
        wrapper.appendChild(errorEl);
    }
    else {
        // SINGLE mode
        let def = (visual.defaultValue || opts['DEFAULT'] || opts['default'] || '');
        if (param && manifest && manifest.parameters) {
            const current = getParam(manifest.parameters, param);
            if (current !== undefined)
                def = current;
        }
        function validateAndPostSingle() {
            const val = singleRow.textInput.value.trim();
            if (!isValidRelDate(val)) {
                singleRow.inputRow.classList.add('is-invalid');
                errorEl.textContent = 'Invalid relative date expression';
                errorEl.style.display = 'block';
                return;
            }
            singleRow.inputRow.classList.remove('is-invalid');
            errorEl.style.display = 'none';
            if (isWebMode && changeActions.length > 0) {
                const batch = changeActions.reduce((o, a) => { o[a.parameterName] = val; return o; }, {});
                postParameters(batch).then(m => { if (m)
                    renderManifest(m); });
            }
        }
        const singleRow = createRelDateRow(def, param, 'relative date', validateAndPostSingle);
        wrapper.appendChild(singleRow.rowWrapper);
        wrapper.appendChild(errorEl);
    }
    container.appendChild(wrapper);
}
