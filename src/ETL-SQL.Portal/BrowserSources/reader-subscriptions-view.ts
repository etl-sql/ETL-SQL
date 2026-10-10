import { readAdminSubscription } from './admin-subscriptions-responses.js';

/** Response readers and row markup for a reader's subscriptions. Pure, so they can be checked without a browser. */
export function esc(value: unknown): string {
    return String(value ?? '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}
const when = (value: string | null | undefined, none: string) => value ? new Date(value).toLocaleString() : none;

export function readSubscriptionList(value: unknown): SubscriptionDto[] {
    if (!Array.isArray(value)) throw new TypeError('The subscription list is unavailable or incomplete.');
    const subscriptions = value.map((item: unknown) => readAdminSubscription(item));
    if (new Set(subscriptions.map(item => item.id)).size !== subscriptions.length)
        throw new TypeError('The subscription list is unavailable or incomplete.');
    return subscriptions;
}

export function readSmtpAliases(value: unknown): string[] {
    if (!Array.isArray(value) || !value.every((alias): alias is string => typeof alias === 'string' && alias.trim().length > 0))
        throw new TypeError('The delivery connection list is unavailable or incomplete.');
    return value;
}

export function renderSubscriptionRows(subscriptions: SubscriptionDto[], pending: ReadonlySet<number>): string {
    return subscriptions.map(item => {
        const label = esc(item.name || item.reportName), locked = pending.has(item.id) ? ' disabled' : '';
        return `<tr data-subscription-id="${item.id}">
          <td><div class="sub-name">${label}</div><div class="sub-report">${esc(item.reportName)}</div></td>
          <td><span class="sub-format">${esc(item.format)}</span>
            <div class="sub-report">${esc(item.recipients || (item.format === 'Link' ? 'Portal link' : 'Profile email'))}</div></td>
          <td><div>${esc(item.schedule || (item.deliverOnRefresh ? 'On refresh' : 'Manual'))}</div>
            <div class="sub-report">Next: ${esc(when(item.nextRunAt, 'Not scheduled'))}</div></td>
          <td class="text-sm text-muted">${esc(item.parameterSummary || 'Default parameters')}</td>
          <td><span class="chip ${item.isActive ? 'chip-active' : 'chip-inactive'}">${item.isActive ? 'Active' : 'Paused'}</span>
            ${item.failCount ? `<div class="sub-warning">${item.failCount} failed send${item.failCount === 1 ? '' : 's'}</div>`
                : `<div class="sub-report">Last: ${esc(when(item.lastSentAt, 'Not sent yet'))}</div>`}</td>
          <td><div class="table-actions">
            <button class="btn btn-outline btn-sm" type="button" data-action="edit-params" data-id="${item.id}"${locked}>Edit Params</button>
            <button class="btn btn-outline btn-sm" type="button" data-action="history" data-id="${item.id}">History</button>
            <button class="btn btn-outline btn-sm" type="button" data-action="toggle" data-id="${item.id}"${locked}>${item.isActive ? 'Pause' : 'Resume'}</button>
            <button class="btn btn-outline btn-sm btn-danger-soft" type="button" data-action="delete" data-id="${item.id}"${locked}>Delete</button>
          </div></td></tr>`;
    }).join('');
}
