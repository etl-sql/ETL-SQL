import * as vscode from 'vscode';
import * as path from 'path';
import * as https from 'https';
import * as http from 'http';
import { URL } from 'url';

interface FolderItem { id: number; path: string; }
interface HttpResult  { status: number; data: any; }

function httpRequest(method: string, urlStr: string, body: any, token?: string): Promise<HttpResult> {
    return new Promise((resolve, reject) => {
        const url     = new URL(urlStr);
        const isHttps = url.protocol === 'https:';
        const bodyStr = body ? JSON.stringify(body) : undefined;

        const options: http.RequestOptions = {
            hostname: url.hostname,
            port:     url.port || (isHttps ? 443 : 80),
            path:     url.pathname + url.search,
            method,
            headers: {
                'Content-Type': 'application/json',
                ...(bodyStr ? { 'Content-Length': Buffer.byteLength(bodyStr) } : {}),
                ...(token   ? { 'Authorization': `Bearer ${token}` }           : {})
            }
        };

        const lib = isHttps ? https : http;
        const req = lib.request(options, res => {
            let data = '';
            res.on('data', chunk => data += chunk);
            res.on('end', () => {
                try   { resolve({ status: res.statusCode ?? 0, data: data ? JSON.parse(data) : null }); }
                catch { resolve({ status: res.statusCode ?? 0, data }); }
            });
        });

        req.on('error', reject);
        if (bodyStr) req.write(bodyStr);
        req.end();
    });
}

async function ensureAuthenticated(context: vscode.ExtensionContext, portalUrl: string): Promise<string | null> {
    const tokenKey  = `portalToken_${portalUrl}`;
    const expiryKey = `portalTokenExpiry_${portalUrl}`;

    const token  = context.globalState.get<string>(tokenKey);
    const expiry = context.globalState.get<number>(expiryKey, 0);

    if (token && Date.now() < expiry) {
        return token;
    }

    const username = await vscode.window.showInputBox({ prompt: 'Portal username', ignoreFocusOut: true });
    if (username === undefined) return null;

    const password = await vscode.window.showInputBox({ prompt: 'Portal password', password: true, ignoreFocusOut: true });
    if (password === undefined) return null;

    try {
        const res = await httpRequest('POST', `${portalUrl}/api/auth/login`, { username, password });
        if (res.status === 401 || !res.data?.token) {
            vscode.window.showErrorMessage('Login failed: invalid credentials.');
            return null;
        }
        if (res.status !== 200) {
            vscode.window.showErrorMessage(`Login failed: ${res.data?.message ?? `HTTP ${res.status}`}`);
            return null;
        }
        const accessToken = res.data.token as string;
        await context.globalState.update(tokenKey,  accessToken);
        await context.globalState.update(expiryKey, Date.now() + 55 * 60 * 1000);
        return accessToken;
    } catch (e: any) {
        vscode.window.showErrorMessage(`Could not connect to portal: ${e.message}`);
        return null;
    }
}

function flattenFolders(nodes: any[], prefix = ''): FolderItem[] {
    const result: FolderItem[] = [];
    for (const node of nodes ?? []) {
        const p = prefix ? `${prefix}/${node.name}` : `/${node.name}`;
        result.push({ id: node.id, path: p });
        if (node.children?.length) {
            result.push(...flattenFolders(node.children, p));
        }
    }
    return result.sort((a, b) => a.path.localeCompare(b.path));
}

