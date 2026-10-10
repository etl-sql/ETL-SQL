/* GENERATED TYPESCRIPT OUTPUT - DO NOT EDIT.
 * Source: src/ETL-SQL.Portal/BrowserSources/portal-authoring.ts
 * Run: node scripts/sync-assets.js
 */
// @ts-nocheck — generated implementation; check the TypeScript owner.
import { PortalAuthoringError } from './authoring-responses.js';
import { asError } from './http-error.js';
export async function readAuthoringJson(response) {
    if (response.status === 401)
        throw new PortalAuthoringError(401, null, 'The Portal session ended while opening the report.');
    if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new PortalAuthoringError(response.status, problem, `Request failed (${response.status}).`);
    }
    return response.status === 204 ? null : response.json();
}
/** Failed loading must leave the author with a retry action and no empty report to save. */
export function showAuthoringError(host, error, retry) {
    const stage = document.createElement('div');
    stage.className = 'etlsql-studio-error-stage';
    stage.setAttribute('role', 'alert');
    stage.style.cssText = 'display:flex;flex-direction:column;align-items:center;justify-content:center;height:100%;min-height:400px;padding:32px;text-align:center;';
    const title = document.createElement('h2');
    title.textContent = 'Unable to load authoring session';
    title.style.cssText = 'font-size:18px;font-weight:600;margin-bottom:8px;';
    const details = document.createElement('p');
    details.textContent = asError(error).message;
    details.style.cssText = 'color:var(--portal-muted,#7a8798);max-width:480px;margin-bottom:20px;font-size:14px;line-height:1.5;';
    const button = document.createElement('button');
    button.type = 'button';
    button.id = 'retrySessionBtn';
    button.className = 'btn btn-primary';
    button.textContent = 'Retry Connection';
    button.addEventListener('click', retry);
    stage.append(title, details, button);
    host.replaceChildren(stage);
}
