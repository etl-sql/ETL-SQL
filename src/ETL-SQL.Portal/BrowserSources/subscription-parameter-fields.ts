/**
 * The input rows for a report's parameters, shared by every dialog that collects them.
 *
 * Rows are built with DOM calls, so a parameter name, description, or stored value is always text.
 * A value the user has not supplied falls back to the definition's default; a required parameter
 * with neither is reported with its input, and nothing is collected.
 */
export interface ParameterInput { parameter: ReportParameterDto; input: HTMLInputElement }
export type ParameterDraft = { values: Record<string, string> } | { missing: ParameterInput };

const RELATIVE_DATES = ['D-0', 'D-1', 'D-7', 'D-30', 'M-1', 'M-3', 'Y-1'];

export function renderParameterInputs(doc: Document, container: HTMLElement, definitions: ReportParameterDto[],
    values: Record<string, string>, idPrefix: string, signal: AbortSignal): ParameterInput[] {
    container.replaceChildren();
    return definitions.map((parameter, index) => {
        const row = doc.createElement('div'); row.className = 'param-row';
        const heading = doc.createElement('div'); heading.className = 'param-heading';
        const input = doc.createElement('input'); input.className = 'param-input'; input.type = 'text';
        input.id = `${idPrefix}-${index}`;
        input.value = Object.hasOwn(values, parameter.name) ? values[parameter.name]! : parameter.default ?? '';
        input.setAttribute('aria-required', String(parameter.required));
        const label = doc.createElement('label'); label.className = 'param-label'; label.htmlFor = input.id; label.textContent = parameter.name;
        const type = doc.createElement('span'); type.className = 'param-type'; type.textContent = parameter.type;
        const requirement = doc.createElement('span'); requirement.className = parameter.required ? 'required-marker' : 'optional-marker';
        requirement.textContent = parameter.required ? 'Required' : 'Optional';
        heading.append(label, type, requirement); row.append(heading);
        if (parameter.description) {
            const hint = doc.createElement('div'); hint.className = 'param-hint'; hint.id = `${input.id}-hint`;
            hint.textContent = parameter.description; input.setAttribute('aria-describedby', hint.id); row.append(hint);
        }
        row.append(input);
        if (parameter.type.toUpperCase() === 'RELDATE') {
            input.placeholder = 'e.g. D-1, M-1, Y-1';
            const picks = doc.createElement('div'); picks.className = 'reldate-quickpicks';
            for (const value of RELATIVE_DATES) {
                const button = doc.createElement('button'); button.type = 'button'; button.textContent = value === 'D-0' ? 'Today' : value;
                button.addEventListener('click', () => { input.value = value; input.focus(); }, { signal });
                picks.append(button);
            }
            // A fixed calendar date is also a valid value for a relative-date parameter.
            const calendar = doc.createElement('input'); calendar.type = 'date'; calendar.className = 'param-date';
            calendar.setAttribute('aria-label', `Choose a calendar date for ${parameter.name}`);
            calendar.addEventListener('change', () => { if (calendar.value) input.value = calendar.value; }, { signal });
            row.append(picks, calendar);
        }
        container.append(row);
        return { parameter, input };
    });
}

/** Collects the entered values, or names the first required parameter that has neither a value nor a default. */
export function readParameterDraft(inputs: ParameterInput[]): ParameterDraft {
    const values: Record<string, string> = Object.create(null);
    for (const entry of inputs) {
        const value = entry.input.value.trim();
        const missing = !value && entry.parameter.required && !entry.parameter.default?.trim();
        entry.input.classList.toggle('param-input-error', missing);
        entry.input.closest('.param-row')?.classList.toggle('param-row-error', missing);
        if (missing) return { missing: entry };
        if (value) values[entry.parameter.name] = value;
    }
    return { values };
}