export async function publishToPortal(context: vscode.ExtensionContext, filePath: string): Promise<void> {
    const config = vscode.workspace.getConfiguration('etlsql');
    let portalUrl = (config.get<string>('portal.url') || '').replace(/\/$/, '');

    if (!portalUrl) {
        const entered = await vscode.window.showInputBox({
            prompt: 'Enter the Report Portal URL (e.g. http://localhost:5001)',
            ignoreFocusOut: true
        });
        if (!entered) {
            vscode.window.showWarningMessage('Publish cancelled. Set etlsql.portal.url in settings to skip this prompt.');
            return;
        }
        portalUrl = entered.replace(/\/$/, '');
        await config.update('portal.url', portalUrl, vscode.ConfigurationTarget.Global);
    }

    const token = await ensureAuthenticated(context, portalUrl);
    if (!token) return;

    // Resolve the script path relative to the portal's script root.
    // The portal runs on a different filesystem (e.g. Docker), so we can't
    // send the local absolute path — we pick from scripts it already knows about.
    let scriptPath: string;
    try {
        const res = await httpRequest('GET', `${portalUrl}/api/reports/available-scripts`, null, token);
        if (res.status !== 200) {
            vscode.window.showErrorMessage(`Could not fetch available scripts: HTTP ${res.status}`);
            return;
        }
        const available: string[] = Array.isArray(res.data) ? res.data : [];
        if (available.length === 0) {
            vscode.window.showErrorMessage(
                'No scripts found in the portal\'s script directory. ' +
                'Copy your .rptsql file into the portal\'s configured ScriptRootPath first.');
            return;
        }
        const localName = path.basename(filePath);
        const preselect = available.find(s => path.basename(s) === localName);
        const picked = await vscode.window.showQuickPick(
            available.map(s => ({ label: s, description: s === preselect ? '(matches open file)' : '' })),
            { placeHolder: 'Select portal script to publish', ignoreFocusOut: true }
        );
        if (!picked) return;
        scriptPath = picked.label;
    } catch (e: any) {
        vscode.window.showErrorMessage(`Could not fetch available scripts: ${e.message}`);
        return;
    }

    const defaultName = path.basename(filePath).replace(/\.(rptsql|rpt)$/i, '');
    const reportName  = await vscode.window.showInputBox({ prompt: 'Report name', value: defaultName, ignoreFocusOut: true });
    if (!reportName) return;

    let folders: FolderItem[];
    try {
        const res = await httpRequest('GET', `${portalUrl}/api/folders`, null, token);
        if (res.status !== 200) {
            vscode.window.showErrorMessage(`Could not fetch folders: HTTP ${res.status}`);
            return;
        }
        folders = flattenFolders(Array.isArray(res.data) ? res.data : [res.data]);
    } catch (e: any) {
        vscode.window.showErrorMessage(`Could not fetch folders: ${e.message}`);
        return;
    }

    if (folders.length === 0) {
        vscode.window.showErrorMessage('No folders available. Ensure you have Manage permission on at least one folder.');
        return;
    }

    const picked = await vscode.window.showQuickPick(
        folders.map(f => ({ label: f.path, folderId: f.id })),
        { placeHolder: 'Select destination folder', ignoreFocusOut: true }
    );
    if (!picked) return;

    const description = await vscode.window.showInputBox({ prompt: 'Description (optional — press Enter to skip)', ignoreFocusOut: true });
    if (description === undefined) return;

    try {
        const res = await httpRequest('POST', `${portalUrl}/api/reports`, {
            folderId:    picked.folderId,
            name:        reportName,
            scriptPath:  scriptPath,
            description: description || ''
        }, token);

        if (res.status === 200 || res.status === 201) {
            vscode.window.showInformationMessage(`"${reportName}" published successfully.`);
        } else if (res.status === 403) {
            vscode.window.showErrorMessage('Publish failed: insufficient permissions on the selected folder.');
        } else if (res.status === 400) {
            const msg = res.data?.message || res.data?.title || `HTTP 400`;
            if (typeof msg === 'string' && msg.toLowerCase().includes('path')) {
                vscode.window.showErrorMessage("Publish failed: file path is outside the Portal's configured root directory.");
            } else {
                vscode.window.showErrorMessage(`Publish failed: ${msg}`);
            }
        } else {
            vscode.window.showErrorMessage(`Publish failed: HTTP ${res.status}`);
        }
    } catch (e: any) {
        vscode.window.showErrorMessage(`Publish failed: ${e.message}`);
    }
}
